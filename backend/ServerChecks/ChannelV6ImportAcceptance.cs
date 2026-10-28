using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ras.Layers;
using Ras.Layers.Classic;
using Ras.Layers.Terrains;
using Ras.Migration;
using Ras.Runtime;
using RasMapperLib;
using Utility.Units;

namespace Ras.Backend.ServerChecks;

internal sealed record ChannelV6ExpectedProfile(string Name, TerrainMapCoordinate[] Points,
    double[] Stations, double[] Elevations);

internal sealed record ChannelV6ImportFixture(string Schema, string RootPath, string ProjectPath,
    string TerrainPath, string SourcePath, string SourceSHA256, string TerrainRevision,
    string InputID, string InputName, string SelectedInputField, string FeatureTable,
    string ProfileTable, ChannelV6ExpectedProfile[] Profiles);

internal sealed record ChannelV6OperationEvidence(string Method, string ID, string State,
    JsonElement? Result, JsonElement? Error, JsonElement[] Events,
    string? TopologyOperation = null, Dictionary<string, string[]>? TableSelections = null);

internal sealed record ChannelV6ImportReport(string State, string ReportPath,
    ChannelV6ImportFixture SourceFixture, ChannelV6ImportFixture WorkingFixture,
    string SavedRevision, string SavedInputID, ChannelV6ExpectedProfile[] SavedProfiles,
    TerrainTopologyFeature[] SavedTopology, string ImportedTopologySHA256,
    string ReferenceTopologySHA256, ChannelV6GeometryImportCleanup AdoptedCleanup,
    ChannelV6GeometryImportCleanup UnadoptedCleanup,
    bool IndependentRetainedConverterReadback, bool CompleteProfileTablesReadback,
    bool CrossRequestUndoRedoReadback, bool UndoWithSelectedProfileClearedSelection,
    bool DraftPayloadReferenceCleanupRejected, bool RedoPayloadReferenceCleanupRejected,
    bool WrongSourceSHARejected,
    bool NonemptyInputRejected, bool StaleDraftRejected, bool StaleSavedRevisionRejected,
    bool EmptyDraftDuplexCancellationReadback, bool SharedTokenCancellationPreservedHistory,
    bool WrongCleanupSHARejected, bool UnadoptedPayloadOnlyRemoved,
    bool RepeatedCleanupIdempotent, bool ParentSaveReleasedOwnedToken,
    bool HdfFilesUnchangedBeforeParentSave, bool IndependentSavedHdfReadback,
    bool OriginalFixtureUnchanged, bool UnrelatedWorkingFilesUnchanged,
    ChannelV6OperationEvidence[] Operations);

/// <summary>
/// Builds real legacy HDF cross sections through RASGeometry, then exercises the
/// production C# worker over HTTP. A separately converted private source copy
/// supplies the complete topology reference; persisted results are reopened
/// through Terrain after the ordinary parent Save.
/// </summary>
internal static partial class ServerAcceptance
{
    private const string ChannelV6SelectedInputField = "TerrainLayersEditor/SelectedInputWithMap";
    private const string ChannelV6FeatureTable = "ChannelModEditor/Features";
    private const string ChannelV6ProfileTable = "ChannelModEditor/Profile";
    private const string ChannelV6ImportMethod = "model.channelV6GeometryImport";
    private const string ChannelV6DiscardMethod = "model.channelV6GeometryImportDiscard";
    private const string ChannelV6WrongSHA = "0000000000000000000000000000000000000000000000000000000000000000";

