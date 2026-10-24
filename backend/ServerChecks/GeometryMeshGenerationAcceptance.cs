using System.Text.Json;
using Ras.Backend;

namespace Ras.Backend.ServerChecks;

internal sealed record GeometryMeshGenerationAcceptanceReport(
    string State,
    string FixtureDirectory,
    string ManifestPath,
    string ProjectPath,
    string GeometryPath,
    string SourceRevision,
    string SavedRevision,
    string GenerationOperationID,
    int CellCount,
    int FaceCount,
    int NodeCount,
    bool SourceTopologyComplete,
    bool ExistingMeshReadback,
    bool GenerationSucceeded,
    bool MeshWasReplaced,
    bool NoMeshErrors,
    bool FiniteMeshExtent,
    bool PropertyTablesSucceeded,
    bool PropertyTablesQueuedForParentSave,
    bool PrepareSucceededAfterReopen,
    bool SourceFilesUnchangedBeforeParentSave,
    bool ParentSavePublishedGeometryRevision,
    bool ParentSaveReadback,
    GeometryMeshGenerationOperationSummary[] Operations,
    string[] Diagnostics);

internal sealed record GeometryMeshGenerationOperationSummary(
    string Method,
    string ID,
    string State,
    long Cursor,
    int ProgressEventCount);

internal sealed record GeometryMeshGenerationOperationEvidence(
    string Method,
    string ID,
    string State,
    long Cursor,
    JsonElement[] ProgressEvents,
    JsonElement Result);

