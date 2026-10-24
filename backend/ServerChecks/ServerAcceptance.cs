using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ras.Backend.ServerChecks;

internal sealed record AcceptanceReport(
    string State,
    string ServerRuntime,
    string WorkspaceId,
    string ProjectPath,
    string PlanPath,
    string ResultPath,
    string ResultRevision,
    int ProgressEventCount,
    int SourceMessageCount,
    int ProfileCount,
    double MinimumCellDepth,
    double MaximumCellDepth,
    double ExpectedRainfallDepthMetres,
    bool WorkerFailureRecovery);

internal static partial class ServerAcceptance
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(15);

    public static async Task<AcceptanceReport> RunAsync(Uri baseAddress, FixtureManifest fixture)
    {
        if (!baseAddress.IsLoopback || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("The server acceptance runner only connects to a loopback HTTP(S) URL.", nameof(baseAddress));

        using var timeout = new CancellationTokenSource(OperationTimeout);
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", timeout.Token);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The server health endpoint did not report ready.");

        using JsonDocument runtime = await GetJsonAsync(client, "api/runtime", timeout.Token);
        JsonElement operations = Property(runtime.RootElement, "operations");
        if (!operations.EnumerateArray().Any(item => item.GetString() == "plan.compute.inspect") ||
            !operations.EnumerateArray().Any(item => item.GetString() == "plan.compute.execute"))
            throw new InvalidDataException("The server runtime does not expose the real Plan compute operations.");

        JsonElement upload = await UploadFixtureAsync(client, fixture, timeout.Token);
        string workspaceID = StringProperty(upload, "workspaceId");
        string uploadedRoot = Path.GetFullPath(StringProperty(upload, "rootPath"));
        JsonElement projects = Property(upload, "projects");
        JsonElement projectEntry = projects.EnumerateArray().SingleOrDefault();
        if (projectEntry.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The server did not discover exactly one uploaded .ras project.");
        string projectPath = Path.GetFullPath(StringProperty(projectEntry, "path"));
        string planPath = UploadedPath(uploadedRoot, fixture.RootPath, fixture.PlanPath);
        string[] requiredSourceFiles =
        [
            projectPath,
            planPath,
            UploadedPath(uploadedRoot, fixture.RootPath, fixture.GeometryPath),
            UploadedPath(uploadedRoot, fixture.RootPath, fixture.BoundaryPath),
            UploadedPath(uploadedRoot, fixture.RootPath, fixture.TerrainPath),
        ];
        int uploadedFileCount = IntProperty(upload, "fileCount");
        int fixtureFileCount = Directory.EnumerateFiles(fixture.RootPath, "*", SearchOption.AllDirectories).Count();
        if (uploadedFileCount != fixtureFileCount || requiredSourceFiles.Any(path => !File.Exists(path)))
            throw new FileNotFoundException("The uploaded workspace is missing files needed by its real Project, Plan, Geometry, Boundary, or terrain source.");

        OperationReceipt loadedOperation = await SubmitOperationAsync(client, "model.load", new
        {
            assetPath = planPath,
            kind = "Plan",
            projectPath,
        }, timeout.Token);
        JsonElement loaded = loadedOperation.Result;
        string revision = StringProperty(loaded, "revision");
        object model = new
        {
            assetPath = planPath,
            kind = "Plan",
            projectPath,
            revision,
            fields = new Dictionary<string, string>(),
            images = new Dictionary<string, object[]>(),
            projectXml = await File.ReadAllTextAsync(projectPath, timeout.Token),
            sourceState = OptionalString(loaded, "sourceState"),
        };

        OperationReceipt inspectOperation = await SubmitOperationAsync(client, "plan.compute.inspect", new { model }, timeout.Token);
        JsonElement inspection = inspectOperation.Result;
        if (!BooleanProperty(inspection, "canExecute"))
            throw new InvalidOperationException("The uploaded source Plan is not ready for CPU compute: " +
                JsonSerializer.Serialize(PropertyOrNull(inspection, "messages"), JsonOptions));
        string inspectionID = StringProperty(inspection, "inspectionId");

        OperationReceipt executeOperation = await SubmitOperationAsync(client, "plan.compute.execute", new
        {
            model,
            inspectionId = inspectionID,
        }, timeout.Token);
        JsonElement computed = executeOperation.Result;
        string computeState = StringProperty(computed, "state");
        if (!string.Equals(computeState, "completed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The real source Plan compute ended in state " + computeState + ": " +
                OptionalString(computed, "errorMessage"));
        JsonElement summary = Property(computed, "summary");
        if (!BooleanProperty(summary, "success"))
            throw new InvalidDataException("The completed operation omitted a successful source Result.Compute summary.");

        string resultPath = Path.GetFullPath(StringProperty(computed, "resultPath"));
        string resultRevision = StringProperty(computed, "resultRevision");
        if (!File.Exists(resultPath)) throw new FileNotFoundException("The source compute Result HDF was not retained.", resultPath);
        string diskRevision = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(resultPath, timeout.Token)));
        if (!string.Equals(diskRevision, resultRevision, StringComparison.Ordinal))
            throw new InvalidDataException("The Result HDF bytes do not match the source compute revision.");

        JsonElement sourceMessages = Property(computed, "messages");
        if (sourceMessages.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The Plan compute response omitted its source message array.");
        int progressCount = executeOperation.Events.Count(HasUsefulProgress);
        if (progressCount == 0)
            throw new InvalidDataException("The Plan compute operation returned no source worker progress with a fraction, task, or telemetry values.");
        int messageCount = sourceMessages.GetArrayLength() + CountSourceMessageEvents(executeOperation.Events);

        string projectXml = await File.ReadAllTextAsync(projectPath, timeout.Token);
        OperationReceipt resultInspectOperation = await SubmitOperationAsync(client, "result.inspect", new
        {
            projectPath,
            projectXml,
            resultPath,
            revision = resultRevision,
        }, timeout.Token);
        JsonElement resultInspector = resultInspectOperation.Result;
        int profileCount = IntProperty(resultInspector, "profileCount");
        if (!BooleanProperty(resultInspector, "hasComputed") || profileCount <= 0 ||
            !BooleanProperty(Property(resultInspector, "computeSummary"), "success"))
            throw new InvalidDataException("Reopening the written Result HDF did not recover computed profiles and summary metadata.");

        OperationReceipt catalogOperation = await SubmitOperationAsync(client, "result.profileCatalog", new
        {
            resultPath,
            revision = resultRevision,
        }, timeout.Token);
        JsonElement catalog = catalogOperation.Result;
        JsonElement outputBlock = Property(catalog, "outputBlocks").EnumerateArray().FirstOrDefault();
        if (outputBlock.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Reopening the Result HDF found no stored output block.");
        JsonElement depthVariable = Property(outputBlock, "variables").EnumerateArray()
            .FirstOrDefault(variable => StringProperty(variable, "name") == "Cell Depth");
        if (depthVariable.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The reopened Result HDF has no Cell Depth series.");
        JsonElement flowArea = Property(catalog, "flowAreas").EnumerateArray().FirstOrDefault();
        if (flowArea.ValueKind != JsonValueKind.Object || IntProperty(flowArea, "cellCount") < 1)
            throw new InvalidDataException("The reopened Result HDF has no cell mesh for numeric readback.");

        OperationReceipt profileOperation = await SubmitOperationAsync(client, "result.profile", new
        {
            resultPath,
            revision = resultRevision,
            outputBlockName = StringProperty(outputBlock, "name"),
            variable = StringProperty(depthVariable, "name"),
            flowAreaIndex = IntProperty(flowArea, "index"),
            elementIndex = 0,
        }, timeout.Token);
        JsonElement profile = profileOperation.Result;
        if (!BooleanProperty(profile, "available"))
            throw new InvalidDataException("The reopened Result HDF profile read was unavailable: " + OptionalString(profile, "unavailableReason"));
        double[] depths = Property(profile, "points").EnumerateArray()
            .Select(point => PropertyOrNull(point, "value"))
            .Where(value => value.ValueKind == JsonValueKind.Number)
            .Select(value => value.GetDouble())
            .Where(double.IsFinite)
            .ToArray();
        if (depths.Length != profileCount || !depths.Any(value => value > 0))
            throw new InvalidDataException("Reopened Result HDF cell depths are missing, incomplete, or never wet.");
        double initialDepth = fixture.InitialStage - fixture.TerrainElevation;
        double expectedFinalDepth = initialDepth + fixture.ExpectedRainfallDepthMetres;
        const double solverDepthToleranceMetres = 0.0001;
        if (depths.Any(depth => Math.Abs(depth - initialDepth) > solverDepthToleranceMetres) ||
            Math.Abs(depths[^1] - expectedFinalDepth) > solverDepthToleranceMetres)
            throw new InvalidDataException($"The reopened closed-cell depth is outside the expected 1 m envelope (expected rainfall {fixture.ExpectedRainfallDepthMetres:G} m, tolerance {solverDepthToleranceMetres:G} m): min={depths.Min():G}, max={depths.Max():G}.");

        await VerifyWorkerFailureRecoveryAsync(client, uploadedRoot, timeout.Token);
        return new AcceptanceReport(computeState, StringProperty(runtime.RootElement, "runtime"),
            workspaceID, projectPath, planPath, resultPath, resultRevision, progressCount,
            messageCount, profileCount, depths.Min(), depths.Max(), fixture.ExpectedRainfallDepthMetres,
            WorkerFailureRecovery: true);
    }

    private static async Task VerifyWorkerFailureRecoveryAsync(HttpClient client, string root, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using HttpResponseMessage accepted = await client.PostAsJsonAsync("api/operations", new
        {
            method = "project.open", parameters = new { projectPath = Path.Combine(root, "missing-project.ras") },
        }, JsonOptions, deadline.Token);
        if (accepted.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidDataException("The failure recovery check was not accepted as a worker task.");
        using JsonDocument submission = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync(deadline.Token));
        string id = StringProperty(submission.RootElement, "id");
        long cursor = 0;
        while (true)
        {
            using JsonDocument poll = await GetJsonAsync(client, $"api/operations/{id}?after={cursor}", deadline.Token);
            JsonElement pollRoot = poll.RootElement;
            cursor = Math.Max(cursor, LongPropertyOrDefault(pollRoot, "cursor", cursor));
            if (BooleanProperty(pollRoot, "hasMore")) continue;
            string state = StringProperty(pollRoot, "state");
            if (state == "failed")
            {
                if (StringProperty(Property(pollRoot, "error"), "code") != "notFound")
                    throw new InvalidDataException("The worker did not preserve the expected missing-project error.");
                break;
            }
            if (IsTerminal(state)) throw new InvalidDataException("The missing project was unexpectedly accepted.");
            await Task.Delay(50, deadline.Token);
        }
        // A failed worker must close and release the serialized execution slot.
        _ = await SubmitOperationAsync(client, "backend.info", new { }, deadline.Token);
    }

    private static async Task<JsonDocument> GetWhenReadyAsync(HttpClient client, string path, CancellationToken cancellation)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { return await GetJsonAsync(client, path, cancellation); }
            catch (HttpRequestException) when (elapsed.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(250, cancellation);
            }
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.GetAsync(path, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GET {path} returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        return JsonDocument.Parse(body);
    }

    private static async Task<JsonElement> UploadFixtureAsync(HttpClient client, FixtureManifest fixture, CancellationToken cancellation)
    {
        byte[] archive = ZipDirectory(fixture.RootPath);
        using var multipart = new MultipartFormDataContent();
        using var stream = new MemoryStream(archive, writable: false);
        using var zipContent = new StreamContent(stream);
        zipContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        multipart.Add(zipContent, "files", "plan-compute-fixture.zip");
        using HttpResponseMessage response = await client.PostAsync("api/workspaces", multipart, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new HttpRequestException($"POST api/workspaces returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static byte[] ZipDirectory(string root)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                ZipArchiveEntry entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                using Stream destination = entry.Open();
                using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                source.CopyTo(destination);
            }
        }
        return buffer.ToArray();
    }

    private static string UploadedPath(string uploadedRoot, string sourceRoot, string sourcePath)
    {
        string relative = Path.GetRelativePath(sourceRoot, sourcePath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidDataException("A fixture asset escaped its upload root.");
        return Path.GetFullPath(Path.Combine(uploadedRoot, relative));
    }

    private static async Task<OperationReceipt> SubmitOperationAsync(HttpClient client, string method,
        object parameters, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("api/operations", new { method, parameters }, JsonOptions, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new HttpRequestException($"POST api/operations ({method}) returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        using JsonDocument submitted = JsonDocument.Parse(body);
        string id = StringProperty(submitted.RootElement, "id");
        return await WaitForOperationAsync(client, id, cancellation);
    }

    private static async Task<OperationReceipt> WaitForOperationAsync(HttpClient client, string id, CancellationToken cancellation)
    {
        long cursor = 0;
        var events = new List<JsonElement>();
        while (true)
        {
            using JsonDocument snapshot = await GetJsonAsync(client,
                $"api/operations/{Uri.EscapeDataString(id)}?after={cursor}", cancellation);
            JsonElement root = snapshot.RootElement;
            if (PropertyOrNull(root, "events") is { ValueKind: JsonValueKind.Array } newEvents)
            {
                foreach (JsonElement item in newEvents.EnumerateArray()) events.Add(item.Clone());
            }
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            bool hasMore = BooleanProperty(root, "hasMore");
            if (hasMore) continue;
            string state = StringProperty(root, "state");
            if (IsTerminal(state))
            {
                JsonElement result = PropertyOrNull(root, "result");
                JsonElement error = PropertyOrNull(root, "error");
                if (!string.Equals(state, "completed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(state, "succeeded", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Server operation {id} ended in {state}: {JsonSerializer.Serialize(error, JsonOptions)}");
                if (result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                    throw new InvalidDataException($"Completed server operation {id} omitted its result payload.");
                return new OperationReceipt(id, state, result.Clone(), events.ToArray(), cursor);
            }
            await Task.Delay(150, cancellation);
        }
    }

    private static bool IsTerminal(string state) => state.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("succeeded", StringComparison.OrdinalIgnoreCase) || state.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("cancelled", StringComparison.OrdinalIgnoreCase) || state.Equals("canceled", StringComparison.OrdinalIgnoreCase);

    private static bool HasUsefulProgress(JsonElement item)
    {
        JsonElement progress = PropertyOrNull(item, "progress");
        if (progress.ValueKind != JsonValueKind.Object) return false;
        if (PropertyOrNull(progress, "fraction").ValueKind == JsonValueKind.Number ||
            PropertyOrNull(progress, "taskName").ValueKind == JsonValueKind.String ||
            PropertyOrNull(progress, "message").ValueKind == JsonValueKind.String)
            return true;
        JsonElement values = PropertyOrNull(progress, "values");
        return values.ValueKind == JsonValueKind.Object && values.EnumerateObject().Any();
    }

    private static int CountSourceMessageEvents(IEnumerable<JsonElement> events) => events.Sum(item =>
    {
        JsonElement planMessages = PropertyOrNull(PropertyOrNull(item, "progress"), "planMessages");
        JsonElement messages = PropertyOrNull(planMessages, "messages");
        return messages.ValueKind == JsonValueKind.Array ? messages.GetArrayLength() : 0;
    });

    private static string StringProperty(JsonElement value, string name) =>
        Property(value, name).GetString() ?? throw new InvalidDataException($"JSON property '{name}' is null.");

    private static string? OptionalString(JsonElement value, string name)
    {
        JsonElement property = PropertyOrNull(value, name);
        return property.ValueKind is JsonValueKind.String ? property.GetString() : null;
    }

    private static bool BooleanProperty(JsonElement value, string name) => Property(value, name).GetBoolean();
    private static int IntProperty(JsonElement value, string name) => Property(value, name).GetInt32();

    private static long LongPropertyOrDefault(JsonElement value, string name, long fallback)
    {
        JsonElement property = PropertyOrNull(value, name);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out long number) ? number : fallback;
    }

    private static JsonElement Property(JsonElement value, string name)
    {
        JsonElement property = PropertyOrNull(value, name);
        return property.ValueKind == JsonValueKind.Undefined
            ? throw new InvalidDataException($"JSON property '{name}' is missing.") : property;
    }

    private static JsonElement PropertyOrNull(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property)
            ? property : default;

    private sealed record OperationReceipt(string Id, string State, JsonElement Result, JsonElement[] Events, long Cursor);
}