    public static ChannelV6ImportFixture CreateChannelV6ImportFixture(string newOutputDirectory)
    {
        string root = ChannelV6CreateNewDirectory(newOutputDirectory);
        RasRuntimeBootstrap.Configure();
        string projectPath = Path.Combine(root, "Channel V6 Import Acceptance.ras");
        Project project = Project.CreateNew(projectPath, UnitSystem.SI);
        string terrainPath;
        const string inputName = "Imported Channel";
        try
        {
            Terrain terrain = project.AddNewTerrain("Channel import draft");
            terrain.Add(new ChannelModTerrainInput(terrain, inputName));
            project.Save();
            terrain.Save();
            terrainPath = Path.GetFullPath(terrain.Filename);
        }
        finally { ChannelV6DisposeProject(project); }

        string sourcePath = Path.Combine(root, "version6-geometry.hdf");
        RASGeometry source = RASGeometry.CreateEmpty(sourcePath, "Version 6 migration source",
            SharedData.EnumUnits.SIUnits);
        source.River.AddFeature(new Polyline(new PointMs([new PointM(50, 0), new PointM(50, 100)])));
        source.River.SetRiverName(0, "Imported River");
        source.River.SetReachName(0, "Imported Reach");
        var expected = new List<ChannelV6ExpectedProfile>();
        for (int index = 0; index < 2; index++)
        {
            double y = 10 + index * 10;
            string station = (1000 - index * 100).ToString(CultureInfo.InvariantCulture);
            source.XS.AddFeature(new Polyline(new PointMs([new PointM(0, y), new PointM(100, y)])));
            IHasRRRSLayerExtensions.SetRiverName(source.XS, index, "Imported River");
            IHasRRRSLayerExtensions.SetReachName(source.XS, index, "Imported Reach");
            IHasRRRSLayerExtensions.SetRiverStationName(source.XS, index, station);
            source.XS.SetNodeName(index, "Imported XS " + index.ToString(CultureInfo.InvariantCulture));
            Profile profile = new();
            profile.Add(0, 30 + index);
            profile.Add(50, 20 + index);
            profile.Add(100, 10 + index);
            source.XS.SetProfile(index, profile);
            expected.Add(new("Imported River - Imported Reach - " + station,
                [new(0, y), new(100, y)], [0, 50, 100], [30 + index, 20 + index, 10 + index]));
        }
        source.Save();

        string terrainRevision = ChannelV6FileSHA(terrainPath);
        var fixture = new ChannelV6ImportFixture("hecras-channel-v6-import-fixture/v1", root,
            projectPath, terrainPath, sourcePath, ChannelV6FileSHA(sourcePath), terrainRevision,
            ChannelV6InputID(terrainPath, terrainRevision), inputName, ChannelV6SelectedInputField,
            ChannelV6FeatureTable, ChannelV6ProfileTable, expected.ToArray());
        File.WriteAllText(Path.Combine(root, "unrelated.txt"), "Channel acceptance preserves unrelated files.\n");
        File.WriteAllText(Path.Combine(root, "fixture.json"), JsonSerializer.Serialize(fixture, JsonOptions));
        return fixture;
    }