/// <summary>
/// Runs the retained Conceptual Mesh generation and parent-save workflow over
/// the actual HTTP Host. All generated files live below a caller-selected,
/// previously nonexistent output directory.
/// </summary>
internal static partial class ServerAcceptance
{
    public static async Task<GeometryMeshGenerationAcceptanceReport> RunGeometryMeshGenerationAsync(
        Uri baseAddress, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Geometry mesh generation acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Geometry mesh generation acceptance requires a new output directory; existing files are never overwritten.");

        string fixtureDirectory = Path.Combine(outputDirectory, "fixture");
        GeometryMeshPatchAcceptanceReport patch = await RunGeometryMeshPatchAsync(baseAddress, fixtureDirectory);
        if (!string.Equals(patch.State, "passed", StringComparison.Ordinal))
            throw new InvalidDataException("The real Cartesian mesh fixture did not finish its existing parent-save acceptance.");
        Dictionary<string, string> sourceFilesBeforeDraft = MeshGenerationCaptureFixtureFiles(fixtureDirectory);
        string geometrySHAAtStart = FileSHA256(patch.GeometryPath);
        string projectSHA = FileSHA256(patch.ProjectPath);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        string projectXmlOnDisk = await File.ReadAllTextAsync(patch.ProjectPath, cancellation);
        JsonElement openedProject = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = patch.ProjectPath }, cancellation)).Result;
        string projectRevision = StringProperty(openedProject, "revision");
        string projectXml = StringProperty(openedProject, "projectXml");
        if (!projectRevision.Equals(projectSHA, StringComparison.OrdinalIgnoreCase) || projectXml != projectXmlOnDisk)
            throw new InvalidDataException("The accepted fixture project changed while opening its parent session.");
        CoreLayerRequestContext? coreLayerContext = PropertyOrNull(openedProject, "coreLayerContext").ValueKind == JsonValueKind.Object
            ? Property(openedProject, "coreLayerContext").Deserialize<CoreLayerRequestContext>(JsonOptions)
            : null;

        var model = new GeometryMeshModelRequest(patch.GeometryPath, "Geometry", patch.ProjectPath,
            projectXml, coreLayerContext);
        JsonElement modelSnapshot = (await SubmitOperationAsync(client, "model.load",
            new ModelRequest(patch.GeometryPath, "Geometry", patch.ProjectPath, projectXml,
                CoreLayerContext: coreLayerContext), cancellation)).Result;
        string sourceRevision = StringProperty(modelSnapshot, "revision");
        if (!sourceRevision.Equals(FileSHA256(patch.GeometryPath), StringComparison.OrdinalIgnoreCase) ||
            !sourceRevision.Equals(patch.SavedRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("model.load did not return the current, accepted Geometry HDF revision.");

        JsonElement loadedCoreContext = PropertyOrNull(modelSnapshot, "coreLayerContext");
        if (loadedCoreContext.ValueKind == JsonValueKind.Object)
            coreLayerContext = loadedCoreContext.Deserialize<CoreLayerRequestContext>(JsonOptions);
        model = model with { CoreLayerContext = coreLayerContext };

        var evidence = new List<GeometryMeshGenerationOperationEvidence>();
        async Task<OperationReceipt> Submit(string method, object parameters)
        {
            OperationReceipt receipt = await SubmitOperationAsync(client, method, parameters, cancellation);
            evidence.Add(new GeometryMeshGenerationOperationEvidence(method, receipt.Id, receipt.State,
                receipt.Cursor, receipt.Events.Select(item => item.Clone()).ToArray(), receipt.Result.Clone()));
            return receipt;
        }

        OperationReceipt topologyReceipt = await Submit("topology.load",
            new GeometryTopologyLoadRequest(new GeometryTopologyModelRequest(model.AssetPath, model.Kind,
                model.ProjectPath, model.ProjectXml, model.CoreLayerContext), sourceRevision));
        GeometryTopologySnapshot topology = MeshGenerationRead<GeometryTopologySnapshot>(topologyReceipt.Result,
            "topology.load");
        GeometryTopologyCompletionResult completion = topology.Completion
            ?? throw new InvalidDataException("topology.load omitted the retained source completion result.");
        bool sourceTopologyComplete = completion.IsComplete &&
            completion.SourceRevision.Equals(sourceRevision, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(completion.DraftIdentity);
        if (!sourceTopologyComplete)
            throw new InvalidDataException("The fixture source topology did not complete at its current source revision: " +
                JsonSerializer.Serialize(completion.Messages, JsonOptions));

        OperationReceipt initialMeshReceipt = await Submit("geometry.mesh.load",
            new GeometryMeshLoadRequest(model, sourceRevision, TopologyDraftState: topology.DraftState));
        GeometryMeshSnapshot initialMesh = MeshGenerationRead<GeometryMeshSnapshot>(initialMeshReceipt.Result,
            "geometry.mesh.load");
        MeshGenerationMetrics initialMetrics = MeshGenerationValidateSnapshot(initialMesh, "initial source mesh");
        bool existingMeshReadback = initialMetrics.CellCount > 0 && initialMetrics.FaceCount > 0 && initialMetrics.NodeCount > 0;
        if (!existingMeshReadback)
            throw new InvalidDataException("The fixture did not reopen with its real source mesh before generation.");

        string generationOperationID = Guid.NewGuid().ToString("N");
        OperationReceipt generateReceipt = await Submit("geometry.mesh.generate", new GeometryMeshGenerateRequest(
            model, sourceRevision, initialMesh.DraftState, topology.DraftState, [], generationOperationID));
        GeometryMeshGenerateResult generated = MeshGenerationRead<GeometryMeshGenerateResult>(generateReceipt.Result,
            "geometry.mesh.generate");
        if (!generated.OperationID.Equals(generationOperationID, StringComparison.Ordinal) ||
            !generated.Revision.Equals(sourceRevision, StringComparison.OrdinalIgnoreCase) || generated.Cancelled)
            throw new InvalidDataException("The duplex mesh-generation receipt did not match its operation, source revision, or non-cancelled request.");
        GeometryMeshGenerationReport generation = generated.Snapshot.GenerationReport
            ?? throw new InvalidDataException("The mesh-generation response omitted its retained source generation report.");
        bool generationSucceeded = generation.Succeeded && !generation.Partial;
        bool meshWasReplaced = generation.MeshWasReplaced;
        if (!generationSucceeded || !meshWasReplaced)
            throw new InvalidDataException("The retained source mesh generator did not report a complete replacement: " +
                JsonSerializer.Serialize(generation, JsonOptions));
        MeshGenerationMetrics generatedMetrics = MeshGenerationValidateSnapshot(generated.Snapshot,
            "generated mesh");

        OperationReceipt propertyTablesReceipt = await Submit("geometry.mesh.computePropertyTables",
            new GeometryMeshPropertyTablesRequest(model, sourceRevision, generated.Snapshot.DraftState,
                topology.DraftState, generated.Snapshot.SelectedRegionIDs));
        GeometryMeshPropertyTablesResult propertyTables = MeshGenerationRead<GeometryMeshPropertyTablesResult>(
            propertyTablesReceipt.Result, "geometry.mesh.computePropertyTables");
        if (!propertyTables.Succeeded || propertyTables.Cancelled ||
            !propertyTables.Snapshot.Revision.Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The retained source property-table calculation did not succeed on the generated mesh: " +
                JsonSerializer.Serialize(propertyTables.Messages, JsonOptions));
        MeshGenerationMetrics propertyTableMetrics = MeshGenerationValidateSnapshot(propertyTables.Snapshot,
            "property-table mesh");
        bool propertyTablesQueuedForParentSave = propertyTableMetrics.PropertyTablesQueuedForParentSave &&
            propertyTableMetrics.HasPropertyTables;
        if (!propertyTablesQueuedForParentSave)
            throw new InvalidDataException("The successful property-table result was not retained as a parent-save mesh draft.");

        OperationReceipt prepareReceipt = await Submit("geometry.mesh.edit", new GeometryMeshEditRequest(
            model, sourceRevision, propertyTables.Snapshot.DraftState, topology.DraftState,
            propertyTables.Snapshot.SelectedRegionIDs, new GeometryMeshMutation("prepare")));
        GeometryMeshSnapshot preparedDraft = MeshGenerationRead<GeometryMeshSnapshot>(prepareReceipt.Result,
            "geometry.mesh.edit/prepare");
        if (!preparedDraft.PreparePending || !preparedDraft.IsDirty ||
            !MeshGenerationDraftContains(preparedDraft.DraftState, "prepare") ||
            !MeshGenerationDraftContains(preparedDraft.DraftState, "computePropertyTables") ||
            !MeshGenerationDraftContains(preparedDraft.DraftState, "generate"))
            throw new InvalidDataException("Prepare, generated-mesh, and property-table intents were not retained together in the parent-save draft.");

        Dictionary<string, string> sourceFilesBeforeParentSave = MeshGenerationCaptureFixtureFiles(fixtureDirectory);
        bool sourceFilesUnchangedBeforeParentSave = MeshGenerationFileSetsEqual(sourceFilesBeforeDraft,
            sourceFilesBeforeParentSave) &&
            geometrySHAAtStart.Equals(FileSHA256(patch.GeometryPath), StringComparison.OrdinalIgnoreCase) &&
            sourceRevision.Equals(FileSHA256(patch.GeometryPath), StringComparison.OrdinalIgnoreCase);
        if (!sourceFilesUnchangedBeforeParentSave)
            throw new InvalidDataException("A mesh child operation changed fixture files before the parent model.save transaction.");

        OperationReceipt saveReceipt = await Submit("model.save", new
        {
            assetPath = patch.GeometryPath,
            kind = "Geometry",
            projectPath = patch.ProjectPath,
            revision = sourceRevision,
            fields = new Dictionary<string, string>(),
            images = Property(modelSnapshot, "images").Clone(),
            projectXml,
            sourceState = OptionalString(modelSnapshot, "sourceState"),
            geometryTopologyState = topology.DraftState,
            geometryMeshState = preparedDraft.DraftState,
            coreLayerContext,
        });
        string savedRevision = StringProperty(saveReceipt.Result, "revision");
        bool parentSavePublishedGeometryRevision =
            !savedRevision.Equals(sourceRevision, StringComparison.OrdinalIgnoreCase) &&
            savedRevision.Equals(FileSHA256(patch.GeometryPath), StringComparison.OrdinalIgnoreCase);
        if (!parentSavePublishedGeometryRevision)
            throw new InvalidDataException("The parent model.save did not publish the new generated Geometry HDF revision.");

        OperationReceipt reopenedProjectReceipt = await Submit("project.open",
            new { projectPath = patch.ProjectPath });
        string reopenedProjectXml = StringProperty(reopenedProjectReceipt.Result, "projectXml");
        if (!StringProperty(reopenedProjectReceipt.Result, "revision").Equals(projectSHA, StringComparison.OrdinalIgnoreCase) ||
            reopenedProjectXml != projectXml || !projectSHA.Equals(FileSHA256(patch.ProjectPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Geometry parent save unexpectedly changed the project XML or its revision.");
        CoreLayerRequestContext? reopenedCoreLayerContext = PropertyOrNull(reopenedProjectReceipt.Result,
                "coreLayerContext").ValueKind == JsonValueKind.Object
            ? Property(reopenedProjectReceipt.Result, "coreLayerContext").Deserialize<CoreLayerRequestContext>(JsonOptions)
            : null;
        var reopenedModel = new GeometryMeshModelRequest(patch.GeometryPath, "Geometry", patch.ProjectPath,
            reopenedProjectXml, reopenedCoreLayerContext);
        OperationReceipt reopenedModelReceipt = await Submit("model.load", new ModelRequest(patch.GeometryPath,
            "Geometry", patch.ProjectPath, reopenedProjectXml, CoreLayerContext: reopenedCoreLayerContext));
        if (!StringProperty(reopenedModelReceipt.Result, "revision").Equals(savedRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("model.load did not reopen the parent-saved Geometry HDF revision.");

        OperationReceipt reopenedMeshReceipt = await Submit("geometry.mesh.load",
            new GeometryMeshLoadRequest(reopenedModel, savedRevision));
        GeometryMeshSnapshot reopenedMesh = MeshGenerationRead<GeometryMeshSnapshot>(reopenedMeshReceipt.Result,
            "geometry.mesh.load/reopen");
        MeshGenerationMetrics reopenedMetrics = MeshGenerationValidateSnapshot(reopenedMesh, "reopened saved mesh");
        bool prepareSucceededAfterReopen = reopenedMesh.PrepareSummary?.Succeeded == true && !reopenedMesh.PreparePending;
        bool parentSaveReadback = !reopenedMesh.IsDirty && reopenedMetrics.CellCount == generatedMetrics.CellCount &&
            reopenedMetrics.FaceCount == generatedMetrics.FaceCount && reopenedMetrics.NodeCount == generatedMetrics.NodeCount &&
            reopenedMetrics.HasPropertyTables && !reopenedMetrics.PropertyTablesQueuedForParentSave &&
            prepareSucceededAfterReopen;
        if (!parentSaveReadback)
            throw new InvalidDataException("The reopened mesh did not retain generated mesh counts, computed property tables, and the completed parent prepare transaction.");

        OperationReceipt reopenedTopologyReceipt = await Submit("topology.load",
            new GeometryTopologyLoadRequest(new GeometryTopologyModelRequest(reopenedModel.AssetPath,
                reopenedModel.Kind, reopenedModel.ProjectPath, reopenedModel.ProjectXml,
                reopenedModel.CoreLayerContext), savedRevision));
        GeometryTopologySnapshot reopenedTopology = MeshGenerationRead<GeometryTopologySnapshot>(
            reopenedTopologyReceipt.Result, "topology.load/reopen");
        GeometryTopologyCompletionResult reopenedCompletion = reopenedTopology.Completion
            ?? throw new InvalidDataException("topology.load/reopen omitted its completion result.");
        if (!reopenedCompletion.IsComplete ||
            !reopenedCompletion.SourceRevision.Equals(savedRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The parent-saved source topology did not reopen complete at the saved geometry revision.");

        Dictionary<string, string> sourceFilesAfterParentSave = MeshGenerationCaptureFixtureFiles(fixtureDirectory);
        bool otherFixtureSourcesUnchanged = sourceFilesBeforeParentSave
            .Where(entry => !Path.GetFullPath(Path.Combine(fixtureDirectory, entry.Key))
                .Equals(Path.GetFullPath(patch.GeometryPath), OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .All(entry => sourceFilesAfterParentSave.TryGetValue(entry.Key, out string? after) &&
                entry.Value.Equals(after, StringComparison.OrdinalIgnoreCase));
        if (!otherFixtureSourcesUnchanged)
            throw new InvalidDataException("The parent mesh save changed a fixture source other than the accepted Geometry HDF.");

        Directory.CreateDirectory(outputDirectory);
        string manifestPath = Path.Combine(outputDirectory, "geometry-mesh-generation-http-e2e-manifest.json");
        GeometryMeshGenerationOperationSummary[] operationSummaries = evidence.Select(item =>
            new GeometryMeshGenerationOperationSummary(item.Method, item.ID, item.State, item.Cursor,
                item.ProgressEvents.Length)).ToArray();
        string[] diagnostics = [.. generation.Messages, .. propertyTables.Messages];
        var manifest = new
        {
            schema = "hecras-geometry-mesh-generation-http-e2e/v1",
            state = "passed",
            fixtureDirectory,
            projectPath = patch.ProjectPath,
            geometryPath = patch.GeometryPath,
            projectRevision,
            sourceRevision,
            savedRevision,
            generationOperationID,
            initialMesh = initialMetrics,
            generatedMesh = generatedMetrics,
            generationReport = generation,
            propertyTables = new { propertyTables.Succeeded, propertyTables.Cancelled, propertyTables.Messages },
            preparedDraft = new { preparedDraft.PreparePending, preparedDraft.IsDirty, preparedDraft.DraftState },
            reopenedMesh = reopenedMesh,
            reopenedTopologyCompletion = reopenedTopology.Completion,
            sourceFilesBeforeParentSave,
            sourceFilesAfterParentSave,
            sourceFilesUnchangedBeforeParentSave,
            otherFixtureSourcesUnchanged,
            parentSavePublishedGeometryRevision,
            prepareSucceededAfterReopen,
            parentSaveReadback,
            operations = evidence,
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellation);

        return new GeometryMeshGenerationAcceptanceReport(
            "passed", fixtureDirectory, manifestPath, patch.ProjectPath, patch.GeometryPath,
            sourceRevision, savedRevision, generationOperationID,
            reopenedMetrics.CellCount, reopenedMetrics.FaceCount, reopenedMetrics.NodeCount,
            sourceTopologyComplete, existingMeshReadback, generationSucceeded, meshWasReplaced,
            generatedMetrics.NoMeshErrors, generatedMetrics.FiniteExtent,
            propertyTables.Succeeded, propertyTablesQueuedForParentSave, prepareSucceededAfterReopen,
            sourceFilesUnchangedBeforeParentSave, parentSavePublishedGeometryRevision, parentSaveReadback,
            operationSummaries, diagnostics);
    }

    private sealed record MeshGenerationMetrics(
        int CellCount,
        int FaceCount,
        int NodeCount,
        bool NoMeshErrors,
        bool FiniteExtent,
        bool HasPropertyTables,
        bool PropertyTablesQueuedForParentSave);

    private static T MeshGenerationRead<T>(JsonElement value, string context) where T : class =>
        value.Deserialize<T>(JsonOptions)
        ?? throw new InvalidDataException($"The {context} HTTP operation returned an empty response.");

    private static MeshGenerationMetrics MeshGenerationValidateSnapshot(GeometryMeshSnapshot snapshot, string context)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Revision) || string.IsNullOrWhiteSpace(snapshot.DraftState))
            throw new InvalidDataException($"The {context} snapshot omitted its source revision or replayable draft.");
        GeometryMeshFlowAreaRow[] rows = snapshot.FlowAreas.Where(row => row.HasMesh).ToArray();
        if (rows.Length == 0 || rows.Length != snapshot.FlowAreas.Length ||
            rows.Any(row => row.CellCount <= 0 || row.FaceCount <= 0 || row.NodeCount <= 0))
            throw new InvalidDataException($"The {context} snapshot did not contain a non-empty mesh with cells, faces, and nodes.");
        bool noMeshErrors = rows.All(row => !row.HasMeshErrors);
        bool finiteExtent = rows.All(row => row.Extent is { } extent &&
            double.IsFinite(extent.MinX) && double.IsFinite(extent.MinY) &&
            double.IsFinite(extent.MaxX) && double.IsFinite(extent.MaxY) &&
            extent.MinX <= extent.MaxX && extent.MinY <= extent.MaxY);
        if (!noMeshErrors || !finiteExtent)
            throw new InvalidDataException($"The {context} snapshot contains mesh errors or a non-finite/invalid extent.");
        return new MeshGenerationMetrics(rows.Sum(row => row.CellCount), rows.Sum(row => row.FaceCount),
            rows.Sum(row => row.NodeCount), noMeshErrors, finiteExtent,
            rows.All(row => row.HasPropertyTables), rows.All(row => row.PropertyTablesQueuedForParentSave));
    }

    private static bool MeshGenerationDraftContains(string draftState, string operation)
    {
        using JsonDocument document = JsonDocument.Parse(draftState);
        JsonElement mutations = Property(document.RootElement, "mutations");
        return mutations.EnumerateArray().Any(item => StringProperty(item, "operation") == operation);
    }

    private static Dictionary<string, string> MeshGenerationCaptureFixtureFiles(string fixtureDirectory)
    {
        string root = Path.GetFullPath(fixtureDirectory);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToDictionary(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                FileSHA256, StringComparer.Ordinal);
    }

    private static bool MeshGenerationFileSetsEqual(Dictionary<string, string> before,
        Dictionary<string, string> after) => before.Count == after.Count && before.All(entry =>
        after.TryGetValue(entry.Key, out string? hash) && entry.Value.Equals(hash, StringComparison.OrdinalIgnoreCase));
}
