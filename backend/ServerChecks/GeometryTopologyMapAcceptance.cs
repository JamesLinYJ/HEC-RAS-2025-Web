using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Ras.Runtime;
using Ras.Backend;

namespace Ras.Backend.ServerChecks;

internal sealed record GeometryTopologyMapAcceptanceReport(
    string State,
    string FixtureDirectory,
    string SourceProjectPath,
    string ProjectPath,
    string GeometryPath,
    string MapID,
    string MapName,
    string MapType,
    string InitialContextToken,
    string ReopenedContextToken,
    string SourceRevision,
    string SavedRevision,
    int SelectedNodeFID,
    string StableNodeMapFeatureID,
    string StableNodeTableRowID,
    double InitialNodeX,
    double InitialNodeY,
    double SavedNodeX,
    double SavedNodeY,
    bool SavedMapCatalogReadback,
    bool MapGeometryContextTokenReadback,
    bool FixtureTopologyPrepared,
    bool MapGeometryTopologySourceReadback,
    bool SpatialSelectionStableIDReadback,
    bool MoveNodeDraftReadback,
    bool DraftUndoReadback,
    bool DraftRedoReadback,
    bool SourceHdfUnchangedBeforeSave,
    bool ParentSaveChangedHdfRevision,
    bool ReopenedCoordinateReadback,
    bool ExpiredDraftRejected,
    bool OriginalFixtureUnchanged);

/// <summary>
/// Exercises the saved-map Geometry topology route through the actual loopback
/// HTTP Host. The caller supplies a saved .ras fixture; its entire
/// project directory is copied into a new output directory before any edits.
/// </summary>
internal static partial class ServerAcceptance
{
    private const string TopologyMapFixturePlanName = "Plan CPU";
    private const string TopologyMapFixtureGeometryName = "Solver Geometry";