    public static Task<ChannelV6ImportReport> RunChannelV6ImportAsync(string baseUrl, string newOutputDirectory) =>
        RunChannelV6ImportAsync(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), newOutputDirectory);

    public static async Task<ChannelV6ImportReport> RunChannelV6ImportAsync(Uri baseAddress, string newOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Channel V6 acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));
        string root = ChannelV6CreateNewDirectory(newOutputDirectory);
        ChannelV6ImportFixture original = CreateChannelV6ImportFixture(Path.Combine(root, "source"));
        Dictionary<string, string> sourceHashes = ChannelV6TreeHashes(original.RootPath);
        string runRoot = ChannelV6CreateNewDirectory(Path.Combine(root, "run"));
        foreach (string file in Directory.EnumerateFiles(original.RootPath, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(runRoot, Path.GetRelativePath(original.RootPath, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
        string Rebase(string path) => Path.Combine(runRoot, Path.GetRelativePath(original.RootPath, path));
        ChannelV6ImportFixture fixture = original with
        {
            RootPath = runRoot, ProjectPath = Rebase(original.ProjectPath), TerrainPath = Rebase(original.TerrainPath),
            SourcePath = Rebase(original.SourcePath),
            InputID = ChannelV6InputID(Rebase(original.TerrainPath), original.TerrainRevision)
        };
        File.WriteAllText(Path.Combine(root, "working-fixture.json"), JsonSerializer.Serialize(fixture, JsonOptions));
        Dictionary<string, string> workingHashes = ChannelV6TreeHashes(runRoot);
        ChannelV6Require(workingHashes[fixture.TerrainPath] == original.TerrainRevision &&
            workingHashes[fixture.SourcePath] == original.SourceSHA256, "The private working HDF copy changed source bytes.");

        TerrainTopologyFeature[] referenceTopology = ChannelV6ReferenceTopology(fixture,
            ChannelV6CreateNewDirectory(Path.Combine(root, "independent-reference")));
        string referenceSHA = ChannelV6TopologySHA(referenceTopology);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        ChannelV6Require(StringProperty(health.RootElement, "status") == "ready", "The C# server is not ready.");
        var evidence = new List<ChannelV6OperationEvidence>();
        async Task<ChannelV6OperationEvidence> Invoke(string method, object request, bool cancelImmediately = false)
        {
            ChannelV6OperationEvidence result = await ChannelV6InvokeAsync(client, method, request, cancellation, cancelImmediately);
            evidence.Add(result);
            await File.WriteAllTextAsync(Path.Combine(root, "operations.json"), JsonSerializer.Serialize(evidence, JsonOptions), cancellation);
            return result;
        }
        async Task<T> Call<T>(string method, object request) => ChannelV6Completed<T>(await Invoke(method, request));
        async Task Reject(string method, object request, string code)
        {
            ChannelV6OperationEvidence result = await Invoke(method, request);
            ChannelV6Require(result.State == "failed" && result.Error is { } error &&
                OptionalString(error, "code") == code, "Expected " + code + " rejection from " + method + ": " +
                JsonSerializer.Serialize(result, JsonOptions));
        }
        void AssertNoSourceWrites()
        {
            ChannelV6Require(workingHashes.All(pair => File.Exists(pair.Key) && ChannelV6FileSHA(pair.Key) == pair.Value),
                "A Channel draft operation changed a working project/source file before parent Save.");
            ChannelV6Require(sourceHashes.All(pair => File.Exists(pair.Key) && ChannelV6FileSHA(pair.Key) == pair.Value),
                "The generated original fixture changed.");
        }

        _ = await Call<JsonElement>("project.open", new { projectPath = fixture.ProjectPath });
        ModelSnapshot loaded = await Call<ModelSnapshot>("model.load", new ModelRequest(fixture.TerrainPath, "Terrain", fixture.ProjectPath));
        ChannelV6Require(loaded.Revision == fixture.TerrainRevision && loaded.Terrain?.Inputs.Single().Id == fixture.InputID,
            "The HTTP terrain source identity differs from the copied fixture.");
        ModelSnapshot selected = await Call<ModelSnapshot>("model.terrainTransform",
            new TerrainTransformRequest(ChannelV6Model(loaded, fixture), "selectInput", ItemId: fixture.InputID));
        TerrainTopologyResponse initial = await Call<TerrainTopologyResponse>("model.terrainTopology",
            new TerrainTopologyRequest(ChannelV6Model(selected, fixture), fixture.InputID, "load"));
        selected = initial.Snapshot;
        ChannelV6Require(ChannelV6Rows(selected, ChannelV6FeatureTable).Length == 0 && initial.Topology.Features.Length == 0,
            "The selected fixture Channel is not empty.");
        AssertNoSourceWrites();

        ChannelV6GeometryImportRequest initialRequest = ChannelV6ImportRequest(selected, fixture);
        await Reject(ChannelV6ImportMethod, initialRequest with { SourceSHA256 = ChannelV6WrongSHA }, "sourceChanged");
        ChannelV6OperationEvidence earlyCancellation = await Invoke(ChannelV6ImportMethod, initialRequest, cancelImmediately: true);
        ChannelV6CheckCancelled(earlyCancellation);
        ChannelV6Require(ChannelV6NoDraftFiles(fixture.TerrainPath), "Cancellation left an unadopted draft payload.");
        AssertNoSourceWrites();

        ChannelV6GeometryImportResponse imported = await Call<ChannelV6GeometryImportResponse>(ChannelV6ImportMethod, initialRequest);
        ModelSnapshot importedSnapshot = imported.Snapshot ?? throw new InvalidDataException("Channel import omitted its parent snapshot.");
        TerrainTopologyData importedTopology = imported.Topology ?? throw new InvalidDataException("Channel import omitted topology.");
        ChannelV6GeometryImportCleanup adoptedCleanup = imported.Cleanup ?? throw new InvalidDataException("Channel import omitted its exact cleanup receipt.");
        ChannelV6Require(imported.State == "completed" && imported.SourceRevision == fixture.SourceSHA256 &&
            importedTopology.InputId == fixture.InputID && importedTopology.SourceRevision == fixture.TerrainRevision &&
            importedSnapshot.Terrain?.ImportDraftToken == adoptedCleanup.DraftToken && importedTopology.CanUndo && !importedTopology.CanRedo,
            "The imported response lost source, parent draft or undo ownership.");
        string importedSHA = ChannelV6TopologySHA(importedTopology.Features);
        ChannelV6Require(importedSHA == referenceSHA && importedTopology.Features.Any(feature => feature.Kind == "arc"),
            "HTTP topology differs from the complete independent retained-converter topology.");
        ChannelV6VerifyDraftProfiles(importedSnapshot, fixture);
        ChannelV6Require(!importedSnapshot.SourceState!.Contains("hecras-channel-v6-", StringComparison.Ordinal),
            "The parent draft persisted a private converter path.");
        string adoptedPath = ChannelV6PayloadPath(fixture.TerrainPath, adoptedCleanup);
        ChannelV6Require(ChannelV6FileSHA(adoptedPath) == adoptedCleanup.MigratedSHA256,
            "The imported cleanup receipt does not identify its staged HDF bytes.");
        importedSnapshot = await ChannelV6ReadProfileTablesAsync(importedSnapshot, fixture, Call<ModelSnapshot>);
        await Reject(ChannelV6DiscardMethod,
            new ChannelV6GeometryImportDiscardRequest(ChannelV6Model(importedSnapshot, fixture), adoptedCleanup), "invalidInput");
        AssertNoSourceWrites();

        await Reject(ChannelV6ImportMethod, ChannelV6ImportRequest(importedSnapshot, fixture) with
            { ExpectedDraftIdentity = initialRequest.ExpectedDraftIdentity }, "sourceChanged");
        await Reject(ChannelV6ImportMethod, ChannelV6ImportRequest(importedSnapshot, fixture), "invalidInput");
        AssertNoSourceWrites();
        ChannelV6Require(ChannelV6FileSHA(adoptedPath) == adoptedCleanup.MigratedSHA256,
            "A rejected import changed the accepted payload.");

        TerrainTopologyResponse beforeUndo = await Call<TerrainTopologyResponse>("model.terrainTopology",
            new TerrainTopologyRequest(ChannelV6Model(importedSnapshot, fixture), fixture.InputID, "load"));
        string[] selectedBeforeUndo = beforeUndo.Snapshot.Terrain?.Tables?[ChannelV6FeatureTable].SelectedRowIDs ?? [];
        ChannelV6Require(selectedBeforeUndo.Length == 1 &&
            ChannelV6Rows(beforeUndo.Snapshot, ChannelV6ProfileTable).Length == fixture.Profiles[^1].Stations.Length,
            "The Undo regression must retain the selected imported feature and its visible profile rows.");
        TerrainTopologyResponse undone = await Call<TerrainTopologyResponse>("model.terrainTopology",
            new TerrainTopologyRequest(ChannelV6Model(beforeUndo.Snapshot, fixture), fixture.InputID, "undo",
                ExpectedDraftIdentity: beforeUndo.Topology.DraftIdentity));
        ChannelV6Require(undone.Snapshot.Terrain?.Tables?.Values.All(table => table.SelectedRowIDs is not { Length: > 0 }) == true &&
            ChannelV6Rows(undone.Snapshot, ChannelV6FeatureTable).Length == 0 &&
            ChannelV6Rows(undone.Snapshot, ChannelV6ProfileTable).Length == 0 &&
            ChannelV6Rows(undone.Snapshot, "ChannelModEditor/TerrainProfile").Length == 0 &&
            !undone.Snapshot.Fields.Keys.Any(field => field.StartsWith("TerrainSelection/", StringComparison.Ordinal)),
            "Undo left selected row identities or details belonging to the removed imported profiles.");
        TerrainTopologyResponse undoReload = await Call<TerrainTopologyResponse>("model.terrainTopology",
            new TerrainTopologyRequest(ChannelV6Model(undone.Snapshot, fixture), fixture.InputID, "load"));
        ChannelV6Require(undoReload.Topology.CanRedo && !undoReload.Topology.CanUndo &&
            undoReload.Topology.Features.Length == 0 && ChannelV6Rows(undoReload.Snapshot, ChannelV6FeatureTable).Length == 0 &&
            undoReload.Snapshot.Terrain?.ImportDraftToken == adoptedCleanup.DraftToken,
            "Cross-request Undo did not preserve an empty Channel plus the accepted redo payload.");
        await Reject(ChannelV6DiscardMethod,
            new ChannelV6GeometryImportDiscardRequest(ChannelV6Model(undoReload.Snapshot, fixture), adoptedCleanup), "invalidInput");
        ChannelV6Require(ChannelV6FileSHA(adoptedPath) == adoptedCleanup.MigratedSHA256,
            "Cleanup removed a payload referenced by active or redo draft history.");

        ChannelV6GeometryImportRequest undoneRequest = ChannelV6ImportRequest(undoReload.Snapshot, fixture);
        ChannelV6OperationEvidence sharedCancellation = await Invoke(ChannelV6ImportMethod, undoneRequest, cancelImmediately: true);
        ChannelV6CheckCancelled(sharedCancellation);
        ChannelV6Require(ChannelV6FileSHA(adoptedPath) == adoptedCleanup.MigratedSHA256,
            "Cancellation deleted the accepted shared-token history.");

        ChannelV6GeometryImportResponse unadopted = await Call<ChannelV6GeometryImportResponse>(ChannelV6ImportMethod, undoneRequest);
        ChannelV6GeometryImportCleanup unadoptedCleanup = unadopted.Cleanup
            ?? throw new InvalidDataException("The unadopted result omitted its exact cleanup receipt.");
        string unadoptedPath = ChannelV6PayloadPath(fixture.TerrainPath, unadoptedCleanup);
        ChannelV6Require(unadopted.State == "completed" && unadoptedCleanup.DraftToken == adoptedCleanup.DraftToken &&
            unadoptedCleanup.PayloadId != adoptedCleanup.PayloadId && File.Exists(adoptedPath) && File.Exists(unadoptedPath),
            "The shared-token fixture did not create two distinct real migrated payloads.");
        await Reject(ChannelV6DiscardMethod, new ChannelV6GeometryImportDiscardRequest(ChannelV6Model(undoReload.Snapshot, fixture),
            unadoptedCleanup with { MigratedSHA256 = ChannelV6WrongSHA }), "sourceChanged");
        ChannelV6Require(ChannelV6FileSHA(adoptedPath) == adoptedCleanup.MigratedSHA256 &&
            ChannelV6FileSHA(unadoptedPath) == unadoptedCleanup.MigratedSHA256,
            "A mismatched cleanup receipt deleted or changed a shared-token payload.");
        var discardRequest = new ChannelV6GeometryImportDiscardRequest(ChannelV6Model(undoReload.Snapshot, fixture), unadoptedCleanup);
        ChannelV6GeometryImportDiscardResponse discarded = await Call<ChannelV6GeometryImportDiscardResponse>(ChannelV6DiscardMethod, discardRequest);
        ChannelV6GeometryImportDiscardResponse discardedAgain = await Call<ChannelV6GeometryImportDiscardResponse>(ChannelV6DiscardMethod, discardRequest);
        ChannelV6Require(discarded.Discarded && discardedAgain.Discarded && !discarded.DraftDirectoryRemoved &&
            !File.Exists(unadoptedPath) && ChannelV6FileSHA(adoptedPath) == adoptedCleanup.MigratedSHA256,
            "Discarding an unadopted result failed to preserve accepted payload ownership or idempotency.");
        AssertNoSourceWrites();

        TerrainTopologyResponse redone = await Call<TerrainTopologyResponse>("model.terrainTopology",
            new TerrainTopologyRequest(ChannelV6Model(undoReload.Snapshot, fixture), fixture.InputID, "redo",
                ExpectedDraftIdentity: undoReload.Topology.DraftIdentity));
        ChannelV6Require(redone.Topology.CanUndo && !redone.Topology.CanRedo &&
            ChannelV6TopologySHA(redone.Topology.Features) == referenceSHA, "Redo did not replay the accepted topology after sibling cleanup.");
        ChannelV6VerifyDraftProfiles(redone.Snapshot, fixture);
        ModelSnapshot readyToSave = await ChannelV6ReadProfileTablesAsync(redone.Snapshot, fixture, Call<ModelSnapshot>);
        AssertNoSourceWrites();
        ModelSnapshot saved = await Call<ModelSnapshot>("model.save", ChannelV6Model(readyToSave, fixture));
        string savedRevision = ChannelV6FileSHA(fixture.TerrainPath);
        ChannelV6Require(saved.Revision == savedRevision && savedRevision != fixture.TerrainRevision,
            "Parent Save did not acknowledge the changed terrain HDF revision.");
        ChannelV6Require(!Directory.Exists(ChannelV6DraftDirectory(fixture.TerrainPath, adoptedCleanup.DraftToken)),
            "Parent Save did not release the owned import-token directory.");

        ChannelV6ExpectedProfile[] savedProfiles;
        TerrainTopologyFeature[] savedTopology;
        using (Terrain reopenedHdf = new(fixture.TerrainPath))
        {
            ChannelModTerrainInput persisted = reopenedHdf.Layers.OfType<ChannelModTerrainInput>().Single();
            ChannelV6Require(persisted.Name == fixture.InputName, "Parent Save changed the selected input name.");
            savedProfiles = ChannelV6Profiles(persisted);
            ChannelV6ProfilesEqual(savedProfiles, fixture.Profiles, "Independent saved-HDF readback");
            var session = new TerrainTopologyEditSession(fixture.InputID, persisted);
            session.Initialize();
            savedTopology = session.Features();
            ChannelV6Require(ChannelV6TopologySHA(savedTopology) == referenceSHA,
                "Independent saved-HDF topology differs from the retained source conversion.");
        }

        ModelSnapshot reopened = await Call<ModelSnapshot>("model.load", new ModelRequest(fixture.TerrainPath, "Terrain", fixture.ProjectPath));
        string savedInputID = reopened.Terrain?.Inputs.Single().Id ?? throw new InvalidDataException("Saved Channel input is missing.");
        ChannelV6ImportFixture savedFixture = fixture with { InputID = savedInputID, TerrainRevision = savedRevision };
        ModelSnapshot reselected = await Call<ModelSnapshot>("model.terrainTransform",
            new TerrainTransformRequest(ChannelV6Model(reopened, savedFixture), "selectInput", ItemId: savedInputID));
        TerrainTopologyResponse savedReload = await Call<TerrainTopologyResponse>("model.terrainTopology",
            new TerrainTopologyRequest(ChannelV6Model(reselected, savedFixture), savedInputID, "load"));
        ChannelV6Require(!savedReload.Topology.CanUndo && !savedReload.Topology.CanRedo &&
            ChannelV6TopologySHA(savedReload.Topology.Features) == referenceSHA, "Saved HTTP reopen did not load committed topology as its baseline.");
        _ = await ChannelV6ReadProfileTablesAsync(savedReload.Snapshot, savedFixture, Call<ModelSnapshot>);
        await Reject(ChannelV6ImportMethod, initialRequest, "sourceChanged");
        ChannelV6GeometryImportDiscardResponse afterSaveCleanup = await Call<ChannelV6GeometryImportDiscardResponse>(ChannelV6DiscardMethod,
            new ChannelV6GeometryImportDiscardRequest(ChannelV6Model(readyToSave, fixture), adoptedCleanup));
        ChannelV6Require(afterSaveCleanup.Discarded && afterSaveCleanup.DraftDirectoryRemoved,
            "Exact cleanup is not idempotent after the parent already released its token.");

        ChannelV6Require(sourceHashes.All(pair => File.Exists(pair.Key) && ChannelV6FileSHA(pair.Key) == pair.Value),
            "The original fixture changed during acceptance.");
        ChannelV6Require(workingHashes.Where(pair => pair.Key != fixture.TerrainPath)
            .All(pair => File.Exists(pair.Key) && ChannelV6FileSHA(pair.Key) == pair.Value) &&
            ChannelV6FileSHA(fixture.TerrainPath) == savedRevision,
            "Parent Save or a stale operation changed an unrelated working file or source HDF.");
        ChannelV6Require(Directory.EnumerateFiles(runRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .SequenceEqual(workingHashes.Keys.Order(StringComparer.Ordinal)), "Acceptance left unexpected files in the working project.");
        string reportPath = Path.Combine(root, "channel-v6-import-report.json");
        var report = new ChannelV6ImportReport("passed", reportPath, original, fixture, savedRevision, savedInputID,
            savedProfiles, savedTopology, importedSHA, referenceSHA, adoptedCleanup, unadoptedCleanup,
            IndependentRetainedConverterReadback: true, CompleteProfileTablesReadback: true,
            CrossRequestUndoRedoReadback: true, UndoWithSelectedProfileClearedSelection: true,
            DraftPayloadReferenceCleanupRejected: true, RedoPayloadReferenceCleanupRejected: true,
            WrongSourceSHARejected: true, NonemptyInputRejected: true, StaleDraftRejected: true,
            StaleSavedRevisionRejected: true, EmptyDraftDuplexCancellationReadback: true,
            SharedTokenCancellationPreservedHistory: true, WrongCleanupSHARejected: true,
            UnadoptedPayloadOnlyRemoved: true, RepeatedCleanupIdempotent: true,
            ParentSaveReleasedOwnedToken: true, HdfFilesUnchangedBeforeParentSave: true,
            IndependentSavedHdfReadback: true, OriginalFixtureUnchanged: true,
            UnrelatedWorkingFilesUnchanged: true, Operations: evidence.ToArray());
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellation);
        return report;
    }

    private static TerrainTopologyFeature[] ChannelV6ReferenceTopology(ChannelV6ImportFixture fixture, string directory)
    {
        string input = Path.Combine(directory, "source-copy.hdf");
        string output = Path.Combine(directory, "migrated-classic.hdf");
        File.Copy(fixture.SourcePath, input, overwrite: false);
        ChannelV6Require(ModelConverter.MigrateGeometry(input, output,
            new MigrationParams { AsClassic = true, Generate1DLines = Generate1DLinesMode.IfNeeded }),
            "The independent retained C# V6 converter failed.");
        using ClassicGeometry geometry = new(output);
        using Terrain terrain = Terrain.CreateInMemory("Independent conversion reference");
        var channel = new ChannelModTerrainInput(terrain, fixture.InputName);
        terrain.Add(channel);
        channel.Import1DGeometry(geometry);
        ChannelV6ProfilesEqual(ChannelV6Profiles(channel), fixture.Profiles, "Independent retained conversion");
        var session = new TerrainTopologyEditSession(fixture.InputID, channel);
        session.Initialize();
        return session.Features();
    }

    private static async Task<ModelSnapshot> ChannelV6ReadProfileTablesAsync(ModelSnapshot snapshot,
        ChannelV6ImportFixture fixture, Func<string, object, Task<ModelSnapshot>> call)
    {
        ModelRow[] features = ChannelV6Rows(snapshot, ChannelV6FeatureTable);
        ChannelV6Require(features.Length == fixture.Profiles.Length, "The Channel feature table count changed.");
        foreach (ChannelV6ExpectedProfile profile in fixture.Profiles)
        {
            ModelRow feature = features.Single(row => row.Values["Name"] == profile.Name);
            ChannelV6Require(ChannelV6Number(feature.Values["LineLength"]) == 100,
                "A complete imported cross-section line length changed.");
            snapshot = await call("model.terrainTransform", new TerrainTransformRequest(ChannelV6Model(snapshot, fixture),
                "selectRows", TableId: ChannelV6FeatureTable, RowIDs: [feature.Id]));
            ModelRow[] rows = ChannelV6Rows(snapshot, ChannelV6ProfileTable);
            ChannelV6Require(rows.Length == profile.Stations.Length &&
                rows.Select(row => ChannelV6Number(row.Values["Station"])).SequenceEqual(profile.Stations) &&
                rows.Select(row => ChannelV6Number(row.Values["Elevation"])).SequenceEqual(profile.Elevations),
                "The HTTP profile table differs from all source station/elevation pairs for " + profile.Name);
        }
        return snapshot;
    }

    private static void ChannelV6VerifyDraftProfiles(ModelSnapshot snapshot, ChannelV6ImportFixture fixture)
    {
        using JsonDocument state = JsonDocument.Parse(snapshot.SourceState ?? throw new InvalidDataException("The imported draft is missing."));
        JsonElement mutations = state.RootElement.GetProperty("topologyDrafts").GetProperty(fixture.InputID).GetProperty("mutations");
        ChannelV6Require(mutations.GetArrayLength() == 1 && mutations[0].GetProperty("operation").GetString() == "importGeometry",
            "The import did not create one parent topology mutation.");
        TerrainImportedChannelProfileSnapshot[] profiles = mutations[0].GetProperty("importedChannelProfiles")
            .Deserialize<TerrainImportedChannelProfileSnapshot[]>(JsonOptions) ?? [];
        ChannelV6ProfilesEqual(profiles.Select(profile => new ChannelV6ExpectedProfile(profile.Name ?? string.Empty,
            profile.Points, profile.Stations.Select(ChannelV6Number).ToArray(), profile.Elevations.Select(ChannelV6Number).ToArray())).ToArray(),
            fixture.Profiles, "Parent import replay journal");
    }

    private static ModelSaveRequest ChannelV6Model(ModelSnapshot snapshot, ChannelV6ImportFixture fixture) =>
        new(fixture.TerrainPath, "Terrain", fixture.ProjectPath, snapshot.Revision,
            new(StringComparer.Ordinal) { [ChannelV6SelectedInputField] = fixture.InputID }, new(StringComparer.Ordinal),
            SourceState: snapshot.SourceState, Rows: snapshot.Terrain?.Rows,
            TableSelections: snapshot.Terrain?.Tables?.ToDictionary(pair => pair.Key, pair => pair.Value.SelectedRowIDs ?? [], StringComparer.Ordinal));

    private static ChannelV6GeometryImportRequest ChannelV6ImportRequest(ModelSnapshot snapshot,
        ChannelV6ImportFixture fixture, string? expectedDraftIdentity = null) =>
        new(ChannelV6Model(snapshot, fixture), fixture.InputID, fixture.SourcePath, fixture.SourceSHA256,
            expectedDraftIdentity ?? ModelEditorAdapter.DraftIdentity(fixture.InputID, snapshot.Revision, snapshot.SourceState));

    private static ModelRow[] ChannelV6Rows(ModelSnapshot snapshot, string table) =>
        snapshot.Terrain?.Rows?.GetValueOrDefault(table) ?? throw new InvalidDataException("Missing Channel table " + table);

    private static ChannelV6ExpectedProfile[] ChannelV6Profiles(ChannelModTerrainInput channel) => channel.Features
        .Select(profile => new ChannelV6ExpectedProfile(profile.Name,
            Enumerable.Range(0, profile.Line.Count).Select(index => new TerrainMapCoordinate(profile.Line[index].X, profile.Line[index].Y)).ToArray(),
            profile.Profile.Stations.ToArray(), profile.Profile.Elevations.Select(value => (double)value).ToArray())).ToArray();

    private static void ChannelV6ProfilesEqual(ChannelV6ExpectedProfile[] actual, ChannelV6ExpectedProfile[] expected, string context)
    {
        string actualJson = JsonSerializer.Serialize(actual.OrderBy(profile => profile.Name, StringComparer.Ordinal), JsonOptions);
        string expectedJson = JsonSerializer.Serialize(expected.OrderBy(profile => profile.Name, StringComparer.Ordinal), JsonOptions);
        ChannelV6Require(actualJson == expectedJson, context + " changed names, line coordinates, stations or elevations: " + actualJson);
    }

    private static string ChannelV6TopologySHA(TerrainTopologyFeature[] features)
    {
        static string? Normalize(string? id) => id is null ? null : string.Join(':', id.Split(':').Skip(2));
        TerrainTopologyFeature[] canonical = features.Select(feature => feature with
            { Id = Normalize(feature.Id)!, ParentId = Normalize(feature.ParentId) }).OrderBy(feature => feature.Id, StringComparer.Ordinal).ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, JsonOptions))));
    }

    private static async Task<ChannelV6OperationEvidence> ChannelV6InvokeAsync(HttpClient client, string method,
        object parameters, CancellationToken cancellation, bool cancelImmediately)
    {
        using HttpResponseMessage submitted = await client.PostAsJsonAsync("api/operations", new { method, parameters }, JsonOptions, cancellation);
        string body = await submitted.Content.ReadAsStringAsync(cancellation);
        if (submitted.StatusCode != HttpStatusCode.Accepted)
            throw new HttpRequestException($"Channel HTTP {method} submission returned {(int)submitted.StatusCode}: {body}");
        using JsonDocument submission = JsonDocument.Parse(body);
        string id = StringProperty(submission.RootElement, "id");
        if (cancelImmediately)
        {
            using HttpResponseMessage control = await client.PostAsJsonAsync($"api/operations/{Uri.EscapeDataString(id)}/controls",
                new { control = new { type = "cancel" } }, JsonOptions, cancellation);
            string controlBody = await control.Content.ReadAsStringAsync(cancellation);
            ChannelV6Require(control.StatusCode == HttpStatusCode.Accepted,
                "The real duplex cancellation control was not accepted: " + controlBody);
        }
        long cursor = 0;
        var events = new List<JsonElement>();
        while (true)
        {
            using JsonDocument poll = await GetJsonAsync(client, $"api/operations/{Uri.EscapeDataString(id)}?after={cursor}", cancellation);
            JsonElement value = poll.RootElement;
            if (PropertyOrNull(value, "events") is { ValueKind: JsonValueKind.Array } updates)
                events.AddRange(updates.EnumerateArray().Select(update => update.Clone()));
            cursor = LongPropertyOrDefault(value, "cursor", cursor);
            if (BooleanProperty(value, "hasMore")) continue;
            string state = StringProperty(value, "state");
            if (state is "completed" or "failed" or "canceled")
            {
                JsonElement result = PropertyOrNull(value, "result"), error = PropertyOrNull(value, "error");
                return new(method, id, state,
                    result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : result.Clone(),
                    error.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : error.Clone(), events.ToArray(),
                    (parameters as TerrainTopologyRequest)?.Operation,
                    (parameters as TerrainTopologyRequest)?.Model.TableSelections);
            }
            await Task.Delay(25, cancellation);
        }
    }

    private static T ChannelV6Completed<T>(ChannelV6OperationEvidence operation)
    {
        ChannelV6Require(operation.State == "completed" && operation.Error is null && operation.Result is not null,
            "Channel operation failed: " + JsonSerializer.Serialize(operation, JsonOptions));
        return operation.Result!.Value.Deserialize<T>(JsonOptions) ?? throw new InvalidDataException("Channel operation returned no typed result.");
    }

    private static void ChannelV6CheckCancelled(ChannelV6OperationEvidence operation)
    {
        ChannelV6GeometryImportResponse? response = operation.Result?.Deserialize<ChannelV6GeometryImportResponse>(JsonOptions);
        ChannelV6Require(operation.State == "canceled" && response?.State == "cancelled" &&
            response.Snapshot is null && response.Topology is null && response.Cleanup is null,
            "The service did not return an unpublished cancelled import after its real HTTP duplex control.");
    }

    private static string ChannelV6CreateNewDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        if (Directory.Exists(full) || File.Exists(full)) throw new IOException("Channel V6 acceptance requires a new output directory.");
        Directory.CreateDirectory(full);
        return full;
    }
    private static string ChannelV6FileSHA(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static Dictionary<string, string> ChannelV6TreeHashes(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, ChannelV6FileSHA, StringComparer.Ordinal);
    private static string ChannelV6InputID(string terrainPath, string revision) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.GetFullPath(terrainPath) + "\n" + revision + "\nterrain:0")));
    private static string ChannelV6DraftAssetDirectory(string assetPath) => Path.Combine(Path.GetTempPath(), "HECRAS-TerrainInputDrafts",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(assetPath)))).ToLowerInvariant());
    private static string ChannelV6DraftDirectory(string assetPath, string token) => Path.Combine(ChannelV6DraftAssetDirectory(assetPath), token);
    private static string ChannelV6PayloadPath(string assetPath, ChannelV6GeometryImportCleanup cleanup) =>
        Path.Combine(ChannelV6DraftDirectory(assetPath, cleanup.DraftToken), "channel-geometry", cleanup.PayloadId + ".hdf");
    private static bool ChannelV6NoDraftFiles(string assetPath) => !Directory.Exists(ChannelV6DraftAssetDirectory(assetPath)) ||
        !Directory.EnumerateFiles(ChannelV6DraftAssetDirectory(assetPath), "*", SearchOption.AllDirectories).Any();
    private static double ChannelV6Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static void ChannelV6Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
    private static void ChannelV6DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
            .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues).Concat(project.ClassificationLayers)) layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }
}
