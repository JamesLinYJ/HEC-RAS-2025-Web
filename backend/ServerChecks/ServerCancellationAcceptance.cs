using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ras.Backend.ServerChecks;

internal sealed record CancellationAcceptanceReport(
    string State,
    string WorkspaceId,
    string OperationId,
    string PlanPath,
    int ProgressEventCount,
    int SolverStepAtCancellation,
    bool CancelRequested,
    string? ResultState,
    bool? ResultSummarySuccess,
    bool FollowUpBackendRequestCompleted);

internal static partial class ServerAcceptance
{
    private static readonly TimeSpan CancellationTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SolverProgressTimeout = TimeSpan.FromSeconds(90);

    public static async Task<CancellationAcceptanceReport> RunCancellationAsync(Uri baseAddress,
        FixtureManifest fixture)
    {
        if (!baseAddress.IsLoopback || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("The server cancellation runner only connects to a loopback HTTP(S) URL.", nameof(baseAddress));

        using var timeout = new CancellationTokenSource(CancellationTimeout);
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
        string workspaceId = StringProperty(upload, "workspaceId");
        string uploadedRoot = Path.GetFullPath(StringProperty(upload, "rootPath"));
        JsonElement projectEntry = Property(upload, "projects").EnumerateArray().SingleOrDefault();
        if (projectEntry.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The server did not discover exactly one uploaded cancellation Plan project.");
        string projectPath = Path.GetFullPath(StringProperty(projectEntry, "path"));
        string planPath = UploadedPath(uploadedRoot, fixture.RootPath, fixture.PlanPath);
        string[] requiredFiles =
        [
            projectPath,
            planPath,
            UploadedPath(uploadedRoot, fixture.RootPath, fixture.GeometryPath),
            UploadedPath(uploadedRoot, fixture.RootPath, fixture.BoundaryPath),
            UploadedPath(uploadedRoot, fixture.RootPath, fixture.TerrainPath),
        ];
        if (IntProperty(upload, "fileCount") != Directory.EnumerateFiles(fixture.RootPath, "*", SearchOption.AllDirectories).Count() ||
            requiredFiles.Any(path => !File.Exists(path)))
            throw new FileNotFoundException("The uploaded cancellation fixture is missing a project, Plan, Geometry, Boundary, or terrain file.");

        OperationReceipt load = await SubmitOperationAsync(client, "model.load", new
        {
            assetPath = planPath,
            kind = "Plan",
            projectPath,
        }, timeout.Token);
        JsonElement loaded = load.Result;
        var model = new
        {
            assetPath = planPath,
            kind = "Plan",
            projectPath,
            revision = StringProperty(loaded, "revision"),
            fields = new Dictionary<string, string>(),
            images = new Dictionary<string, object[]>(),
            projectXml = await File.ReadAllTextAsync(projectPath, timeout.Token),
            sourceState = OptionalString(loaded, "sourceState"),
        };

        OperationReceipt inspectionOperation = await SubmitOperationAsync(client, "plan.compute.inspect",
            new { model }, timeout.Token);
        JsonElement inspection = inspectionOperation.Result;
        if (!BooleanProperty(inspection, "canExecute"))
            throw new InvalidOperationException("The cancellation Plan cannot execute on the CPU: " +
                JsonSerializer.Serialize(PropertyOrNull(inspection, "messages"), JsonOptions));

        using HttpResponseMessage submitted = await client.PostAsJsonAsync("api/operations", new
        {
            method = "plan.compute.execute",
            parameters = new
            {
                model,
                inspectionId = StringProperty(inspection, "inspectionId"),
            },
        }, JsonOptions, timeout.Token);
        string submittedBody = await submitted.Content.ReadAsStringAsync(timeout.Token);
        if (submitted.StatusCode != HttpStatusCode.Accepted)
            throw new HttpRequestException($"POST api/operations (plan.compute.execute) returned {(int)submitted.StatusCode} {submitted.ReasonPhrase}: {submittedBody}");
        using JsonDocument submittedJson = JsonDocument.Parse(submittedBody);
        string operationId = StringProperty(submittedJson.RootElement, "id");

        long cursor = 0;
        int solverStep = -1;
        var events = new List<JsonElement>();
        Stopwatch progressWait = Stopwatch.StartNew();
        bool lastHasMore = false;
        string lastState = "queued";
        JsonElement lastSnapshot = default;
        while (true)
        {
            using JsonDocument snapshot = await GetJsonAsync(client,
                $"api/operations/{Uri.EscapeDataString(operationId)}?after={cursor}", timeout.Token);
            JsonElement root = snapshot.RootElement;
            lastSnapshot = root.Clone();
            JsonElement returnedEvents = PropertyOrNull(root, "events");
            if (returnedEvents.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in returnedEvents.EnumerateArray())
                {
                    JsonElement copy = item.Clone();
                    events.Add(copy);
                    if (solverStep < 0 && TryReadSolverStep(copy, out int step)) solverStep = step;
                }
            }
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            lastHasMore = BooleanProperty(root, "hasMore");
            lastState = StringProperty(root, "state");
            // A terminal operation can still have older event pages pending. Drain
            // those pages before trusting its visible state or terminal payload.
            if (lastHasMore) continue;
            if (IsTerminal(lastState))
            {
                JsonElement earlyTerminalResult = PropertyOrNull(root, "result");
                string sourceState = OptionalString(earlyTerminalResult, "state") ?? "missing";
                string reason = solverStep > 0
                    ? $"A completed solver step ({solverStep}) was observed, but the operation was already terminal before DELETE; source result state was '{sourceState}'."
                    : $"The source Plan ended as '{lastState}' before emitting a completed-step solver telemetry event; source result state was '{sourceState}'.";
                throw new InvalidOperationException(reason + " " + OperationDiagnostic(operationId, events, lastHasMore, root));
            }
            if (solverStep >= 0) break;
            if (progressWait.Elapsed >= SolverProgressTimeout)
                throw new TimeoutException("The real CPU Plan emitted no telemetry with Plan Compute Steps > 0 within 90 seconds. " +
                    OperationDiagnostic(operationId, events, lastHasMore, lastSnapshot));
            await Task.Delay(75, timeout.Token);
        }

        using HttpResponseMessage cancellationResponse = await client.DeleteAsync(
            $"api/operations/{Uri.EscapeDataString(operationId)}", timeout.Token);
        string cancellationBody = await cancellationResponse.Content.ReadAsStringAsync(timeout.Token);
        if (cancellationResponse.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"DELETE api/operations/{operationId} returned {(int)cancellationResponse.StatusCode} {cancellationResponse.ReasonPhrase}: {cancellationBody}. " +
                OperationDiagnostic(operationId, events, lastHasMore, lastSnapshot));
        using JsonDocument cancellationJson = JsonDocument.Parse(cancellationBody);
        bool cancelRequested = BooleanProperty(cancellationJson.RootElement, "cancelRequested");

        JsonElement terminal;
        while (true)
        {
            using JsonDocument snapshot = await GetJsonAsync(client,
                $"api/operations/{Uri.EscapeDataString(operationId)}?after={cursor}", timeout.Token);
            JsonElement root = snapshot.RootElement;
            lastSnapshot = root.Clone();
            JsonElement returnedEvents = PropertyOrNull(root, "events");
            if (returnedEvents.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in returnedEvents.EnumerateArray()) events.Add(item.Clone());
            }
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            lastHasMore = BooleanProperty(root, "hasMore");
            if (lastHasMore) continue;
            string state = StringProperty(root, "state");
            lastState = state;
            if (IsTerminal(state))
            {
                terminal = root.Clone();
                break;
            }
            await Task.Delay(75, timeout.Token);
        }

        string terminalState = StringProperty(terminal, "state");
        JsonElement observedTerminalResult = PropertyOrNull(terminal, "result");
        string? observedSourceState = OptionalString(observedTerminalResult, "state");
        if (!cancelRequested ||
            (!terminalState.Equals("canceled", StringComparison.OrdinalIgnoreCase) &&
             !terminalState.Equals("cancelled", StringComparison.OrdinalIgnoreCase)))
        {
            string reason = !cancelRequested
                ? "DELETE was too late to request cancellation."
                : $"The CPU Plan reached server state '{terminalState}' after DELETE; source result state was '{observedSourceState ?? "missing"}'.";
            throw new InvalidOperationException(reason + " " + OperationDiagnostic(operationId, events, lastHasMore, terminal));
        }

        JsonElement terminalResult = observedTerminalResult;
        string? resultState = null;
        bool? resultSummarySuccess = null;
        if (terminalResult.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            resultState = OptionalString(terminalResult, "state");
            if (resultState is null || (!resultState.Equals("canceled", StringComparison.OrdinalIgnoreCase) &&
                                        !resultState.Equals("cancelled", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The canceled operation returned a result payload that was not marked canceled. " +
                    OperationDiagnostic(operationId, events, lastHasMore, terminal));
            JsonElement summary = PropertyOrNull(terminalResult, "summary");
            JsonElement success = PropertyOrNull(summary, "success");
            if (success.ValueKind == JsonValueKind.True)
                throw new InvalidDataException("The canceled CPU Plan returned a successful compute summary. " +
                    OperationDiagnostic(operationId, events, lastHasMore, terminal));
            resultSummarySuccess = success.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }

        _ = await SubmitOperationAsync(client, "backend.info", new { }, timeout.Token);
        return new CancellationAcceptanceReport("canceled", workspaceId, operationId, planPath,
            events.Count, solverStep, cancelRequested, resultState, resultSummarySuccess,
            FollowUpBackendRequestCompleted: true);
    }

    private static string OperationDiagnostic(string operationId, IReadOnlyList<JsonElement> events,
        bool hasMore, JsonElement snapshot)
    {
        object? OptionalJson(JsonElement value) => value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null : value;
        var progressSamples = events.TakeLast(5).Select(item => new
        {
            sequence = PropertyOrNull(item, "sequence"),
            progress = OptionalJson(PropertyOrNull(item, "progress")),
        }).ToArray();
        return "diagnostic=" + JsonSerializer.Serialize(new
        {
            operationId,
            eventCount = events.Count,
            hasMore,
            state = OptionalString(snapshot, "state"),
            result = OptionalJson(PropertyOrNull(snapshot, "result")),
            error = OptionalJson(PropertyOrNull(snapshot, "error")),
            progressSamples,
        }, JsonOptions);
    }

    private static bool TryReadSolverStep(JsonElement item, out int step)
    {
        step = -1;
        JsonElement progress = PropertyOrNull(item, "progress");
        if (progress.ValueKind != JsonValueKind.Object || OptionalString(progress, "taskName") != "Plan Compute")
            return false;
        JsonElement values = PropertyOrNull(progress, "values");
        JsonElement steps = PropertyOrNull(values, "Steps");
        if (steps.ValueKind == JsonValueKind.String && int.TryParse(steps.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsed))
            step = parsed;
        else if (steps.ValueKind == JsonValueKind.Number && steps.TryGetInt32(out parsed))
            step = parsed;
        return step > 0;
    }
}