    public static async Task<GeometryTopologyMapAcceptanceReport> RunGeometryTopologyMapAsync(
        Uri baseAddress, string sourceProjectPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Geometry topology map acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string sourceProject = Path.GetFullPath(sourceProjectPath);
        if (!File.Exists(sourceProject) || !string.Equals(Path.GetExtension(sourceProject), ".ras", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The acceptance input must be an existing saved .ras project.", sourceProject);
        string sourceDirectory = Path.GetDirectoryName(sourceProject)
            ?? throw new IOException("The source project directory is unavailable.");
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Geometry topology map acceptance requires a new output directory; existing files are never overwritten.");
        string sourceRootPath = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (outputDirectory.StartsWith(sourceRootPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("The acceptance output must be outside the source fixture directory to prevent recursive copying.");

        string sourceVisualizationPath = Path.ChangeExtension(sourceProject, ".vis");
        if (!File.Exists(sourceVisualizationPath))
            throw new FileNotFoundException("The input project must contain its actual saved .vis map catalog.", sourceVisualizationPath);

        string sourceProjectSHA = FileSHA256(sourceProject);
        string sourceVisualizationSHA = FileSHA256(sourceVisualizationPath);
        string fixtureDirectory = Path.Combine(outputDirectory, "fixture");
        string projectPath = CopyProjectDirectory(sourceDirectory, sourceProject, fixtureDirectory);
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!File.Exists(visualizationPath) || FileSHA256(visualizationPath) != sourceVisualizationSHA)
            throw new InvalidDataException("The copied saved map catalog does not match the read-only source fixture.");
        TopologyMapFixturePreparation topologyFixture = PrepareTopologyMapFixture(projectPath);
        string copiedGeometryPath = topologyFixture.GeometryPath;
        if (!PathIsInside(copiedGeometryPath, fixtureDirectory))
            throw new InvalidDataException("The prepared conceptual topology did not resolve inside the isolated project copy.");
        string copiedGeometrySHABeforeTopologyPreparation = topologyFixture.InitialSHA256;
        string preparedFixtureGeometrySHA = topologyFixture.PreparedSHA256;
        string sourceGeometryPath = Path.Combine(sourceDirectory, Path.GetRelativePath(fixtureDirectory, copiedGeometryPath));
        if (!File.Exists(copiedGeometryPath) || !File.Exists(sourceGeometryPath))
            throw new FileNotFoundException("The saved map project or its copied source Geometry HDF is missing.", copiedGeometryPath);
        if (string.Equals(preparedFixtureGeometrySHA, copiedGeometrySHABeforeTopologyPreparation, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The isolated fixture's conceptual topology preparation did not change its copied Geometry HDF.");
        if (!string.Equals(FileSHA256(sourceProject), sourceProjectSHA, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(FileSHA256(sourceVisualizationPath), sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(FileSHA256(sourceGeometryPath), copiedGeometrySHABeforeTopologyPreparation, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Preparing conceptual topology modified the read-only source fixture instead of only the isolated copy.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        var operations = new List<object>();
        OperationReceipt projectOpen = await SubmitOperationAsync(client, "project.open",
            new { projectPath }, cancellation);
        operations.Add(OperationEntry("project.open", projectOpen));
        string projectRevision = StringProperty(projectOpen.Result, "revision");
        string projectXml = StringProperty(projectOpen.Result, "projectXml");
        string projectXmlSHA = TextSHA256(projectXml);
        if (!IsSHA256(projectRevision) || !string.Equals(FileSHA256(projectPath), projectRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The copied project.open revision does not match the copied .ras bytes.");

        JsonElement coreLayerContextElement = PropertyOrNull(projectOpen.Result, "coreLayerContext");
        object? coreLayerContext = coreLayerContextElement.ValueKind is JsonValueKind.Object
            ? coreLayerContextElement.Clone()
            : null;
        string projectSessionID = Guid.NewGuid().ToString("D");

        OperationReceipt mapList = await SubmitOperationAsync(client, "map.document.list", new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            expectedProjectRevision = projectRevision,
            coreLayerContext,
        }, cancellation);
        operations.Add(OperationEntry("map.document.list", mapList));
        string visualizationRevision = StringProperty(mapList.Result, "visualizationRevision");
        if (!IsSHA256(visualizationRevision) || !string.Equals(FileSHA256(visualizationPath), visualizationRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved map catalog revision does not match the copied .vis file.");

        var matchingOwners = new List<MapGeometryOwner>();
        foreach (JsonElement map in Property(mapList.Result, "maps").EnumerateArray())
        {
            int mapIndex = IntProperty(map, "mapIndex");
            string mapName = StringProperty(map, "mapName");
            string mapType = StringProperty(map, "mapType");
            if (mapType != "Plan") continue;
            string mapID = "map-" + mapIndex.ToString(CultureInfo.InvariantCulture);
            object mapRequest = new
            {
                projectPath,
                projectXml,
                projectXmlSHA,
                mapID,
                mapIndex,
                mapName,
                mapType,
                expectedVisualizationRevision = visualizationRevision,
                projectSessionID,
                coreLayerContext,
            };
            OperationReceipt treeOperation = await SubmitOperationAsync(client, "map.tree.load", mapRequest, cancellation);
            operations.Add(OperationEntry("map.tree.load", treeOperation));
            MapTreeSnapshot tree = JsonSerializer.Deserialize<MapTreeSnapshot>(treeOperation.Result.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("The saved map tree response was empty.");
            if (!string.Equals(tree.VisualizationRevision, visualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The saved map tree returned an unexpected .vis revision.");

            MapGeometryOwner? match = FindGeometryOwner(mapID, mapIndex, mapName, mapType,
                visualizationRevision, tree.Roots, mapRequest, topologyFixture.PlanName,
                topologyFixture.GeometryName, copiedGeometryPath);
            if (match is not null) matchingOwners.Add(match);
        }
        if (matchingOwners.Count != 1)
            throw new InvalidDataException("The saved .vis catalog must have exactly one focused Plan Geometry locator matching its .ras association and prepared source path.");
        MapGeometryOwner owner = matchingOwners[0];

        OperationReceipt mapGeometryOperation = await SubmitOperationAsync(client, "map.geometry.load", new
        {
            map = owner.MapRequest,
            geometryNode = owner.GeometryNode,
            pageSize = 512,
            skipFlowAreasPage = true,
            skipCulvertsPage = true,
            planNodes = owner.PlanNodes,
        }, cancellation);
        operations.Add(OperationEntry("map.geometry.load", mapGeometryOperation));
        JsonElement mapGeometry = mapGeometryOperation.Result;
        string geometryPath = StringProperty(mapGeometry, "geometryCanonicalPath");
        string sourceRevision = StringProperty(mapGeometry, "geometryRevision");
        string contextToken = StringProperty(mapGeometry, "contextToken");
        if (!IsSHA256(sourceRevision) || !IsSHA256(contextToken) ||
            StringProperty(Property(mapGeometry, "topologyState"), "status") != "available" ||
            !PathIsInside(geometryPath, fixtureDirectory))
            throw new InvalidDataException("map.geometry.load did not return a source Geometry, available topology, and exact Host context token.");
        string initialGeometrySHA = FileSHA256(geometryPath);
        string originalGeometrySHA = FileSHA256(sourceGeometryPath);
        if (!string.Equals(initialGeometrySHA, sourceRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(originalGeometrySHA, copiedGeometrySHABeforeTopologyPreparation, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(initialGeometrySHA, preparedFixtureGeometrySHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The checked map Geometry HDF does not match the prepared isolated topology or its captured revision.");
        string initialVisualizationOnDisk = FileSHA256(visualizationPath);

        object geometryModel = new
        {
            assetPath = geometryPath,
            kind = "Geometry",
            projectPath,
            projectXml,
            coreLayerContext,
        };
        OperationReceipt modelLoad = await SubmitOperationAsync(client, "model.load", geometryModel, cancellation);
        operations.Add(OperationEntry("model.load", modelLoad));
        if (!string.Equals(StringProperty(modelLoad.Result, "revision"), sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("model.load and map.geometry.load did not capture the same source Geometry revision.");

        OperationReceipt topologyLoad = await SubmitOperationAsync(client, "topology.load", new
        {
            model = geometryModel,
            revision = sourceRevision,
        }, cancellation);
        operations.Add(OperationEntry("topology.load", topologyLoad));
        JsonElement initialTopology = topologyLoad.Result;
        RequireTopologyOwner(initialTopology, sourceRevision);
        if (!BooleanProperty(initialTopology, "canEdit"))
            throw new InvalidDataException("The saved Geometry topology is not editable through the source model.");

        JsonElement topologyMap = Property(initialTopology, "map");
        JsonElement nodeFeature = ChooseStableTopologyNode(topologyMap);
        int selectedNodeFID = IntProperty(nodeFeature, "fid");
        string stableNodeMapFeatureID = StringProperty(nodeFeature, "id");
        JsonElement initialPoint = FirstPathCoordinate(nodeFeature);
        double initialX = TopologyMapNumberProperty(initialPoint, "x");
        double initialY = TopologyMapNumberProperty(initialPoint, "y");
        JsonElement[] mapTopologyMatches = Property(Property(mapGeometry, "topology"), "features").EnumerateArray()
            .Where(feature => StringProperty(feature, "featureKind") == "node" &&
                PropertyOrNull(feature, "fid").ValueKind == JsonValueKind.Number && IntProperty(feature, "fid") == selectedNodeFID)
            .ToArray();
        bool mapGeometryTopologySourceReadback = mapTopologyMatches.Length == 1 &&
            StringProperty(mapTopologyMatches[0], "id") == stableNodeMapFeatureID &&
            TopologyMapNumberProperty(FirstPathCoordinate(mapTopologyMatches[0]), "x") == initialX &&
            TopologyMapNumberProperty(FirstPathCoordinate(mapTopologyMatches[0]), "y") == initialY;
        if (!mapGeometryTopologySourceReadback)
            throw new InvalidDataException("map.geometry.load and topology.load did not expose the same prepared stable topology feature.");
        double[] otherNodeDistances = Property(topologyMap, "features").EnumerateArray()
            .Where(feature => StringProperty(feature, "featureKind") == "node" &&
                PropertyOrNull(feature, "fid").ValueKind == JsonValueKind.Number &&
                IntProperty(feature, "fid") != selectedNodeFID)
            .Select(feature => FirstPathCoordinate(feature))
            .Select(point => Math.Sqrt(Math.Pow(TopologyMapNumberProperty(point, "x") - initialX, 2) +
                Math.Pow(TopologyMapNumberProperty(point, "y") - initialY, 2)))
            .Where(distance => double.IsFinite(distance) && distance > 0d)
            .ToArray();
        if (otherNodeDistances.Length == 0)
            throw new InvalidDataException("The saved topology has no second stable source node for a safe move distance.");
        double nearestNodeDistance = otherNodeDistances.Min();
        JsonElement topologyExtent = Property(topologyMap, "extent");
        double centerX = (TopologyMapNumberProperty(topologyExtent, "minX") + TopologyMapNumberProperty(topologyExtent, "maxX")) / 2d;
        double centerY = (TopologyMapNumberProperty(topologyExtent, "minY") + TopologyMapNumberProperty(topologyExtent, "maxY")) / 2d;
        double moveX = initialX + (initialX <= centerX ? 1d : -1d) * Math.Min(0.125d, nearestNodeDistance / 8d);
        double moveY = initialY + (initialY <= centerY ? 1d : -1d) * Math.Min(0.0625d, nearestNodeDistance / 16d);
        double mapCellSize = MapCellSize(topologyMap);
        string initialDraftState = StringProperty(initialTopology, "draftState");
        string initialDraftIdentity = StringProperty(Property(initialTopology, "completion"), "draftIdentity");

        OperationReceipt spatialSelection = await SubmitOperationAsync(client, "topology.spatialSelect", new
        {
            requestID = Guid.NewGuid().ToString("D"),
            model = geometryModel,
            revision = sourceRevision,
            draftState = initialDraftState,
            draftIdentity = initialDraftIdentity,
            mapID = owner.MapID,
            contextToken,
            geometryRevision = sourceRevision,
            selectionScope = "Nodes",
            selectionMode = "Replace",
            gesture = "Click",
            worldPoints = new[] { new { x = initialX, y = initialY } },
            mapCellSize,
            marqueeHitMode = (string?)null,
            currentArcFIDs = Array.Empty<int>(),
            currentNodeFIDs = Array.Empty<int>(),
            currentRegionFIDs = Array.Empty<int>(),
        }, cancellation);
        operations.Add(OperationEntry("topology.spatialSelect", spatialSelection));
        JsonElement[] selectedNodes = Property(spatialSelection.Result, "selections").EnumerateArray()
            .Where(selection => StringProperty(selection, "featureKind") == "node").ToArray();
        if (selectedNodes.Length != 1 || IntProperty(selectedNodes[0], "fid") != selectedNodeFID)
            throw new InvalidDataException("Spatial selection did not return exactly the clicked stable source-node FID.");
        string stableNodeTableRowID = StringProperty(selectedNodes[0], "tableRowID");
        if (stableNodeTableRowID != Property(nodeFeature, "selectionIDs").EnumerateArray().Single().GetString() ||
            !Property(selectedNodes[0], "mapFeatureIDs").EnumerateArray().Any(item => item.GetString() == stableNodeMapFeatureID))
            throw new InvalidDataException("Spatial selection lost the stable topology row or exact map feature identity.");
        if (StringProperty(spatialSelection.Result, "contextToken") != contextToken ||
            StringProperty(spatialSelection.Result, "draftIdentity") != initialDraftIdentity ||
            StringProperty(spatialSelection.Result, "geometryRevision") != sourceRevision)
            throw new InvalidDataException("Spatial selection did not echo the exact saved map and topology draft owner.");

        object mapRequestContext = new
        {
            requestID = Guid.NewGuid().ToString("D"),
            mapID = owner.MapID,
            contextToken,
            geometryRevision = sourceRevision,
            expectedDraftIdentity = initialDraftIdentity,
        };
        OperationReceipt movedOperation = await SubmitOperationAsync(client, "topology.edit", new
        {
            model = geometryModel,
            revision = sourceRevision,
            draftState = initialDraftState,
            mapRequestContext,
            mutation = new
            {
                operation = "mapCommand",
                featureKind = "map",
                featureIDs = Array.Empty<int>(),
                mapCommand = new
                {
                    action = "moveNode",
                    featureKind = "node",
                    featureIDs = Array.Empty<int>(),
                    nodeFID = selectedNodeFID,
                    nodeFIDs = new[] { selectedNodeFID },
                    coordinates = new[] { new { x = moveX, y = moveY } },
                    mapCellSize,
                },
            },
        }, cancellation);
        operations.Add(OperationEntry("topology.edit/mapCommand/moveNode", movedOperation));
        JsonElement moved = movedOperation.Result;
        JsonElement movedNode = FindNodeFeature(moved, selectedNodeFID);
        JsonElement movedPoint = FirstPathCoordinate(movedNode);
        double movedX = TopologyMapNumberProperty(movedPoint, "x");
        double movedY = TopologyMapNumberProperty(movedPoint, "y");
        if (movedX != moveX || movedY != moveY || !BooleanProperty(moved, "isDirty") ||
            !BooleanProperty(moved, "canUndo") || StringProperty(movedNode, "id") != stableNodeMapFeatureID)
            throw new InvalidDataException("The typed mapCommand moveNode did not update the exact stable node in the unsaved draft.");
        using (JsonDocument moveDraft = JsonDocument.Parse(StringProperty(moved, "draftState")))
        {
            JsonElement mutations = Property(moveDraft.RootElement, "mutations");
            if (mutations.GetArrayLength() != 1 || StringProperty(mutations[0], "operation") != "mapCommand" ||
                StringProperty(Property(mutations[0], "mapCommand"), "action") != "moveNode")
                throw new InvalidDataException("moveNode was not recorded as one replayable source map-command draft mutation.");
        }

        OperationReceipt undoOperation = await SubmitOperationAsync(client, "topology.edit", new
        {
            model = geometryModel,
            revision = sourceRevision,
            draftState = StringProperty(moved, "draftState"),
            mutation = new { operation = "undo", featureKind = "history", featureIDs = Array.Empty<int>() },
        }, cancellation);
        operations.Add(OperationEntry("topology.edit/undo", undoOperation));
        JsonElement undone = undoOperation.Result;
        JsonElement undonePoint = FirstPathCoordinate(FindNodeFeature(undone, selectedNodeFID));
        bool undoReadback = TopologyMapNumberProperty(undonePoint, "x") == initialX && TopologyMapNumberProperty(undonePoint, "y") == initialY &&
            !BooleanProperty(undone, "canUndo") && BooleanProperty(undone, "canRedo");
        if (!undoReadback) throw new InvalidDataException("Undo did not restore the captured source-node coordinate and redo state.");

        OperationReceipt redoOperation = await SubmitOperationAsync(client, "topology.edit", new
        {
            model = geometryModel,
            revision = sourceRevision,
            draftState = StringProperty(undone, "draftState"),
            mutation = new { operation = "redo", featureKind = "history", featureIDs = Array.Empty<int>() },
        }, cancellation);
        operations.Add(OperationEntry("topology.edit/redo", redoOperation));
        JsonElement redone = redoOperation.Result;
        JsonElement redonePoint = FirstPathCoordinate(FindNodeFeature(redone, selectedNodeFID));
        bool redoReadback = TopologyMapNumberProperty(redonePoint, "x") == movedX && TopologyMapNumberProperty(redonePoint, "y") == movedY &&
            BooleanProperty(redone, "canUndo") && !BooleanProperty(redone, "canRedo");
        if (!redoReadback) throw new InvalidDataException("Redo did not restore the accepted map moveNode coordinate.");

        bool sourceHdfUnchangedBeforeSave = FileSHA256(geometryPath) == initialGeometrySHA &&
            FileSHA256(projectPath) == projectRevision && FileSHA256(visualizationPath) == initialVisualizationOnDisk;
        if (!sourceHdfUnchangedBeforeSave)
            throw new InvalidDataException("The map/topology draft changed the saved Geometry, .ras, or .vis before model.save.");

        OperationReceipt save = await SubmitOperationAsync(client, "model.save", new
        {
            assetPath = geometryPath,
            kind = "Geometry",
            projectPath,
            revision = sourceRevision,
            fields = new Dictionary<string, string>(),
            images = Property(modelLoad.Result, "images"),
            projectXml,
            sourceState = OptionalString(modelLoad.Result, "sourceState"),
            geometryTopologyState = StringProperty(redone, "draftState"),
            coreLayerContext,
        }, cancellation);
        operations.Add(OperationEntry("model.save", save));
        string savedRevision = StringProperty(save.Result, "revision");
        string savedGeometrySHA = FileSHA256(geometryPath);
        bool parentSaveChangedHdfRevision = IsSHA256(savedRevision) &&
            !string.Equals(savedRevision, sourceRevision, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(savedGeometrySHA, savedRevision, StringComparison.OrdinalIgnoreCase);
        if (!parentSaveChangedHdfRevision)
            throw new InvalidDataException("model.save did not publish the changed Geometry HDF at its returned revision.");

        OperationReceipt reopenedProject = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        operations.Add(OperationEntry("project.open/reopen", reopenedProject));
        string reopenedProjectRevision = StringProperty(reopenedProject.Result, "revision");
        string reopenedProjectXml = StringProperty(reopenedProject.Result, "projectXml");
        if (!string.Equals(reopenedProjectRevision, projectRevision, StringComparison.OrdinalIgnoreCase) ||
            reopenedProjectXml != projectXml || FileSHA256(projectPath) != projectRevision ||
            FileSHA256(visualizationPath) != initialVisualizationOnDisk)
            throw new InvalidDataException("The parent Geometry save unexpectedly changed the source project XML or saved map catalog.");

        object reopenedModelRequest = new
        {
            assetPath = geometryPath,
            kind = "Geometry",
            projectPath,
            projectXml = reopenedProjectXml,
            coreLayerContext,
        };
        OperationReceipt reopenedModel = await SubmitOperationAsync(client, "model.load", reopenedModelRequest, cancellation);
        operations.Add(OperationEntry("model.load/reopen", reopenedModel));
        if (StringProperty(reopenedModel.Result, "revision") != savedRevision)
            throw new InvalidDataException("model.load did not reopen the saved Geometry revision.");
        OperationReceipt reopenedTopologyOperation = await SubmitOperationAsync(client, "topology.load", new
        {
            model = reopenedModelRequest,
            revision = savedRevision,
        }, cancellation);
        operations.Add(OperationEntry("topology.load/reopen", reopenedTopologyOperation));
        JsonElement reopenedTopology = reopenedTopologyOperation.Result;
        RequireTopologyOwner(reopenedTopology, savedRevision);
        JsonElement reopenedPoint = FirstPathCoordinate(FindNodeFeature(reopenedTopology, selectedNodeFID));
        bool reopenedCoordinateReadback = TopologyMapNumberProperty(reopenedPoint, "x") == movedX &&
            TopologyMapNumberProperty(reopenedPoint, "y") == movedY && !BooleanProperty(reopenedTopology, "isDirty") &&
            !BooleanProperty(reopenedTopology, "canUndo") && !BooleanProperty(reopenedTopology, "canRedo");
        if (!reopenedCoordinateReadback)
            throw new InvalidDataException("Reopened topology did not retain the moved node coordinate as a clean saved baseline.");

        OperationReceipt reopenedMapTreeOperation = await SubmitOperationAsync(client, "map.tree.load", owner.MapRequest, cancellation);
        operations.Add(OperationEntry("map.tree.load/reopen", reopenedMapTreeOperation));
        MapTreeSnapshot reopenedMapTree = JsonSerializer.Deserialize<MapTreeSnapshot>(
                reopenedMapTreeOperation.Result.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("The reopened saved map tree response was empty.");
        if (!string.Equals(reopenedMapTree.VisualizationRevision, visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The reopened saved map tree returned an unexpected .vis revision.");
        MapGeometryOwner? reopenedOwner = FindGeometryOwner(owner.MapID, owner.MapIndex, owner.MapName,
            owner.MapType, visualizationRevision, reopenedMapTree.Roots, owner.MapRequest,
            topologyFixture.PlanName, topologyFixture.GeometryName, copiedGeometryPath);
        if (reopenedOwner is null ||
            JsonSerializer.Serialize(reopenedOwner.GeometryNode, JsonOptions) != JsonSerializer.Serialize(owner.GeometryNode, JsonOptions) ||
            JsonSerializer.Serialize(reopenedOwner.PlanNodes, JsonOptions) != JsonSerializer.Serialize(owner.PlanNodes, JsonOptions))
            throw new InvalidDataException("Reopening the saved map did not resolve the same exact Geometry and Plan owner locators.");
        OperationReceipt reopenedMapGeometryOperation = await SubmitOperationAsync(client, "map.geometry.load", new
        {
            map = reopenedOwner.MapRequest,
            geometryNode = reopenedOwner.GeometryNode,
            pageSize = 512,
            skipFlowAreasPage = true,
            skipCulvertsPage = true,
            planNodes = reopenedOwner.PlanNodes,
        }, cancellation);
        operations.Add(OperationEntry("map.geometry.load/reopen", reopenedMapGeometryOperation));
        string reopenedContextToken = StringProperty(reopenedMapGeometryOperation.Result, "contextToken");
        if (StringProperty(reopenedMapGeometryOperation.Result, "geometryRevision") != savedRevision ||
            !IsSHA256(reopenedContextToken) || string.Equals(reopenedContextToken, contextToken, StringComparison.Ordinal))
            throw new InvalidDataException("The saved Geometry map reload did not issue a fresh context token for the new source revision.");

        bool expiredDraftRejected = await OperationRejectedWithCodeAsync(client, "topology.edit", new
        {
            model = geometryModel,
            revision = sourceRevision,
            draftState = StringProperty(redone, "draftState"),
            mutation = new { operation = "undo", featureKind = "history", featureIDs = Array.Empty<int>() },
        }, "sourceChanged", cancellation);
        if (!expiredDraftRejected)
            throw new InvalidDataException("A topology draft from before model.save was not rejected with sourceChanged.");

        bool originalFixtureUnchanged = FileSHA256(sourceProject) == sourceProjectSHA &&
            FileSHA256(sourceVisualizationPath) == sourceVisualizationSHA && FileSHA256(sourceGeometryPath) == originalGeometrySHA;
        if (!originalFixtureUnchanged)
                throw new InvalidDataException("The saved source fixture changed; the acceptance must edit only its copied fixture.");

        string manifestPath = Path.Combine(outputDirectory, "geometry-topology-map-http-e2e-manifest.json");
        var manifest = new
        {
            schema = "hecras-geometry-topology-map-http-e2e/v1",
            sourceProjectPath = sourceProject,
            sourceProjectSHA256 = sourceProjectSHA,
            sourceVisualizationSHA256 = sourceVisualizationSHA,
            sourceGeometrySHA256 = originalGeometrySHA,
            fixtureDirectory,
            projectPath,
            projectRevision,
            projectXmlSHA,
            geometryPath,
            initialGeometrySHA256 = initialGeometrySHA,
            copiedGeometrySHA256BeforeTopologyPreparation = copiedGeometrySHABeforeTopologyPreparation,
            fixtureTopologyPrepared = true,
            savedGeometrySHA256 = savedGeometrySHA,
            map = new { owner.MapID, owner.MapIndex, owner.MapName, owner.MapType, visualizationRevision },
            initialContextToken = contextToken,
            reopenedContextToken,
            node = new { selectedNodeFID, stableNodeMapFeatureID, stableNodeTableRowID,
                initialX, initialY, savedX = movedX, savedY = movedY },
            savedMapCatalogReadback = true,
            mapGeometryContextTokenReadback = true,
            mapGeometryTopologySourceReadback,
            spatialSelectionStableIDReadback = true,
            moveNodeDraftReadback = true,
            draftUndoReadback = undoReadback,
            draftRedoReadback = redoReadback,
            sourceHdfUnchangedBeforeSave,
            parentSaveChangedHdfRevision,
            reopenedCoordinateReadback,
            expiredDraftRejected,
            originalFixtureUnchanged,
            operations,
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false), cancellation);

        return new GeometryTopologyMapAcceptanceReport("passed", fixtureDirectory, sourceProject, projectPath,
            geometryPath, owner.MapID, owner.MapName, owner.MapType, contextToken, reopenedContextToken,
            sourceRevision, savedRevision, selectedNodeFID, stableNodeMapFeatureID, stableNodeTableRowID,
            initialX, initialY, movedX, movedY, SavedMapCatalogReadback: true,
            MapGeometryContextTokenReadback: true, FixtureTopologyPrepared: true,
            MapGeometryTopologySourceReadback: mapGeometryTopologySourceReadback,
            SpatialSelectionStableIDReadback: true,
            MoveNodeDraftReadback: true, DraftUndoReadback: undoReadback, DraftRedoReadback: redoReadback,
            SourceHdfUnchangedBeforeSave: sourceHdfUnchangedBeforeSave,
            ParentSaveChangedHdfRevision: parentSaveChangedHdfRevision,
            ReopenedCoordinateReadback: reopenedCoordinateReadback,
            ExpiredDraftRejected: expiredDraftRejected, OriginalFixtureUnchanged: originalFixtureUnchanged);
    }

    private static string CopyProjectDirectory(string sourceDirectory, string sourceProjectPath, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        string sourceRoot = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            FileAttributes attributes = File.GetAttributes(sourceFile);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The saved map fixture contains a symbolic or reparse-point file and cannot be copied safely.");
            string relative = Path.GetRelativePath(sourceDirectory, sourceFile);
            string targetFile = Path.GetFullPath(Path.Combine(targetDirectory, relative));
            if (!targetFile.StartsWith(Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("A saved map fixture file escaped its copy directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: false);
        }
        if (!Path.GetFullPath(sourceProjectPath).StartsWith(sourceRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("The requested .ras file is outside the copied fixture directory.");
        string projectCopy = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, sourceProjectPath));
        if (!File.Exists(projectCopy)) throw new FileNotFoundException("The copied .ras project was not found.", projectCopy);
        return projectCopy;
    }

    private static TopologyMapFixturePreparation PrepareTopologyMapFixture(string projectPath)
    {
        RasRuntimeBootstrap.Configure();
        Project project = new(projectPath);
        try
        {
            Plan[] associatedPlans = project.Plans.Where(plan =>
                string.Equals(plan.Name, TopologyMapFixturePlanName, StringComparison.Ordinal)).ToArray();
            if (associatedPlans.Length != 1)
                throw new InvalidDataException("The saved project must contain exactly one Plan CPU association.");
            Plan plan = associatedPlans[0];
            PlanAssociation association = plan.PlanAssociation;
            Geometry? associatedGeometry = association.Geometry;
            if (associatedGeometry is null ||
                !string.Equals(association.GeometryName, TopologyMapFixtureGeometryName, StringComparison.Ordinal) ||
                !string.Equals(associatedGeometry.Name, TopologyMapFixtureGeometryName, StringComparison.Ordinal))
                throw new InvalidDataException("The saved Plan CPU GeometryAssociation must resolve exactly to Solver Geometry.");

            string geometryPath = Path.GetFullPath(associatedGeometry.Filename);
            string relativeGeometryPath = Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, geometryPath);
            if (!string.Equals(relativeGeometryPath,
                    Path.Combine("Geometries", TopologyMapFixtureGeometryName + ".h5"),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("The saved Plan CPU association did not resolve to the fixture's exact Geometries/Solver Geometry.h5 path.");

            Geometry[] matchingGeometries = project.Geometries.Where(geometry =>
                SameFixturePath(Path.GetFullPath(geometry.Filename), geometryPath)).ToArray();
            if (matchingGeometries.Length != 1 || !ReferenceEquals(matchingGeometries[0], associatedGeometry))
                throw new InvalidDataException("The Plan CPU association must match exactly one Geometry object at Solver Geometry.h5.");
            Geometry geometry = matchingGeometries[0];
            string initialSHA256 = FileSHA256(geometryPath);
            if (geometry.ConceptualMeshLayer.LoadFailed || !geometry.ConceptualMeshLayer.IsDataLoaded ||
                !geometry.ConceptualMeshLayer.Topology.IsEmpty())
                throw new InvalidDataException("The saved input must have a successfully loaded empty conceptual topology before fixture preparation.");
            Topology topology = Topology.FromExtent(new Extent(0, 4, 0, 4));
            topology.EnsureMeshMetadataColumns();
            topology.SetControlPointData(0, PatchType.Cartesian, SizeFunction.Constant,
                2f, stretchRate: 0.1f, secondaryCellSize: 5f, rotationDegrees: 0f,
                recipeSource: "Default()", multipointPrecision: 0f, sizeTolerance: 1f);
            for (int arcID = 0; arcID < topology.Arcs.Count; arcID++)
                topology.SetArcData(arcID, LinearSizeFunction.Constant, 2f, 4, 1f);
            if (topology.Nodes.Count != 4 || topology.Arcs.Count != 4 || topology.RegionControlPoints.Count != 1)
                throw new InvalidDataException("The isolated topology fixture must contain the exact 4-node, 4-arc, 1-region Cartesian topology.");
            geometry.ConceptualMeshLayer.Topology = topology;
            geometry.Save();
            return new TopologyMapFixturePreparation(geometryPath, initialSHA256, FileSHA256(geometryPath),
                TopologyMapFixturePlanName, TopologyMapFixtureGeometryName);
        }
        finally
        {
            DisposeTopologyMapProject(project);
        }
    }

    private static void DisposeTopologyMapProject(Project project)
    {
        foreach (Result result in project.Results) result.Dispose();
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                     .Concat(project.Geometries).Concat(project.BoundaryConditions)
                     .Concat(project.Terrains).Concat(project.NValues).Concat(project.ClassificationLayers))
            layer.Dispose();
    }

    private static MapGeometryOwner? FindGeometryOwner(string mapID, int mapIndex, string mapName,
        string mapType, string visualizationRevision, CatalogTreeNode[] roots, object mapRequest,
        string expectedPlanName, string expectedGeometryName, string expectedGeometryPath)
    {
        if (mapType != "Plan") return null;
        CatalogTreeNode[] nodes = WalkNodes(roots).ToArray();
        foreach (CatalogTreeNode node in nodes)
        {
            CatalogTreeNodeLocator? geometry = node.Locator;
            if (geometry is null || geometry.NodeType != "Geometry" || string.IsNullOrWhiteSpace(geometry.AssetID)) continue;
            if (geometry.RootName != "Focused Plans" ||
                !string.Equals(geometry.NodeName, expectedGeometryName, StringComparison.Ordinal) ||
                !SameFixturePath(geometry.AssetID, expectedGeometryPath) ||
                geometry.ParentPlanAssetID is not { Length: > 0 } parentAsset ||
                geometry.ParentPlanNodePath is not { Length: >= 2 } parentPath ||
                geometry.ParentPlanNodeName is not { Length: > 0 } parentName ||
                !string.Equals(parentName, expectedPlanName, StringComparison.Ordinal) ||
                geometry.AssociationKind != "Geometry" || geometry.AssociationAssetID is not { Length: > 0 } association ||
                !SameFixturePath(association, geometry.AssetID))
                continue;
            CatalogTreeNodeLocator[] matchingPlans = nodes.Select(item => item.Locator)
                .Where(locator => locator is { RootName: "Focused Plans", NodeType: "Plan", NodePath.Length: >= 2 } &&
                    locator.NodePath!.SequenceEqual(parentPath) && locator.NodeName == parentName &&
                    locator.AssetID is { Length: > 0 } planAsset && SameFixturePath(planAsset, parentAsset))
                .Cast<CatalogTreeNodeLocator>().ToArray();
            if (matchingPlans.Length != 1) continue;
            return new MapGeometryOwner(mapID, mapIndex, mapName, mapType, visualizationRevision,
                geometry, matchingPlans, mapRequest);
        }
        return null;
    }

    private static IEnumerable<CatalogTreeNode> WalkNodes(IEnumerable<CatalogTreeNode> roots)
    {
        foreach (CatalogTreeNode node in roots)
        {
            yield return node;
            foreach (CatalogTreeNode child in WalkNodes(node.Children ?? [])) yield return child;
        }
    }

    private static JsonElement ChooseStableTopologyNode(JsonElement map)
    {
        JsonElement[] nodes = Property(map, "features").EnumerateArray()
            .Where(feature => StringProperty(feature, "featureKind") == "node" &&
                PropertyOrNull(feature, "fid").ValueKind == JsonValueKind.Number &&
                PropertyOrNull(feature, "incidentArcs").ValueKind == JsonValueKind.Array &&
                Property(feature, "incidentArcs").GetArrayLength() > 0)
            .ToArray();
        if (nodes.Length == 0)
            throw new InvalidDataException("The prepared conceptual topology has no stable source node with an incident arc.");
        JsonElement extent = Property(map, "extent");
        double centerX = (TopologyMapNumberProperty(extent, "minX") + TopologyMapNumberProperty(extent, "maxX")) / 2d;
        double centerY = (TopologyMapNumberProperty(extent, "minY") + TopologyMapNumberProperty(extent, "maxY")) / 2d;
        return nodes.OrderByDescending(feature => Property(feature, "incidentArcs").GetArrayLength())
            .ThenBy(feature =>
        {
            JsonElement point = FirstPathCoordinate(feature);
            double dx = TopologyMapNumberProperty(point, "x") - centerX;
            double dy = TopologyMapNumberProperty(point, "y") - centerY;
            return dx * dx + dy * dy;
        }).First();
    }

    private static JsonElement FindNodeFeature(JsonElement topology, int fid)
    {
        JsonElement[] matches = Property(Property(topology, "map"), "features").EnumerateArray()
            .Where(feature => StringProperty(feature, "featureKind") == "node" &&
                PropertyOrNull(feature, "fid").ValueKind == JsonValueKind.Number && IntProperty(feature, "fid") == fid)
            .ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException(
            "The topology map did not contain exactly one feature for stable source node FID " + fid.ToString(CultureInfo.InvariantCulture) + ".");
    }

    private static JsonElement FirstPathCoordinate(JsonElement feature)
    {
        JsonElement paths = Property(feature, "paths");
        if (paths.GetArrayLength() == 0 || paths[0].GetArrayLength() == 0)
            throw new InvalidDataException("A stable map feature has no coordinate path.");
        return paths[0][0];
    }

    private static double MapCellSize(JsonElement map)
    {
        JsonElement extent = Property(map, "extent");
        double width = TopologyMapNumberProperty(extent, "maxX") - TopologyMapNumberProperty(extent, "minX");
        double height = TopologyMapNumberProperty(extent, "maxY") - TopologyMapNumberProperty(extent, "minY");
        double result = Math.Max(width / 1200d, height / 800d);
        return double.IsFinite(result) && result > 0d ? result : throw new InvalidDataException(
            "The saved topology map extent cannot provide a positive world-units-per-pixel cell size.");
    }

    private static void RequireTopologyOwner(JsonElement topology, string expectedRevision)
    {
        if (!string.Equals(StringProperty(topology, "revision"), expectedRevision, StringComparison.OrdinalIgnoreCase) ||
            !IsSHA256(StringProperty(Property(topology, "completion"), "draftIdentity")) ||
            string.IsNullOrWhiteSpace(StringProperty(topology, "draftState")))
            throw new InvalidDataException("The topology snapshot omitted its exact source revision or replayable draft identity.");
    }

    private static async Task<bool> OperationRejectedWithCodeAsync(HttpClient client, string method,
        object parameters, string expectedCode, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("api/operations",
            new { method, parameters }, JsonOptions, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new HttpRequestException($"POST api/operations ({method}) returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        using JsonDocument submission = JsonDocument.Parse(body);
        string id = StringProperty(submission.RootElement, "id");
        long cursor = 0;
        while (true)
        {
            using JsonDocument poll = await GetJsonAsync(client,
                $"api/operations/{Uri.EscapeDataString(id)}?after={cursor}", cancellation);
            JsonElement root = poll.RootElement;
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            if (BooleanProperty(root, "hasMore")) continue;
            string state = StringProperty(root, "state");
            if (IsTerminal(state))
            {
                JsonElement error = PropertyOrNull(root, "error");
                return state.Equals("failed", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(OptionalString(error, "code"), expectedCode, StringComparison.OrdinalIgnoreCase);
            }
            await Task.Delay(100, cancellation);
        }
    }

    private static bool PathIsInside(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool SameFixturePath(string left, string right)
    {
        string normalizedLeft = Path.GetFullPath(left);
        string normalizedRight = Path.GetFullPath(right);
        return string.Equals(normalizedLeft, normalizedRight,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsSHA256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string TextSHA256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static double TopologyMapNumberProperty(JsonElement value, string name) =>
        Property(value, name).GetDouble();

    private static object OperationEntry(string method, OperationReceipt operation) => new
    {
        method,
        id = operation.Id,
        state = operation.State,
    };

    private sealed record MapGeometryOwner(string MapID, int MapIndex, string MapName, string MapType,
        string VisualizationRevision, CatalogTreeNodeLocator GeometryNode,
        CatalogTreeNodeLocator[] PlanNodes, object MapRequest);

    private sealed record TopologyMapFixturePreparation(string GeometryPath, string InitialSHA256,
        string PreparedSHA256, string PlanName, string GeometryName);
}
