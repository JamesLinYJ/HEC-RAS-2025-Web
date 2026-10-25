using System.Text.Json;
using Ras.Hydraulics.HydraulicDesign;
using Ras.Layers;
using Ras.Runtime;
using Utility.Units;

namespace Ras.Backend.ServerChecks;

internal sealed record StableChannelFixtureManifest(string Schema, string ProjectPath, string DesignPath,
    StableChannelDebugState Inputs);

internal static partial class ServerAcceptance
{
    public static StableChannelFixtureManifest CreateStableChannelFixture(string requestedDirectory)
    {
        string root = Path.GetFullPath(requestedDirectory);
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException("Stable Channel fixture requires a new directory.");
        Directory.CreateDirectory(root);
        RasRuntimeBootstrap.Configure();
        string projectPath = Path.Combine(root, "Stable Channel Acceptance.ras");
        Project project = Project.CreateNew(projectPath, UnitSystem.USCustomary);
        StableChannelDebugState inputs = StableInputs();
        try
        {
            project.Save();
            foreach (string name in new[] { "A Stable Channel", "B Unchanged Channel" })
            {
                string path = StableChannelDesign.MakeFilename(project.ProjectDirectory, name);
                using StableChannelDesign design = StableChannelDesign.CreateNew(path);
                design.Project = project;
                design.TractiveForce = StableSourceMethod(inputs);
                design.SaveAs(path);
            }
        }
        finally { StableDisposeProject(project); }
        Project reopened = new(projectPath);
        string designPath;
        try { designPath = Path.GetFullPath(reopened.HydraulicDesign.StableChannelDesigns.First().Filename); }
        finally { StableDisposeProject(reopened); }
        File.WriteAllText(Path.Combine(root, "unrelated.txt"), "Unrelated acceptance data must stay unchanged.\n");
        var manifest = new StableChannelFixtureManifest("hecras-stable-channel-fixture/v1", projectPath, designPath, inputs);
        File.WriteAllText(Path.Combine(root, "fixture.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        return manifest;
    }

    public static async Task<object> RunStableChannelAsync(Uri baseAddress, string requestedDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Stable Channel acceptance requires a loopback HTTP(S) server.");
        string root = Path.GetFullPath(requestedDirectory);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Acceptance requires a new output directory.");
        Directory.CreateDirectory(root);
        StableChannelFixtureManifest original = CreateStableChannelFixture(Path.Combine(root, "source"));
        string sourceRoot = Path.GetDirectoryName(original.ProjectPath)!;
        string runRoot = Path.Combine(root, "run");
        CopyDirectory(sourceRoot, runRoot);
        string projectPath = RebasePath(original.ProjectPath, sourceRoot, runRoot);
        Dictionary<string, string> originalHashes = StableTreeHashes(sourceRoot);
        Dictionary<string, string> initialHashes = StableTreeHashes(runRoot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        CancellationToken ct = timeout.Token;
        async Task<T> Call<T>(string method, object request) =>
            (await SubmitOperationAsync(client, method, request, ct)).Result.Deserialize<T>(JsonOptions)
            ?? throw new InvalidDataException("Missing HTTP response for " + method);
        StableChannelDebugOpenResponse memory = await Call<StableChannelDebugOpenResponse>("stableChannel.open",
            new StableChannelDebugOpenRequest("memory-open", null));
        StableRequire(memory.Source.InMemory && Guid.TryParse(memory.Source.SourceID, out _) &&
            memory.State.Depth == 0 && memory.State.Temperature == 0 && memory.State.SolutionMethod == "Lane" &&
            memory.State.LeftBank?.Equation == "Strickler" && memory.State.Bed?.D == 0,
            "In-memory source defaults changed.");
        await StableExpectFailureAsync(() => Call<StableChannelDebugSaveResponse>("stableChannel.save",
            new StableChannelDebugSaveRequest("memory-save", memory.Source, original.Inputs)));
        StableChannelDebugOpenResponse opened = await Call<StableChannelDebugOpenResponse>("stableChannel.open",
            new StableChannelDebugOpenRequest("persisted-open", projectPath));
        string designPath = opened.Source.DesignPath ?? throw new InvalidDataException("Persisted design missing.");
        StableRequire(!opened.Source.InMemory && PathEquals(opened.Source.ProjectPath!, projectPath) &&
            opened.Source.ProjectRevision == FileSha256(projectPath) && opened.Source.DesignRevision == FileSha256(designPath),
            "Persisted source identity or revisions did not match files.");

        var comparisons = new List<object>();
        StableChannelDebugState? solved = null;
        foreach ((StableChannelDebugSource source, string[] pair) in new[]
        {
            (memory.Source, new[] { "Depth", "Width" }),
            (opened.Source, new[] { "Slope", "ParticleSize" })
        })
        {
            TractiveForceMethod retained = StableSourceMethod(original.Inputs);
            var expected = new Dictionary<string, double>();
            if (pair[0] == "Depth")
            {
                retained.SolveForDepthAndWidth(out double depth, out double width);
                expected["depth"] = depth; expected["width"] = width;
            }
            else
            {
                retained.SolveForSlopeAndD(out double slope, out double[] sizes);
                expected["slope"] = slope;
                expected["leftD"] = sizes[0]; expected["bedD"] = sizes[1]; expected["rightD"] = sizes[2];
            }
            StableChannelDebugComputeResponse result = await Call<StableChannelDebugComputeResponse>("stableChannel.compute",
                new StableChannelDebugComputeRequest("solve-" + pair[0], source, original.Inputs, pair));
            StableRequire(result.Computed && result.ErrorCode is null && result.SourceID == source.SourceID,
                "Retained solve failed: " + result.ErrorCode);
            var actual = new Dictionary<string, double?>
            {
                ["depth"] = result.State.Depth, ["width"] = result.State.Width, ["slope"] = result.State.Slope,
                ["leftD"] = result.State.LeftBank?.D, ["bedD"] = result.State.Bed?.D, ["rightD"] = result.State.RightBank?.D
            };
            foreach (var value in expected)
                StableRequire(double.IsFinite(value.Value) && actual[value.Key] is double observed &&
                    Math.Abs(observed - value.Value) <= 1e-12 * Math.Max(1, Math.Abs(value.Value)),
                    "HTTP result differs from retained TractiveForceMethod: " + value.Key);
            comparisons.Add(new { pair, source.InMemory, expected, actual });
            solved = result.State;
        }
        foreach ((StableChannelDebugState state, string[] targets, string code) in new[]
        {
            (original.Inputs, new[] { "Depth" }, "selectExactlyTwoTargets"),
            (original.Inputs with { Temperature = 39 }, new[] { "Depth", "Width" }, "temperatureOutsideSourceRange"),
            (original.Inputs with { SolutionMethod = "invalid" }, new[] { "Depth", "Width" }, "invalidSolutionMethod")
        })
        {
            var rejected = await Call<StableChannelDebugComputeResponse>("stableChannel.compute",
                new StableChannelDebugComputeRequest("invalid-" + code, opened.Source, state, targets));
            StableRequire(!rejected.Computed && rejected.ErrorCode == code, "Invalid input was not rejected: " + code);
        }
        StableRequire(initialHashes.All(item => FileSha256(item.Key) == item.Value), "Compute changed source files.");
        StableChannelDebugState draft = solved! with { Discharge = 120 };
        var receipt = await Call<StableChannelDebugSaveResponse>("stableChannel.save",
            new StableChannelDebugSaveRequest("persisted-save", opened.Source, draft));
        StableRequire(receipt.DesignRevision != opened.Source.DesignRevision && receipt.DesignRevision == FileSha256(designPath),
            "Save failed to advance the persisted HDF revision.");
        Project readback = new(projectPath);
        try
        {
            TractiveForceMethod method = readback.HydraulicDesign.StableChannelDesigns.First().TractiveForce;
            StableRequire(method.Discharge == 120 && method.Slope == draft.Slope && method.LeftBank.D == draft.LeftBank!.D &&
                method.Bed.Equation == BankEquation.Manning, "Fresh typed HDF readback differs from the saved draft.");
        }
        finally { StableDisposeProject(readback); }
        var reopened = await Call<StableChannelDebugOpenResponse>("stableChannel.open",
            new StableChannelDebugOpenRequest("reopen", projectPath));
        StableRequire(reopened.Source.DesignRevision == receipt.DesignRevision && reopened.State.Discharge == 120,
            "HTTP reopen did not read saved state.");
        var stale = await Call<StableChannelDebugComputeResponse>("stableChannel.compute",
            new StableChannelDebugComputeRequest("stale-compute", opened.Source, draft, ["Depth", "Width"]));
        StableRequire(!stale.Computed && stale.ErrorCode == "sourceChanged", "Stale compute was not rejected.");
        await StableExpectFailureAsync(() => Call<StableChannelDebugSaveResponse>("stableChannel.save",
            new StableChannelDebugSaveRequest("stale-save", opened.Source, draft with { Discharge = 999 })));
        StableRequire(FileSha256(designPath) == receipt.DesignRevision, "Stale save changed the new design.");
        StableRequire(originalHashes.All(item => FileSha256(item.Key) == item.Value), "Original fixture was changed.");
        StableRequire(initialHashes.Where(item => !PathEquals(item.Key, designPath)).All(item => FileSha256(item.Key) == item.Value),
            "Save changed the project or unrelated file.");
        string[] addedFiles = Directory.GetFiles(runRoot, "*", SearchOption.AllDirectories).Except(initialHashes.Keys).ToArray();
        StableRequire(addedFiles.Length == 0, "Save left unexpected files in the project.");
        var report = new { state = "passed", projectPath, designPath, originalSourcePath = original.ProjectPath,
            comparisons, receipt, memoryDefaultsPreserved = true, invalidInputsRejected = true,
            staleComputeAndSaveRejected = true, typedHdfReadback = true, projectAndOtherFilesUnchanged = true,
            originalFilesUnchanged = true, originalHashes, initialHashes };
        await File.WriteAllTextAsync(Path.Combine(root, "stable-channel-report.json"), JsonSerializer.Serialize(report, JsonOptions), ct);
        return report;
    }

    private static StableChannelDebugState StableInputs() => new(2, 100, .001, 2, 2, 70, 60, "Lane",
        StablePart(), StablePart(), StablePart());
    private static StableChannelDebugPartState StablePart() => new(.005, 45, .03, 2.65, .047, "Manning");
    private static TractiveForceMethod StableSourceMethod(StableChannelDebugState s) => new()
    {
        Depth = s.Depth!.Value, Discharge = s.Discharge!.Value, Slope = s.Slope!.Value,
        LeftBankSideSlope = s.LeftBankSideSlope!.Value, RightBankSideSlope = s.RightBankSideSlope!.Value,
        Temperature = s.Temperature!.Value, Width = s.Width!.Value,
        SolutionMethod = Enum.Parse<TractiveForceSolutionMethod>(s.SolutionMethod),
        LeftBank = StableSourcePart(s.LeftBank!), Bed = StableSourcePart(s.Bed!), RightBank = StableSourcePart(s.RightBank!)
    };
    private static TractiveForceMethodPart StableSourcePart(StableChannelDebugPartState p) => new()
    {
        D = p.D!.Value, AngleOfRepose = p.AngleOfRepose!.Value, Roughness = p.Roughness!.Value,
        SpecificGravity = p.SpecificGravity!.Value, ShieldsParameter = p.ShieldsParameter!.Value,
        Equation = Enum.Parse<BankEquation>(p.Equation)
    };
    private static Dictionary<string, string> StableTreeHashes(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, FileSha256);
    private static void StableRequire(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
    private static async Task StableExpectFailureAsync(Func<Task> action)
    {
        try { await action(); } catch (InvalidOperationException) { return; }
        throw new InvalidDataException("The server accepted an operation that should fail.");
    }
    private static void StableDisposeProject(Project project)
    {
        foreach (StableChannelDesign design in project.HydraulicDesign.StableChannelDesigns) design.Dispose();
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
            .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues).Concat(project.ClassificationLayers)) layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }
}
