using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ras.Layers;

namespace Ras.Backend.ServerChecks;

internal sealed record PlanMatrixIsolationReport(string State, string Directory, int SourceFileCount,
    bool SourceHashesUnchanged, int Written, bool DraftAndAssociationsReadBack,
    bool PreparedPropertyTablesReadBack, bool FailedEmitCleanedUp, bool StaleDependencyRejected,
    bool ActiveCancellationCleanedUp, bool PartialCopyFailureCleanedUp);

internal static partial class ServerAcceptance
{
    // Uses only a fresh generated fixture. No plan.compute/solver or model.save RPC.
    public static async Task<PlanMatrixIsolationReport> RunPlanMatrixIsolationAsync(Uri baseAddress,
        string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Matrix isolation acceptance requires a loopback server.");
        string directory = Path.GetFullPath(requestedOutputDirectory);
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException("Matrix isolation acceptance requires a fresh output directory.");
        FixtureManifest fixture = PlanComputeFixture.CreateMap(Path.Combine(directory, "fixture"));
        Dictionary<string, string> before = Snapshot();
        string[] workspacesBefore = Workspaces();
        string projectXml = await File.ReadAllTextAsync(fixture.ProjectPath);
        string planRevision = FileSHA256(fixture.PlanPath);
        DateTime start = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Local);
        var capture = new PlanAnalysisProjectCapture(fixture.ProjectPath, FileSHA256(fixture.ProjectPath),
            projectXml, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectXml))));
        var draft = new PlanAnalysisPlanDraft(fixture.PlanPath, planRevision,
            new Dictionary<string, string>
            {
                ["PlanSettingsPage/ViewModel_SelectedPlan_ComputeTimestep"] = "0.25",
                ["PlanComputeSettingsPage/ViewModel_SelectedPlan_CoreCount"] = "4"
            }, JsonSerializer.Serialize(new { kind = "PlanTimeWindowDraft", assetPath = fixture.PlanPath,
                revision = planRevision, internalStartTime = start, internalComputeDurationSeconds = 4,
                timeWindowMode = 1 }, JsonOptions),
            [new(Guid.NewGuid().ToString(), "PlanSettingsPage/ViewModel_SelectedPlan_ComputeEndTime",
                start.AddSeconds(6).ToString("O"))]);
        var preview = new PlanMatrixPreviewRequest(capture, fixture.PlanPath, planRevision, draft,
            [new("ComputeTimestep", ["0.1", "0.2"])], [], true, Path.Combine(directory, "output"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        JsonElement inspection = ResultOf(await Send("preview", "plan.r2rMatrix.preview", preview));
        var emit = new PlanMatrixEmitRequest(preview, StringProperty(inspection, "inspectionID"), false, true, true);
        JsonElement emitted = ResultOf(await Send("emit", "plan.r2rMatrix.emit", emit));
        Require(IntProperty(emitted, "written") == 2 && BooleanProperty(emitted, "sourcesStillCurrent"), "Two outputs were not written from current sources.");
        AssertSourcesAndCleanup();
        int index = 0;
        foreach (JsonElement file in Property(emitted, "files").EnumerateArray())
        {
            using var result = new Result(StringProperty(file, "filePath"));
            Require(result.Plan.CoreCount == 4 && result.Plan.InternalComputeDurationSeconds == 6 &&
                result.Plan.InternalStartTime == start &&
                result.Plan.ComputeTimestep == TimeSpan.FromSeconds(index++ == 0 ? 0.1 : 0.2), "Export lost the Plan draft or sweep assignments.");
            Require(result.Geometry.Name == "Solver Geometry" && result.BoundaryConditions.Name == "Solver Boundary" &&
                result.Geometry.FlowAreaLayer[0].PropertyTableSignature.TerrainSignature.Name == "Solver Terrain" &&
                result.Geometry.FlowAreaLayer[0].PropertyTableSignature.NValueSignature.Name == "Solver Roughness", "Export lost the captured associations.");
            // HDF depth is float32 and property-table stage/depth conversion uses
            // the same 0.1 mm envelope as the existing real-compute acceptance.
            Require(result.Geometry.Mesh.CellCount == fixture.CellCount && result.Geometry.FlowAreaLayer[0].PropertyTables is not null &&
                result.InitialConditions.CellElevation.All(value => Math.Abs(value - fixture.InitialStage) < 0.0001),
                "Required property-table preparation or initial-condition preparation was skipped.");
        }
        JsonElement skipped = ResultOf(await Send("skip", "plan.r2rMatrix.emit", emit));
        Require(IntProperty(skipped, "skipped") == 2, "Existing output was not skipped.");
        AssertSourcesAndCleanup();

        PlanMatrixPreviewRequest failingPreview = preview with { OutputDirectory = Path.Combine(directory, "failed-output") };
        JsonElement failedInspection = ResultOf(await Send("failed-preview", "plan.r2rMatrix.preview", failingPreview));
        // This fails HDF creation after Geometry preparation has actually run.
        Directory.CreateDirectory(Path.Combine(failingPreview.OutputDirectory!,
            StringProperty(Property(failedInspection, "combinations")[0], "filename")));
        JsonElement failed = ResultOf(await Send("failed-emit", "plan.r2rMatrix.emit",
            emit with { Preview = failingPreview, InspectionID = StringProperty(failedInspection, "inspectionID") }));
        Require(IntProperty(failed, "failed") == 1 && StringProperty(failed, "state") == "failed", "The induced HDF output failure was not reported.");
        AssertSourcesAndCleanup();

        byte[] originalGeometry = await File.ReadAllBytesAsync(fixture.GeometryPath);
        try
        {
            await using (var stream = new FileStream(fixture.GeometryPath, FileMode.Append))
                await stream.WriteAsync(Encoding.UTF8.GetBytes("private regression revision change"));
            JsonElement stale = await Send("stale-emit", "plan.r2rMatrix.emit", emit);
            Require(StringProperty(stale, "state") == "failed" &&
                StringProperty(Property(stale, "error"), "code") == "sourceChanged", "A changed Geometry did not invalidate the preview.");
        }
        finally { await File.WriteAllBytesAsync(fixture.GeometryPath, originalGeometry); }
        AssertSourcesAndCleanup();

        PlanMatrixPreviewRequest cancelPreview = preview with
        {
            Axes = [new("CoreCount", Enumerable.Range(1, 128).Select(value => value.ToString()).ToArray())],
            OutputDirectory = Path.Combine(directory, "cancel-output")
        };
        JsonElement cancelInspection = ResultOf(await Send("cancel-preview", "plan.r2rMatrix.preview", cancelPreview));
        JsonElement canceled = await Send("cancel-emit", "plan.r2rMatrix.emit",
            emit with { Preview = cancelPreview, InspectionID = StringProperty(cancelInspection, "inspectionID") }, cancelWhileWriting: true);
        Require(StringProperty(canceled, "state") is "canceled" or "cancelled" &&
            BooleanProperty(Property(canceled, "result"), "wasCancelled"), "Active matrix cancellation was not observed.");
        AssertSourcesAndCleanup();

        // Exercise partial Stage cleanup deterministically: Geometry SaveAs succeeds;
        // the Boundary target is a directory, so its SaveAs fails before Prepare.
        CheckPartialCopyFailureCleanup(fixture);
        AssertSourcesAndCleanup();
        return new("passed", directory, before.Count, true, 2, true, true, true, true, true, true);

        string[] Workspaces() => System.IO.Directory.GetDirectories(Path.GetTempPath(), "hecras-plan-matrix-*").Order().ToArray();
        Dictionary<string, string> Snapshot() => System.IO.Directory.GetFiles(fixture.RootPath, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, FileSHA256, StringComparer.Ordinal);
        void AssertSourcesAndCleanup()
        {
            Dictionary<string, string> after = Snapshot();
            Require(before.Count == after.Count && before.All(item => after.TryGetValue(item.Key, out string? hash) && hash == item.Value), "A matrix operation changed an input file.");
            Require(Workspaces().SequenceEqual(workspacesBefore), "A preparation workspace leaked. Run the server and this check with the same isolated TMPDIR.");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        static JsonElement ResultOf(JsonElement terminal)
        {
            Require(StringProperty(terminal, "state") == "completed", "HTTP operation failed: " + terminal);
            return Property(terminal, "result");
        }
        async Task<JsonElement> Send(string name, string method, object parameters, bool cancelWhileWriting = false)
        {
            using HttpResponseMessage accepted = await client.PostAsJsonAsync("api/operations", new { method, parameters }, JsonOptions, deadline.Token);
            accepted.EnsureSuccessStatusCode();
            using JsonDocument submitted = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync(deadline.Token));
            string id = StringProperty(submitted.RootElement, "id");
            long cursor = 0;
            bool cancelSent = false;
            var events = new List<JsonElement>();
            while (true)
            {
                using JsonDocument poll = await GetJsonAsync(client, $"api/operations/{id}?after={cursor}", deadline.Token);
                JsonElement terminal = poll.RootElement;
                foreach (JsonElement item in Property(terminal, "events").EnumerateArray()) events.Add(item.Clone());
                cursor = LongPropertyOrDefault(terminal, "cursor", cursor);
                if (cancelWhileWriting && !cancelSent && events.Any(item => item.ToString().Contains("\"writing\"", StringComparison.Ordinal)))
                {
                    using HttpResponseMessage canceledResponse = await client.DeleteAsync($"api/operations/{id}", deadline.Token);
                    canceledResponse.EnsureSuccessStatusCode();
                    cancelSent = true;
                }
                if (IsTerminal(StringProperty(terminal, "state")) && !BooleanProperty(terminal, "hasMore"))
                {
                    await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"),
                        JsonSerializer.Serialize(new { method, parameters, terminal, events, cancelSent }, JsonOptions));
                    return terminal.Clone();
                }
                await Task.Delay(25, deadline.Token);
            }
        }
    }

    private static void CheckPartialCopyFailureCleanup(FixtureManifest fixture)
    {
        // Reflection avoids introducing a production-only failure-injection API.
        Type type = typeof(ModelEditorAdapter).GetNestedType("PlanMatrixPreparationWorkspace",
            System.Reflection.BindingFlags.NonPublic)!;
        var workspace = (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
        string path = (string)type.GetField("directory", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.GetValue(workspace)!;
        Project project = new(fixture.ProjectPath);
        try
        {
            Directory.CreateDirectory(Path.Combine(path, "Boundary Conditions", Path.GetFileName(fixture.BoundaryPath)));
            try
            {
                type.GetMethod("Stage")!.Invoke(workspace, [project, project.Plans.Single()]);
                throw new InvalidDataException("The induced dependency-copy failure did not occur.");
            }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
            {
                if (!File.Exists(Path.Combine(path, "Geometries", Path.GetFileName(fixture.GeometryPath))))
                    throw new InvalidDataException("Failure was not reached after the first dependency copy.", error);
            }
        }
        finally
        {
            foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                         .Concat(project.BoundaryConditions).Concat(project.Terrains).Concat(project.NValues)) layer.Dispose();
            workspace.Dispose();
        }
        if (Directory.Exists(path)) throw new InvalidDataException("Partial dependency-copy failure leaked a workspace.");
    }
}
