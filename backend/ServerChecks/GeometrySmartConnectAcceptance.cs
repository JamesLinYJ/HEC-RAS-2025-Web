using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ras.Backend;

namespace Ras.Backend.ServerChecks;

internal sealed record GeometrySmartConnectRejectedRequest(
    string Action, string State, string? Code, string Message);

internal sealed record GeometrySmartConnectTopologyStageDiagnostics(
    string Stage,
    string[] BeforeDerivedBoundaryFeatureSignatures,
    string[] AfterDerivedBoundaryFeatureSignatures,
    bool DerivedBoundaryStateChanged,
    bool? BeforeCompletionIsComplete,
    GeometryTopologyCompletionMessage[] BeforeCompletionMessages,
    bool? AfterCompletionIsComplete,
    GeometryTopologyCompletionMessage[] AfterCompletionMessages);

internal sealed record GeometrySmartConnectAcceptanceReport(
    string State,
    string ManifestPath,
    string SourceManifestPath,
    string SourceProjectPath,
    string SourceGeometryPath,
    string ProjectPath,
    string GeometryPath,
    string MapID,
    string MapName,
    string InitialContextToken,
    string ReopenedContextToken,
    string SourceRevision,
    string SavedRevision,
    GeometryTopologySmartAddTerrainCapture TerrainCapture,
    GeometryTopologySmartAddWindow RasterWindow,
    GeometryTopologySmartAddOptions SmartAddOptions,
    int SmartAddArcFID,
    GeometryTopologyCoordinate[] SmartAddPath,
    int DeepConnectArcFID,
    int DeepConnectSourceContinuationFID,
    string DeepConnectSourceLocator,
    int StartSegmentIndex,
    double StartSegmentFraction,
    int EndSegmentIndex,
    double EndSegmentFraction,
    GeometryTopologyCoordinate[] DeepConnectPath,
    object[] Operations,
    bool SavedMapCatalogReadback,
    bool ExactGeometryMapContextAndSourceReadback,
    bool AssociatedTerrainAndRasterWindowReadback,
    bool StaleTerrainCaptureRejected,
    bool SmartAddDraftShapeReadback,
    bool InvalidDeepConnectAnchorRejected,
    bool DeepConnectInternalStartArcTargetReadback,
    bool DeepConnectAnchorsAndDraftShapeReadback,
    bool OriginalSourceRowsAndAttributesPreserved,
    bool DraftUndoReadback,
    bool DraftRedoReadback,
    bool SourceHdfUnchangedBeforeSave,
    bool ParentSaveChangedHdfRevision,
    bool ReopenedSmartAddAndDeepConnectReadback,
    bool OriginalFixtureUnchanged,
    GeometrySmartConnectTopologyStageDiagnostics[] TopologyStageDiagnostics,
    GeometrySmartConnectRejectedRequest[] RejectedRequests);

/// <summary>
/// Exercises actual Geometry SmartAdd and DeepConnect map commands through the
/// loopback Host. Source terrain sampling, map context, topology draft, and the
/// final parent save are all read through their production HTTP operations.
/// </summary>
internal static partial class ServerAcceptance
{
    public static async Task<GeometrySmartConnectAcceptanceReport> RunGeometrySmartConnectAsync(
        Uri baseAddress, string arcManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Geometry SmartConnect acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string sourceManifestPath = Path.GetFullPath(arcManifestPath);
        if (!File.Exists(sourceManifestPath))
            throw new FileNotFoundException("The accepted source Geometry arc-map manifest was not found.", sourceManifestPath);
        using JsonDocument sourceManifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(sourceManifestPath));
        JsonElement sourceManifest = sourceManifestDocument.RootElement;
        string sourceProjectPath = Path.GetFullPath(ArcMapManifestString(sourceManifest, "projectPath", "ProjectPath"));
        string sourceGeometryPath = Path.GetFullPath(ArcMapManifestString(sourceManifest, "geometryPath", "GeometryPath"));
        if (!File.Exists(sourceProjectPath) || !Path.GetExtension(sourceProjectPath).Equals(".ras", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The arc-map manifest must identify its saved .ras project.", sourceProjectPath);
        if (!File.Exists(sourceGeometryPath) || !Path.GetExtension(sourceGeometryPath).Equals(".h5", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The arc-map manifest must identify its saved Geometry HDF.", sourceGeometryPath);

        string sourceDirectory = Path.GetDirectoryName(sourceProjectPath)
            ?? throw new IOException("The accepted source project has no containing directory.");
        string sourceVisualizationPath = Path.ChangeExtension(sourceProjectPath, ".vis");
        if (!File.Exists(sourceVisualizationPath) || !ArcMapPathIsInside(sourceGeometryPath, sourceDirectory))
            throw new InvalidDataException("The accepted source must contain its saved map catalog and Geometry HDF.");

        string sourceProjectSHA = FileSHA256(sourceProjectPath);
        string sourceVisualizationSHA = FileSHA256(sourceVisualizationPath);
        string sourceGeometrySHA = FileSHA256(sourceGeometryPath);
        SortedDictionary<string, string> sourceFixtureSnapshot = ArcMapFixtureSnapshot(sourceDirectory);
        string? acceptedGeometrySHA = ArcMapOptionalString(sourceManifest, "savedRevision")
            ?? ArcMapOptionalString(sourceManifest, "savedGeometrySHA256");
        if (acceptedGeometrySHA is not null &&
            !acceptedGeometrySHA.Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        string? acceptedVisualizationSHA = ArcMapOptionalString(sourceManifest, "visualizationRevision");
        if (acceptedVisualizationSHA is not null &&
            !acceptedVisualizationSHA.Equals(sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Geometry SmartConnect acceptance requires a new output directory; existing data is never overwritten.");
        if (ArcMapPathsOverlap(outputDirectory, sourceDirectory))
            throw new IOException("The isolated SmartConnect output must not overlap the accepted source fixture.");
        Directory.CreateDirectory(outputDirectory);

        string fixtureDirectory = Path.Combine(outputDirectory, "fixture");
        string projectPath = CopyProjectDirectory(sourceDirectory, sourceProjectPath, fixtureDirectory);
        string geometryPath = Path.GetFullPath(Path.Combine(fixtureDirectory,
            Path.GetRelativePath(sourceDirectory, sourceGeometryPath)));
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!File.Exists(geometryPath) || !File.Exists(visualizationPath) ||
            !FileSHA256(projectPath).Equals(sourceProjectSHA, StringComparison.OrdinalIgnoreCase) ||
            !FileSHA256(geometryPath).Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase) ||
            !FileSHA256(visualizationPath).Equals(sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The copied project, Geometry HDF, or .vis bytes differ from the accepted fixture.");

        using var timeout = new CancellationTokenSource(GeometryArcMapTimeout);
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        OperationReceipt opened = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        string projectRevision = StringProperty(opened.Result, "revision");
        string projectXml = StringProperty(opened.Result, "projectXml");
        string projectXmlSHA = TextSHA256(projectXml);
        if (!IsSHA256(projectRevision) ||
            !projectRevision.Equals(FileSHA256(projectPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("project.open did not bind the copied .ras bytes to its revision.");
        JsonElement coreContextElement = PropertyOrNull(opened.Result, "coreLayerContext");
        CoreLayerRequestContext? coreLayerContext = coreContextElement.ValueKind == JsonValueKind.Object
            ? coreContextElement.Deserialize<CoreLayerRequestContext>(JsonOptions)
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
        string visualizationRevision = StringProperty(mapList.Result, "visualizationRevision");
        if (!IsSHA256(visualizationRevision) ||
            !visualizationRevision.Equals(FileSHA256(visualizationPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved map catalog revision does not match the copied .vis bytes.");

        var owners = new List<MapGeometryOwner>();
        foreach (JsonElement map in Property(mapList.Result, "maps").EnumerateArray())
        {
            int mapIndex = IntProperty(map, "mapIndex");
            string mapName = StringProperty(map, "mapName");
            string mapType = StringProperty(map, "mapType");
            if (!string.Equals(mapType, "Plan", StringComparison.Ordinal)) continue;
            string mapID = Guid.NewGuid().ToString("D");
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
            MapTreeSnapshot tree = JsonSerializer.Deserialize<MapTreeSnapshot>(treeOperation.Result.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("The copied saved map tree response was empty.");
            if (!tree.VisualizationRevision.Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            MapGeometryOwner? owner = FindGeometryOwner(mapID, mapIndex, mapName, mapType,
                visualizationRevision, tree.Roots, mapRequest, TopologyMapFixturePlanName,
                TopologyMapFixtureGeometryName, geometryPath);
            if (owner is not null) owners.Add(owner);
        }
        if (owners.Count != 1)
            throw new InvalidDataException("The saved map catalog must resolve exactly one Plan owner for Solver Geometry.h5.");
        MapGeometryOwner mapOwner = owners[0];

        OperationReceipt mapGeometryOperation = await SubmitOperationAsync(client, "map.geometry.load", new
        {
            map = mapOwner.MapRequest,
            geometryNode = mapOwner.GeometryNode,
            pageSize = 512,
            skipFlowAreasPage = true,
            skipCulvertsPage = true,
            planNodes = mapOwner.PlanNodes,
        }, cancellation);
        JsonElement mapGeometry = mapGeometryOperation.Result;
        string geometryRevision = StringProperty(mapGeometry, "geometryRevision");
        string contextToken = StringProperty(mapGeometry, "contextToken");
        JsonElement topologyPage = Property(mapGeometry, "topology");
        if (!PathEquals(StringProperty(mapGeometry, "geometryCanonicalPath"), geometryPath) ||
            !geometryRevision.Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase) ||
            !IsSHA256(contextToken) || StringProperty(Property(mapGeometry, "topologyState"), "status") != "available" ||
            !BooleanProperty(topologyPage, "complete") || IntProperty(topologyPage, "omittedSourceFeatureCount") != 0)
            throw new InvalidDataException("map.geometry.load did not return the exact complete Geometry source and map context.");

        var geometryModel = new GeometryTopologyModelRequest(geometryPath, "Geometry", projectPath,
            projectXml, coreLayerContext);
        OperationReceipt modelLoad = await SubmitOperationAsync(client, "model.load", geometryModel, cancellation);
        if (!StringProperty(modelLoad.Result, "revision").Equals(geometryRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        OperationReceipt topologyLoad = await SubmitOperationAsync(client, "topology.load",
            new GeometryTopologyLoadRequest(geometryModel, geometryRevision), cancellation);
        GeometryTopologySnapshot initial = ArcMapReadTopology(topologyLoad.Result);
        RequireTopologyOwner(topologyLoad.Result, geometryRevision);
        if (!initial.CanEdit || initial.Map?.Extent is not GeometryTopologyExtent extent || initial.Completion is null ||
            initial.Map.Features.Count(feature => feature.FeatureKind == "arc" && feature.FID is not null) < 4)
            throw new InvalidDataException("The accepted Geometry source must expose its editable saved topology and source arcs.");
        GeometryTopologySmartAddTerrainCapture terrainCapture = initial.SmartAddTerrainCapture
            ?? throw new InvalidDataException("The Geometry topology snapshot omitted its real associated Terrain capture.");
        if (!PathEquals(terrainCapture.GeometryAssetPath, geometryPath) ||
            !terrainCapture.GeometryRevision.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) ||
            !PathEquals(terrainCapture.ProjectPath, projectPath) ||
            string.IsNullOrWhiteSpace(terrainCapture.TerrainName) ||
            !IsSHA256(terrainCapture.SourceRevision) || !File.Exists(terrainCapture.CanonicalTerrainPath) ||
            !ArcMapPathIsInside(terrainCapture.CanonicalTerrainPath, fixtureDirectory))
            throw new InvalidDataException("SmartAdd did not capture the copied project's exact associated Terrain and its dependencies.");

        GeometryTopologyMapFeature[] initialMapFeatures = (initial.Map?.Features ?? []).Where(feature => feature.FID is not null).ToArray();
        GeometryTopologyMapFeature[] mapGeometryFeatures = JsonSerializer.Deserialize<GeometryTopologyMapFeature[]>(
            Property(topologyPage, "features").GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("map.geometry.load omitted its source Geometry features.");
        if (!ArcMapMapFeaturesEqual(initialMapFeatures, mapGeometryFeatures))
            throw new InvalidDataException("map.geometry.load and topology.load disagree on source FIDs or exact coordinates.");

        GeometryTopologyFeatureRow[] originalArcRows = initial.Arcs.Rows.ToArray();
        GeometryTopologyFeatureRow[] originalNodeRows = initial.Nodes.Rows.ToArray();
        GeometryTopologyFeatureRow[] originalRegionRows = initial.Regions.Rows.ToArray();
        HashSet<int> originalArcFIDs = originalArcRows.Select(row => row.FID).ToHashSet();
        double mapCellSize = MapCellSize(topologyLoad.Result.GetProperty("map"));
        GeometryTopologySmartAddWindow rasterWindow = new(extent, 512, 512);
        var smartAddOptions = new GeometryTopologySmartAddOptions(
            Algorithm: "HighGround", DpTolerancePixels: 0d, MaxSegmentPixels: 1000d,
            HighGroundSnapRadiusCells: 0);

        GeometryTopologyFeatureRow[] candidateStartNodes = initial.Nodes.Rows
            .Where(row => row.X is double x && double.IsFinite(x) && row.Y is double y && double.IsFinite(y))
            .ToArray();
        if (candidateStartNodes.Length == 0)
            throw new InvalidDataException("The accepted Geometry topology has no stable source node for SmartAdd.");
        GeometryTopologyFeatureRow startNode = candidateStartNodes.OrderBy(row => row.FID).First();
        var start = new GeometryTopologyCoordinate(startNode.X!.Value, startNode.Y!.Value);
        double width = extent.MaxX - extent.MinX;
        double height = extent.MaxY - extent.MinY;
        GeometryTopologyCoordinate[] targetCandidates =
        [
            new(extent.MinX + width * 0.79d, extent.MinY + height * 0.31d),
            new(extent.MinX + width * 0.23d, extent.MinY + height * 0.81d),
            new(extent.MinX + width * 0.77d, extent.MinY + height * 0.73d),
            new(extent.MinX + width * 0.21d, extent.MinY + height * 0.27d),
        ];
        GeometryTopologyCoordinate target = targetCandidates.OrderByDescending(point =>
            Math.Pow(point.X - start.X, 2d) + Math.Pow(point.Y - start.Y, 2d)).First();
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0d || height <= 0d ||
            Math.Sqrt(Math.Pow(target.X - start.X, 2d) + Math.Pow(target.Y - start.Y, 2d)) < mapCellSize * 12d)
            throw new InvalidDataException("The source map extent cannot provide a finite measurable SmartAdd segment.");

        GeometryTopologySnapshot current = initial;
        var operations = new List<object>();
        var topologyStageDiagnostics = new List<GeometrySmartConnectTopologyStageDiagnostics>();
        var rejected = new List<GeometrySmartConnectRejectedRequest>();

        async Task<GeometryTopologySnapshot> ApplySmartConnectCommandAsync(string action,
            Func<GeometryTopologyMapRequestContext, GeometryTopologyMapCommand> commandFactory)
        {
            string draftIdentity = current.Completion?.DraftIdentity
                ?? throw new InvalidDataException("The current Geometry topology draft has no identity.");
            string requestID = Guid.NewGuid().ToString("D");
            var mapRequestContext = new GeometryTopologyMapRequestContext(requestID, mapOwner.MapID,
                contextToken, geometryRevision, draftIdentity);
            GeometryTopologyMapCommand command = commandFactory(mapRequestContext);
            var mutation = new GeometryTopologyMutation("mapCommand", "map", []) { MapCommand = command };
            OperationReceipt receipt = await SubmitOperationAsync(client, "topology.edit",
                new GeometryTopologyEditRequest(geometryModel, current.Revision, current.DraftState,
                    mutation, mapRequestContext), cancellation);
            GeometryTopologySnapshot next = ArcMapReadTopology(receipt.Result);
            GeometryTopologyMapRequestContext echoed = next.MapRequestContext
                ?? throw new InvalidDataException(action + " response omitted its map-owner context.");
            GeometryTopologyCompletionResult completion = next.Completion
                ?? throw new InvalidDataException(action + " response omitted its topology completion identity.");
            if (!next.Revision.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) ||
                !completion.SourceRevision.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) ||
                !IsSHA256(completion.DraftIdentity) || echoed.RequestID != requestID ||
                echoed.MapID != mapOwner.MapID || echoed.ContextToken != contextToken ||
                echoed.GeometryRevision != geometryRevision || echoed.ExpectedDraftIdentity != draftIdentity)
                throw new InvalidDataException(action + " response lost its exact source, map, or prior draft identity.");
            current = next;
            operations.Add(new { action, id = receipt.Id, state = receipt.State, draftIdentity = completion.DraftIdentity });
            return next;
        }

        async Task<GeometrySmartConnectRejectedRequest> ExpectSmartConnectRejectedAsync(string action,
            Func<GeometryTopologyMapRequestContext, GeometryTopologyMapCommand> commandFactory)
        {
            string draftIdentity = current.Completion?.DraftIdentity
                ?? throw new InvalidDataException("The current Geometry topology draft has no identity.");
            string requestID = Guid.NewGuid().ToString("D");
            var mapRequestContext = new GeometryTopologyMapRequestContext(requestID, mapOwner.MapID,
                contextToken, geometryRevision, draftIdentity);
            var mutation = new GeometryTopologyMutation("mapCommand", "map", [])
            {
                MapCommand = commandFactory(mapRequestContext),
            };
            var request = new GeometryTopologyEditRequest(geometryModel, current.Revision,
                current.DraftState, mutation, mapRequestContext);
            using HttpResponseMessage response = await client.PostAsJsonAsync("api/operations",
                new { method = "topology.edit", parameters = request }, JsonOptions, cancellation);
            string body = await response.Content.ReadAsStringAsync(cancellation);
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                if ((int)response.StatusCode is >= 400 and < 500)
                {
                    var directlyRejected = new GeometrySmartConnectRejectedRequest(action,
                        "rejected", response.StatusCode.ToString(), body);
                    rejected.Add(directlyRejected);
                    return directlyRejected;
                }
                throw new HttpRequestException($"POST api/operations (topology.edit/{action}) returned {(int)response.StatusCode}: {body}");
            }
            using JsonDocument submission = JsonDocument.Parse(body);
            string operationID = StringProperty(submission.RootElement, "id");
            long cursor = 0;
            while (true)
            {
                using JsonDocument poll = await GetJsonAsync(client,
                    $"api/operations/{Uri.EscapeDataString(operationID)}?after={cursor}", cancellation);
                JsonElement root = poll.RootElement;
                cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
                if (BooleanProperty(root, "hasMore")) continue;
                string state = StringProperty(root, "state");
                if (IsTerminal(state))
                {
                    if (!state.Equals("failed", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Invalid {action} input was accepted by the Host: state={state}.");
                    JsonElement error = PropertyOrNull(root, "error");
                    string? code = OptionalString(error, "code");
                    string message = OptionalString(error, "message") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(message))
                        throw new InvalidDataException($"Rejected {action} response omitted its Host error message.");
                    var failed = new GeometrySmartConnectRejectedRequest(action, state, code, message);
                    rejected.Add(failed);
                    operations.Add(new { action = "reject:" + action, id = operationID, state, code });
                    return failed;
                }
                await Task.Delay(100, cancellation);
            }
        }

        string differentSourceRevision = terrainCapture.SourceRevision.All(character => character == '0')
            ? new string('1', 64) : new string('0', 64);
        GeometrySmartConnectRejectedRequest staleTerrainCapture = await ExpectSmartConnectRejectedAsync(
            "smartAdd.staleTerrainCapture", _ => new GeometryTopologyMapCommand("smartAdd", "arc", [],
                Coordinates: [start, target], MapCellSize: mapCellSize,
                SmartAddSource: new GeometryTopologySmartAddSource(
                    terrainCapture with { SourceRevision = differentSourceRevision }, rasterWindow, smartAddOptions))
            { StartNodeFID = startNode.FID });
        bool staleTerrainCaptureRejected = staleTerrainCapture.State.Equals("rejected", StringComparison.OrdinalIgnoreCase) ||
            staleTerrainCapture.State.Equals("failed", StringComparison.OrdinalIgnoreCase);
        GeometryTopologySnapshot afterStaleCapture = current;
        if (afterStaleCapture.Completion?.DraftIdentity != initial.Completion?.DraftIdentity)
            throw new InvalidDataException("A stale Terrain capture unexpectedly changed the current Geometry draft.");

        GeometryTopologySnapshot smartAdded = await ApplySmartConnectCommandAsync("smartAdd", _ =>
            new GeometryTopologyMapCommand("smartAdd", "arc", [], Coordinates: [start, target],
                MapCellSize: mapCellSize,
                SmartAddSource: new GeometryTopologySmartAddSource(terrainCapture, rasterWindow, smartAddOptions))
            { StartNodeFID = startNode.FID });
        int[] smartAddCreatedIDs = smartAdded.Arcs.Rows.Where(row => !originalArcFIDs.Contains(row.FID))
            .Select(row => row.FID).ToArray();
        if (smartAddCreatedIDs.Length != 1)
            throw new InvalidDataException("A real SmartAdd terrain sample did not append exactly one new Geometry arc.");
        int smartAddArcFID = smartAddCreatedIDs[0];
        GeometryTopologyMapFeature smartAddFeature = ArcMapFeatures(smartAdded, "arc")
            .SingleOrDefault(feature => feature.FID == smartAddArcFID)
            ?? throw new InvalidDataException("SmartAdd did not return the newly sampled source arc in its map geometry.");
        GeometryTopologyCoordinate[] smartAddPath = ArcMapPath(smartAddFeature);
        bool smartAddArcRowsPreserved = ArcMapBaselineRowsPreserved(originalArcRows, smartAdded.Arcs.Rows);
        bool smartAddNodeRowsPreserved = ArcMapBaselineRowsPreserved(originalNodeRows, smartAdded.Nodes.Rows);
        bool smartAddRegionRowsPreserved = ArcMapBaselineRowsPreserved(originalRegionRows, smartAdded.Regions.Rows);
        bool smartAddMapFeaturesPreserved = SmartConnectSourceMapFeaturesPreserved(initialMapFeatures,
            smartAdded.Map?.Features ?? []);
        bool smartAddTopologySignatureChanged = ArcMapTopologySignature(smartAdded) != ArcMapTopologySignature(initial);
        topologyStageDiagnostics.Add(SmartConnectTopologyStageDiagnostics("smartAdd", initial, smartAdded));
        bool smartAddDraftShapeReadback = smartAdded.IsDirty && smartAdded.CanUndo &&
            smartAddTopologySignatureChanged && smartAddPath.Length >= 2 &&
            smartAddArcRowsPreserved && smartAddNodeRowsPreserved && smartAddRegionRowsPreserved &&
            smartAddMapFeaturesPreserved;
        if (!smartAddDraftShapeReadback)
        {
            string diagnostics = JsonSerializer.Serialize(new
            {
                operation = "smartAdd",
                state = new { smartAdded.IsDirty, smartAdded.CanUndo, smartAdded.CanRedo },
                geometryRevision,
                initialDraftIdentity = initial.Completion?.DraftIdentity,
                resultingDraftIdentity = smartAdded.Completion?.DraftIdentity,
                resultingDraftStateSHA256 = TextSHA256(smartAdded.DraftState),
                operations = operations.ToArray(),
                rejectedRequests = rejected.ToArray(),
                map = new { mapOwner.MapID, mapOwner.MapIndex, mapOwner.MapName, mapCellSize },
                startNodeFID = startNode.FID,
                start,
                target,
                rasterWindow,
                smartAddArcFID,
                smartAddPath,
                createdArcIDs = smartAddCreatedIDs,
                topologySignatureChanged = smartAddTopologySignatureChanged,
                preserved = new
                {
                    arcRows = smartAddArcRowsPreserved,
                    nodeRows = smartAddNodeRowsPreserved,
                    regionRows = smartAddRegionRowsPreserved,
                    mapFeatures = smartAddMapFeaturesPreserved,
                },
                rowDiffs = new
                {
                    arcs = SmartConnectRowDiffs(originalArcRows, smartAdded.Arcs.Rows),
                    nodes = SmartConnectRowDiffs(originalNodeRows, smartAdded.Nodes.Rows),
                    regions = SmartConnectRowDiffs(originalRegionRows, smartAdded.Regions.Rows),
                },
                mapFeatureDiffs = SmartConnectMapFeatureDiffs(initialMapFeatures,
                    smartAdded.Map?.Features ?? []),
                derivedBoundaryAndCompletion = topologyStageDiagnostics[^1],
            }, JsonOptions);
            throw new InvalidDataException("SmartAdd readback failed; the detailed draft, source-row, and map-feature differences follow: "
                + diagnostics);
        }
        string afterSmartAddSignature = ArcMapTopologySignature(smartAdded);

        GeometryTopologyMapFeature[] deepConnectSourceCandidates = ArcMapFeatures(current, "arc")
            .Where(feature => feature.FID is int fid && originalArcFIDs.Contains(fid))
            .OrderByDescending(feature => ArcMapPath(feature).Length)
            .ThenBy(feature => feature.FID)
            .ToArray();
        GeometryTopologyMapFeature? deepConnectFeature = null;
        GeometryTopologyCoordinate[]? deepConnectPoints = null;
        int? deepConnectStartNodeFID = null;
        foreach (GeometryTopologyMapFeature candidate in deepConnectSourceCandidates)
        {
            GeometryTopologyCoordinate[] candidatePoints = ArcMapPath(candidate);
            int? candidateNodeFID = SmartConnectExactEndpointNodeFID(current, candidatePoints[0]);
            if (candidateNodeFID is null) continue;
            deepConnectFeature = candidate;
            deepConnectPoints = candidatePoints;
            deepConnectStartNodeFID = candidateNodeFID;
            break;
        }
        if (deepConnectFeature is null || deepConnectPoints is null || deepConnectStartNodeFID is null)
            throw new InvalidDataException("The accepted Geometry topology has no original arc whose first endpoint maps to one exact source node.");
        GeometryTopologyCoordinate[] deepConnectSourcePoints = deepConnectPoints;
        int exactDeepConnectStartNodeFID = deepConnectStartNodeFID.Value;
        int deepConnectSourceFID = deepConnectFeature.FID!.Value;
        int startSegmentIndex = 0;
        int endSegmentIndex = deepConnectSourcePoints.Length - 2;
        const double startSegmentFraction = 0d;
        const double endSegmentFraction = 0.75d;
        GeometryTopologyCoordinate expectedDeepStart = SmartConnectInterpolate(deepConnectSourcePoints,
            startSegmentIndex, startSegmentFraction);
        GeometryTopologyCoordinate expectedDeepEnd = SmartConnectInterpolate(deepConnectSourcePoints,
            endSegmentIndex, endSegmentFraction);
        int internalStartSegmentIndex = -1;
        for (int segmentIndex = 0; segmentIndex < deepConnectSourcePoints.Length - 1; segmentIndex++)
        {
            if (deepConnectSourcePoints[segmentIndex].X != deepConnectSourcePoints[segmentIndex + 1].X ||
                deepConnectSourcePoints[segmentIndex].Y != deepConnectSourcePoints[segmentIndex + 1].Y)
            {
                internalStartSegmentIndex = segmentIndex;
                break;
            }
        }
        if (internalStartSegmentIndex < 0)
            throw new InvalidDataException("The captured DeepConnect source arc has no nonzero segment for an internal map anchor.");
        const double internalStartSegmentFraction = 0.5d;
        GeometryTopologyCoordinate expectedInternalDeepStart = SmartConnectInterpolate(deepConnectSourcePoints,
            internalStartSegmentIndex, internalStartSegmentFraction);

        GeometryTopologyMapCommand DeepConnectCommand(GeometryTopologyDeepConnectSource source) => new("deepConnect", "arc", [],
            MapCellSize: mapCellSize, DeepConnectSource: source)
        { StartNodeFID = exactDeepConnectStartNodeFID };

        GeometryTopologyMapCommand InternalStartArcTargetCommand(GeometryTopologyDeepConnectSource source) =>
            new("deepConnect", "arc", [], MapCellSize: mapCellSize, DeepConnectSource: source)
            {
                StartArcTarget = new GeometryTopologyArcSnapTarget(deepConnectSourceFID,
                    internalStartSegmentIndex, expectedInternalDeepStart),
                SplitMode = "Anywhere",
            };

        GeometryTopologyDeepConnectSource DeepConnectSource(GeometryTopologyMapRequestContext mapRequestContext,
            int sourceStartSegmentIndex, double sourceStartSegmentFraction) =>
            new("topology", "arc:" + deepConnectSourceFID.ToString(CultureInfo.InvariantCulture),
                geometryRevision, deepConnectSourceFID, 0,
                "arc:" + deepConnectSourceFID.ToString(CultureInfo.InvariantCulture) + ":path:0",
                deepConnectSourcePoints, sourceStartSegmentIndex, sourceStartSegmentFraction,
                endSegmentIndex, endSegmentFraction,
                TopologySource: new GeometryTopologyDeepConnectTopologySource(geometryModel,
                    current.Revision, current.DraftState, current.Completion!.DraftIdentity,
                    mapRequestContext, "arc", "arc:" + deepConnectSourceFID.ToString(CultureInfo.InvariantCulture)));

        GeometrySmartConnectRejectedRequest invalidDeepConnectAnchor = await ExpectSmartConnectRejectedAsync(
            "deepConnect.invalidAnchor", mapRequestContext => DeepConnectCommand(
                DeepConnectSource(mapRequestContext, startSegmentIndex, startSegmentFraction)
                    with { EndSegmentIndex = deepConnectSourcePoints.Length - 1 }));
        bool invalidDeepConnectAnchorRejected = invalidDeepConnectAnchor.State.Equals("rejected", StringComparison.OrdinalIgnoreCase) ||
            invalidDeepConnectAnchor.State.Equals("failed", StringComparison.OrdinalIgnoreCase);
        if (current.Completion?.DraftIdentity != smartAdded.Completion?.DraftIdentity ||
            ArcMapTopologySignature(current) != afterSmartAddSignature)
            throw new InvalidDataException("An invalid DeepConnect anchor unexpectedly changed the SmartAdd draft.");

        GeometryTopologySnapshot internalDeepConnected = await ApplySmartConnectCommandAsync(
            "deepConnect.internalStartArcTarget", mapRequestContext => InternalStartArcTargetCommand(
                DeepConnectSource(mapRequestContext, internalStartSegmentIndex, internalStartSegmentFraction)));
        topologyStageDiagnostics.Add(SmartConnectTopologyStageDiagnostics(
            "deepConnect.internalStartArcTarget", smartAdded, internalDeepConnected));
        int[] internalStartNewArcIDs = internalDeepConnected.Arcs.Rows
            .Where(row => !smartAdded.Arcs.Rows.Any(prior => prior.FID == row.FID))
            .Select(row => row.FID).Order().ToArray();
        GeometryTopologyMapFeature[] internalStartNewArcFeatures = ArcMapFeatures(internalDeepConnected, "arc")
            .Where(feature => feature.FID is int fid && internalStartNewArcIDs.Contains(fid)).ToArray();
        int[] priorArcFIDs = smartAdded.Arcs.Rows.Select(row => row.FID).Order().ToArray();
        bool priorArcFIDsDense = priorArcFIDs.SequenceEqual(Enumerable.Range(0, priorArcFIDs.Length));
        int[] expectedAppendedArcFIDs = priorArcFIDsDense
            ? Enumerable.Range(priorArcFIDs.Length, 3).ToArray()
            : [];
        bool internalArcFIDsFollowHostOperationOrder = priorArcFIDsDense &&
            internalStartNewArcIDs.Length == 3 &&
            internalStartNewArcIDs.SequenceEqual(expectedAppendedArcFIDs);
        int? internalStartContinuationFID = internalArcFIDsFollowHostOperationOrder
            ? expectedAppendedArcFIDs[0] : null;
        int? internalConnectionFID = internalArcFIDsFollowHostOperationOrder
            ? expectedAppendedArcFIDs[1] : null;
        int? internalEndContinuationFID = internalArcFIDsFollowHostOperationOrder
            ? expectedAppendedArcFIDs[2] : null;
        GeometryTopologyMapFeature[] internalConnectionFeatures = internalConnectionFID is int connectionFID
            ? internalStartNewArcFeatures.Where(feature => feature.FID == connectionFID).ToArray()
            : [];
        GeometryTopologyMapFeature? internalConnectionFeature = internalConnectionFeatures.SingleOrDefault();
        GeometryTopologyCoordinate[] internalConnectionPath = internalConnectionFeature is null
            ? [] : ArcMapPath(internalConnectionFeature);
        bool internalConnectionJoinsCapturedAnchors = internalConnectionPath.Length >= 2 &&
            SmartConnectNear(internalConnectionPath[0], expectedInternalDeepStart) &&
            SmartConnectNear(internalConnectionPath[^1], expectedDeepEnd);
        int[] internalSplitArcIDs = internalArcFIDsFollowHostOperationOrder
            ? [internalStartContinuationFID!.Value, internalEndContinuationFID!.Value]
            : [];
        GeometryTopologyFeatureRow internalSplitSourceRow = smartAdded.Arcs.Rows
            .Single(row => row.FID == deepConnectSourceFID);
        GeometryTopologyFeatureRow[] internalSplitPieceRows = internalDeepConnected.Arcs.Rows
            .Where(row => row.FID == deepConnectSourceFID || internalSplitArcIDs.Contains(row.FID))
            .ToArray();
        GeometryTopologyMapFeature[] internalSplitPieces = ArcMapFeatures(internalDeepConnected, "arc")
            .Where(feature => feature.FID == deepConnectSourceFID ||
                internalSplitArcIDs.Contains(feature.FID ?? -1)).ToArray();
        GeometryTopologyFeatureRow[] internalStartSplitNodeRows = internalDeepConnected.Nodes.Rows
            .Where(row => !smartAdded.Nodes.Rows.Any(prior => prior.FID == row.FID) &&
                row.X is double x && row.Y is double y &&
                SmartConnectNear(new GeometryTopologyCoordinate(x, y), expectedInternalDeepStart))
            .ToArray();
        GeometryTopologyMapFeature[] internalStartSplitNodeFeatures = internalStartSplitNodeRows.Length == 1
            ? ArcMapFeatures(internalDeepConnected, "node")
                .Where(feature => feature.FID == internalStartSplitNodeRows[0].FID).ToArray()
            : [];
        GeometryTopologyFeatureRow[] internalEndSplitNodeRows = internalDeepConnected.Nodes.Rows
            .Where(row => !smartAdded.Nodes.Rows.Any(prior => prior.FID == row.FID) &&
                row.X is double x && row.Y is double y &&
                SmartConnectNear(new GeometryTopologyCoordinate(x, y), expectedDeepEnd))
            .ToArray();
        GeometryTopologyMapFeature[] internalEndSplitNodeFeatures = internalEndSplitNodeRows.Length == 1
            ? ArcMapFeatures(internalDeepConnected, "node")
                .Where(feature => feature.FID == internalEndSplitNodeRows[0].FID).ToArray()
            : [];
        bool internalAnchorsOutsideExistingNodeHitWindow =
            SmartConnectAnchorOutsideNodeHitWindow(smartAdded.Nodes.Rows, expectedInternalDeepStart, mapCellSize) &&
            SmartConnectAnchorOutsideNodeHitWindow(smartAdded.Nodes.Rows, expectedDeepEnd, mapCellSize);
        bool internalSourceSplitPieceCount = internalArcFIDsFollowHostOperationOrder &&
            internalSplitPieces.Length == 3;
        bool internalSourceSplitPieceEndpointsAtBothAnchors = internalSourceSplitPieceCount &&
            internalSplitPieces.Any(feature => SmartConnectPathTouchesAnchor(feature, expectedInternalDeepStart)) &&
            internalSplitPieces.Any(feature => SmartConnectPathTouchesAnchor(feature, expectedDeepEnd));
        bool internalSourceSplitPathReconstructsOriginal = internalSourceSplitPieceCount &&
            SmartConnectSplitReconstructsOriginalPath(deepConnectSourcePoints,
                internalSplitPieces.Select(ArcMapPath).ToArray(), internalStartSegmentIndex,
                expectedInternalDeepStart, endSegmentIndex, expectedDeepEnd);
        bool internalSourceSplitAtCapturedPoints = internalSourceSplitPieceCount &&
            internalSourceSplitPieceEndpointsAtBothAnchors && internalSourceSplitPathReconstructsOriginal;
        bool internalStartSplitNodeReadback = internalStartSplitNodeRows.Length == 1 &&
            internalStartSplitNodeFeatures.Length == 1 && internalStartSplitNodeFeatures[0].Paths.Length == 1 &&
            internalStartSplitNodeFeatures[0].Paths[0].Length == 1 &&
            SmartConnectNear(internalStartSplitNodeFeatures[0].Paths[0][0], expectedInternalDeepStart);
        bool internalEndSplitNodeReadback = internalEndSplitNodeRows.Length == 1 &&
            internalEndSplitNodeFeatures.Length == 1 && internalEndSplitNodeFeatures[0].Paths.Length == 1 &&
            internalEndSplitNodeFeatures[0].Paths[0].Length == 1 &&
            SmartConnectNear(internalEndSplitNodeFeatures[0].Paths[0][0], expectedDeepEnd);
        bool internalSplitNodesReadback = internalStartSplitNodeReadback && internalEndSplitNodeReadback;
        bool internalSplitAttributesPreserved = internalSplitArcIDs.Length == 2 &&
            internalSplitPieceRows.Length == 3 &&
            internalSplitPieceRows.All(row =>
                ArcMapCopiedArcAttributesEqual(internalSplitSourceRow.Values, row.Values));
        bool internalUnrelatedArcRowsPreserved = ArcMapBaselineRowsPreserved(
            originalArcRows.Where(row => row.FID != deepConnectSourceFID), internalDeepConnected.Arcs.Rows);
        bool internalOriginalNodeRowsPreserved = ArcMapBaselineRowsPreserved(originalNodeRows,
            internalDeepConnected.Nodes.Rows);
        bool internalOriginalRegionRowsPreserved = ArcMapBaselineRowsPreserved(originalRegionRows,
            internalDeepConnected.Regions.Rows);
        bool internalUnrelatedMapFeaturesPreserved = SmartConnectSourceMapFeaturesPreserved(
            initialMapFeatures.Where(feature =>
                feature.FeatureKind != "arc" || feature.FID != deepConnectSourceFID),
            internalDeepConnected.Map?.Features ?? []);
        bool internalDraftHasUndoState = internalDeepConnected.IsDirty && internalDeepConnected.CanUndo;
        bool internalAnchoredConnectionReadback = internalConnectionFeatures.Length == 1 &&
            internalConnectionJoinsCapturedAnchors;
        bool internalSourceContinuationCount = internalSplitArcIDs.Length == 2;
        bool internalSourceSplitTableRowCount = internalSplitPieceRows.Length == 3;
        bool deepConnectInternalStartArcTargetReadback = internalDraftHasUndoState &&
            internalAnchorsOutsideExistingNodeHitWindow && internalArcFIDsFollowHostOperationOrder &&
            internalAnchoredConnectionReadback && internalSourceContinuationCount &&
            internalSourceSplitTableRowCount && internalSourceSplitAtCapturedPoints && internalSplitNodesReadback &&
            internalSplitAttributesPreserved && internalUnrelatedArcRowsPreserved &&
            internalOriginalNodeRowsPreserved && internalOriginalRegionRowsPreserved &&
            internalUnrelatedMapFeaturesPreserved;
        if (!deepConnectInternalStartArcTargetReadback)
        {
            object[] splitPieceReadback = internalSplitPieceRows.Select(row => new
            {
                row.ID,
                row.FID,
                row.StartNodeID,
                row.EndNodeID,
                copiedAttributesPreserved = ArcMapCopiedArcAttributesEqual(internalSplitSourceRow.Values, row.Values),
                copiedAttributeDifferences = SmartConnectCopiedArcAttributeDifferences(
                    internalSplitSourceRow.Values, row.Values),
                allValueDifferences = ArcMapValueDifferences(internalSplitSourceRow.Values, row.Values),
                row.Values,
            }).ToArray();
            GeometryTopologyMapFeature[] newlyCreatedNodeFeatures = ArcMapFeatures(internalDeepConnected, "node")
                .Where(feature => feature.FID is int fid &&
                    !smartAdded.Nodes.Rows.Any(row => row.FID == fid)).ToArray();
            object[] internalNewArcRowDiagnostics = internalStartNewArcIDs.Select(fid =>
            {
                GeometryTopologyFeatureRow row = internalDeepConnected.Arcs.Rows.Single(candidate => candidate.FID == fid);
                GeometryTopologyMapFeature? feature = internalStartNewArcFeatures.SingleOrDefault(candidate => candidate.FID == fid);
                string role = fid == internalStartContinuationFID ? "start-source-continuation" :
                    fid == internalConnectionFID ? "deep-connect-connection" :
                    fid == internalEndContinuationFID ? "end-source-continuation" : "unclassified";
                return (object)new
                {
                    role,
                    row.ID,
                    row.FID,
                    row.StartNodeID,
                    row.EndNodeID,
                    row.Values,
                    path = feature is null ? Array.Empty<GeometryTopologyCoordinate>() : ArcMapPath(feature),
                };
            }).ToArray();
            object diagnostics = new
            {
                operation = "deepConnect.internalStartArcTarget",
                state = new
                {
                    internalDeepConnected.IsDirty,
                    internalDeepConnected.CanUndo,
                    internalDeepConnected.CanRedo,
                    internalDeepConnected.Revision,
                    priorDraftIdentity = smartAdded.Completion?.DraftIdentity,
                    resultingDraftIdentity = internalDeepConnected.Completion?.DraftIdentity,
                    resultingDraftStateSHA256 = TextSHA256(internalDeepConnected.DraftState),
                },
                source = new
                {
                    featureFID = deepConnectSourceFID,
                    path = deepConnectSourcePoints,
                    startAnchor = expectedInternalDeepStart,
                    endAnchor = expectedDeepEnd,
                    startAnchorSegmentIndex = internalStartSegmentIndex,
                    startAnchorSegmentFraction = internalStartSegmentFraction,
                    endAnchorSegmentIndex = endSegmentIndex,
                    endAnchorSegmentFraction = endSegmentFraction,
                    hostSplitSemantics = "AddArc splits StartArcTarget before NewArcOperation. BaseTopology.AddArc appends the arc and Table.AddNewRow assigns row count minus one as FID. CompleteArcOperation then splits the end-hit source arc. With dense pre-edit FIDs and both anchors outside existing-node hit windows, the created arc FIDs therefore identify start continuation, connection, then end continuation by append order.",
                    anchorsOutsideExistingNodeHitWindow = internalAnchorsOutsideExistingNodeHitWindow,
                    preEditArcFIDs = priorArcFIDs,
                    preEditArcFIDsDense = priorArcFIDsDense,
                    expectedAppendedArcFIDs,
                    observedNewArcFIDs = internalStartNewArcIDs,
                    allocationOrderMatchesHostContract = internalArcFIDsFollowHostOperationOrder,
                    startContinuationFID = internalStartContinuationFID,
                    connectionFID = internalConnectionFID,
                    endContinuationFID = internalEndContinuationFID,
                },
                createdArcIDs = internalStartNewArcIDs,
                allCreatedArcRows = internalNewArcRowDiagnostics,
                connectionFeatures = internalConnectionFeatures.Select(feature => new
                {
                    feature.ID,
                    feature.FID,
                    path = ArcMapPath(feature),
                    joinsCapturedAnchors = internalConnectionJoinsCapturedAnchors,
                }),
                sourceContinuationIDs = internalSplitArcIDs,
                sourcePieces = internalSplitPieces.Select(feature => new
                {
                    feature.ID,
                    feature.FID,
                    path = ArcMapPath(feature),
                    touchesStartAnchor = SmartConnectNear(ArcMapPath(feature)[0], expectedInternalDeepStart) ||
                        SmartConnectNear(ArcMapPath(feature)[^1], expectedInternalDeepStart),
                    touchesEndAnchor = SmartConnectNear(ArcMapPath(feature)[0], expectedDeepEnd) ||
                        SmartConnectNear(ArcMapPath(feature)[^1], expectedDeepEnd),
                }),
                splitPieceRows = splitPieceReadback,
                splitNodes = new
                {
                    expectedStartAnchorRows = internalStartSplitNodeRows.Select(row => new
                    {
                        row.ID,
                        row.FID,
                        row.X,
                        row.Y,
                    }),
                    expectedEndAnchorRows = internalEndSplitNodeRows.Select(row => new
                    {
                        row.ID,
                        row.FID,
                        row.X,
                        row.Y,
                    }),
                    allNewNodeRows = internalDeepConnected.Nodes.Rows
                        .Where(row => !smartAdded.Nodes.Rows.Any(prior => prior.FID == row.FID))
                        .Select(row => new { row.ID, row.FID, row.X, row.Y }),
                    allNewNodeFeatures = newlyCreatedNodeFeatures.Select(feature => new
                    {
                        feature.ID,
                        feature.FID,
                        feature.Paths,
                        feature.IncidentArcs,
                    }),
                },
                predicates = new
                {
                    internalDraftHasUndoState,
                    priorArcFIDsDense,
                    internalAnchorsOutsideExistingNodeHitWindow,
                    internalArcFIDsFollowHostOperationOrder,
                    internalAnchoredConnectionReadback,
                    internalSourceContinuationCount,
                    internalSourceSplitTableRowCount,
                    internalSourceSplitPieceCount,
                    internalSourceSplitPieceEndpointsAtBothAnchors,
                    internalSourceSplitPathReconstructsOriginal,
                    internalStartSplitNodeReadback,
                    internalEndSplitNodeReadback,
                    internalSplitAttributesPreserved,
                    internalUnrelatedArcRowsPreserved,
                    internalOriginalNodeRowsPreserved,
                    internalOriginalRegionRowsPreserved,
                    internalUnrelatedMapFeaturesPreserved,
                    internalStartNewArcFeatureCount = internalStartNewArcFeatures.Length,
                    internalSplitFeatureCount = internalSplitPieces.Length,
                    internalSplitRowCount = internalSplitPieceRows.Length,
                    internalStartSplitNodeRowMatchCount = internalStartSplitNodeRows.Length,
                    internalStartSplitNodeFeatureMatchCount = internalStartSplitNodeFeatures.Length,
                    internalEndSplitNodeRowMatchCount = internalEndSplitNodeRows.Length,
                    internalEndSplitNodeFeatureMatchCount = internalEndSplitNodeFeatures.Length,
                },
                rowDiffs = new
                {
                    unrelatedArcs = SmartConnectRowDiffs(
                        originalArcRows.Where(row => row.FID != deepConnectSourceFID),
                        internalDeepConnected.Arcs.Rows),
                    nodes = SmartConnectRowDiffs(originalNodeRows, internalDeepConnected.Nodes.Rows),
                    regions = SmartConnectRowDiffs(originalRegionRows, internalDeepConnected.Regions.Rows),
                    sourceArcRows = SmartConnectRowDiffs([internalSplitSourceRow], internalSplitPieceRows),
                },
                mapFeatureDiffs = SmartConnectMapFeatureDiffs(
                    initialMapFeatures.Where(feature =>
                        feature.FeatureKind != "arc" || feature.FID != deepConnectSourceFID),
                    internalDeepConnected.Map?.Features ?? []),
                derivedBoundaryAndCompletion = topologyStageDiagnostics[^1],
                operations = operations.ToArray(),
            };
            throw new InvalidDataException("The UI-shaped internal StartArcTarget DeepConnect readback failed; per-predicate source split, anchor, node, attribute, and preservation diagnostics follow: " +
                JsonSerializer.Serialize(diagnostics, JsonOptions));
        }

        OperationReceipt undoInternalStart = await SubmitOperationAsync(client, "topology.edit",
            new GeometryTopologyEditRequest(geometryModel, internalDeepConnected.Revision,
                internalDeepConnected.DraftState,
                new GeometryTopologyMutation("undo", "history", [])), cancellation);
        GeometryTopologySnapshot restoredAfterInternalStart = ArcMapReadTopology(undoInternalStart.Result);
        operations.Add(new { action = "undoDeepConnectInternalStartArcTarget", id = undoInternalStart.Id, state = undoInternalStart.State });
        topologyStageDiagnostics.Add(SmartConnectTopologyStageDiagnostics(
            "undoDeepConnectInternalStartArcTarget", internalDeepConnected, restoredAfterInternalStart));
        if (ArcMapTopologySignature(restoredAfterInternalStart) != afterSmartAddSignature ||
            !restoredAfterInternalStart.CanUndo || !restoredAfterInternalStart.CanRedo)
            throw new InvalidDataException("Undo did not restore the exact SmartAdd state after the internal-start DeepConnect split.");
        current = restoredAfterInternalStart;

        GeometryTopologySnapshot deepConnected = await ApplySmartConnectCommandAsync("deepConnect", mapRequestContext =>
            DeepConnectCommand(DeepConnectSource(mapRequestContext, startSegmentIndex, startSegmentFraction)));
        topologyStageDiagnostics.Add(SmartConnectTopologyStageDiagnostics("deepConnect", restoredAfterInternalStart,
            deepConnected));
        int[] deepConnectPriorArcFIDs = restoredAfterInternalStart.Arcs.Rows
            .Select(row => row.FID).Order().ToArray();
        bool deepConnectPriorArcFIDsDense = deepConnectPriorArcFIDs
            .SequenceEqual(Enumerable.Range(0, deepConnectPriorArcFIDs.Length));
        int[] expectedDeepConnectAppendedFIDs = deepConnectPriorArcFIDsDense
            ? Enumerable.Range(deepConnectPriorArcFIDs.Length, 2).ToArray()
            : Array.Empty<int>();
        int[] deepConnectCreatedIDs = deepConnected.Arcs.Rows
            .Where(row => !restoredAfterInternalStart.Arcs.Rows.Any(prior => prior.FID == row.FID))
            .Select(row => row.FID).Order().ToArray();
        bool deepConnectAppendOrderMatchesHostContract = deepConnectPriorArcFIDsDense &&
            deepConnectCreatedIDs.Length == 2 &&
            deepConnectCreatedIDs.SequenceEqual(expectedDeepConnectAppendedFIDs);
        if (!deepConnectAppendOrderMatchesHostContract)
        {
            object diagnostics = new
            {
                priorArcFIDs = deepConnectPriorArcFIDs,
                priorArcFIDsDense = deepConnectPriorArcFIDsDense,
                expectedAppendedArcFIDs = expectedDeepConnectAppendedFIDs,
                observedNewArcFIDs = deepConnectCreatedIDs,
                newArcRows = deepConnectCreatedIDs.Select(fid =>
                {
                    GeometryTopologyFeatureRow row = deepConnected.Arcs.Rows.Single(candidate => candidate.FID == fid);
                    GeometryTopologyMapFeature? feature = ArcMapFeatures(deepConnected, "arc")
                        .SingleOrDefault(candidate => candidate.FID == fid);
                    return new
                    {
                        row.ID,
                        row.FID,
                        row.StartNodeID,
                        row.EndNodeID,
                        row.Values,
                        path = feature?.Paths,
                    };
                }).ToArray(),
                hostOrder = "With endpoint-start (no StartArcTarget), AddArc creates NewArcOperation first; CompleteArcOperation then splits the captured end-hit source arc. Arc FIDs append in Table row order.",
            };
            throw new InvalidDataException("Endpoint-start DeepConnect did not allocate its connection and end continuation in the proven Host append order: " +
                JsonSerializer.Serialize(diagnostics, JsonOptions));
        }
        int deepConnectArcFID = expectedDeepConnectAppendedFIDs[0];
        int deepConnectSourceContinuationFID = expectedDeepConnectAppendedFIDs[1];
        GeometryTopologyMapFeature deepConnectedFeature = ArcMapFeatures(deepConnected, "arc")
            .SingleOrDefault(feature => feature.FID == deepConnectArcFID)
            ?? throw new InvalidDataException("DeepConnect did not return its created source arc in the topology map.");
        GeometryTopologyCoordinate[] deepConnectPath = ArcMapPath(deepConnectedFeature);
        GeometryTopologyFeatureRow deepConnectSourceRowBefore = restoredAfterInternalStart.Arcs.Rows
            .Single(row => row.FID == deepConnectSourceFID);
        GeometryTopologyFeatureRow deepConnectConnectionRow = deepConnected.Arcs.Rows
            .Single(row => row.FID == deepConnectArcFID);
        bool deepConnectCapturedStartNodeMatchesSourceArc =
            deepConnectSourceRowBefore.StartNodeID == exactDeepConnectStartNodeFID;
        GeometryTopologyFeatureRow[] deepConnectSourceRows = deepConnected.Arcs.Rows
            .Where(row => row.FID == deepConnectSourceFID || row.FID == deepConnectSourceContinuationFID)
            .ToArray();
        GeometryTopologyMapFeature[] deepConnectSourceFeatures = ArcMapFeatures(deepConnected, "arc")
            .Where(feature => feature.FID == deepConnectSourceFID ||
                feature.FID == deepConnectSourceContinuationFID).ToArray();
        GeometryTopologyFeatureRow[] deepConnectEndNodeRows = deepConnected.Nodes.Rows
            .Where(row => !restoredAfterInternalStart.Nodes.Rows.Any(prior => prior.FID == row.FID) &&
                row.X is double x && row.Y is double y &&
                SmartConnectNear(new GeometryTopologyCoordinate(x, y), expectedDeepEnd))
            .ToArray();
        GeometryTopologyMapFeature[] deepConnectEndNodeFeatures = deepConnectEndNodeRows.Length == 1
            ? ArcMapFeatures(deepConnected, "node")
                .Where(feature => feature.FID == deepConnectEndNodeRows[0].FID).ToArray()
            : [];
        bool deepConnectEndAnchorOutsideExistingNodeHitWindow =
            SmartConnectAnchorOutsideNodeHitWindow(restoredAfterInternalStart.Nodes.Rows, expectedDeepEnd, mapCellSize);
        bool deepConnectEndNodeReadback = deepConnectEndNodeRows.Length == 1 &&
            deepConnectEndNodeFeatures.Length == 1 && deepConnectEndNodeFeatures[0].Paths.Length == 1 &&
            deepConnectEndNodeFeatures[0].Paths[0].Length == 1 &&
            SmartConnectNear(deepConnectEndNodeFeatures[0].Paths[0][0], expectedDeepEnd);
        bool deepConnectSourceRowsAndAttributesReadback =
            SmartConnectSourceSplitRowsReadback(deepConnected, deepConnectSourceFID,
                deepConnectSourceContinuationFID, deepConnectSourceRowBefore.Values,
                deepConnectSourceRowBefore.StartNodeID, deepConnectSourceRowBefore.EndNodeID,
                deepConnectEndNodeRows.Length == 1 ? deepConnectEndNodeRows[0].FID : null);
        bool deepConnectSourcePathReconstructsOriginal =
            SmartConnectSingleSplitReconstructsOriginalPath(deepConnectSourcePoints,
                deepConnectSourceFeatures.Select(ArcMapPath).ToArray(), endSegmentIndex, expectedDeepEnd);
        bool deepConnectOtherArcRowsPreserved = ArcMapBaselineRowsPreserved(
            restoredAfterInternalStart.Arcs.Rows.Where(row => row.FID != deepConnectSourceFID),
            deepConnected.Arcs.Rows);
        bool deepConnectOtherMapFeaturesPreserved = SmartConnectSourceMapFeaturesPreserved(
            (restoredAfterInternalStart.Map?.Features ?? []).Where(feature =>
                feature.FeatureKind != "arc" || feature.FID != deepConnectSourceFID),
            deepConnected.Map?.Features ?? []);
        bool deepConnectNodeRowsPreserved = ArcMapBaselineRowsPreserved(
            restoredAfterInternalStart.Nodes.Rows, deepConnected.Nodes.Rows);
        bool deepConnectRegionRowsPreserved = ArcMapBaselineRowsPreserved(
            restoredAfterInternalStart.Regions.Rows, deepConnected.Regions.Rows);
        bool deepConnectConnectionJoinsCapturedAnchors = deepConnectPath.Length >= 2 &&
            SmartConnectNear(deepConnectPath[0], expectedDeepStart) &&
            SmartConnectNear(deepConnectPath[^1], expectedDeepEnd);
        bool deepConnectConnectionNodeReferencesReadback = deepConnectEndNodeRows.Length == 1 &&
            deepConnectConnectionRow.StartNodeID == deepConnectSourceRowBefore.StartNodeID &&
            deepConnectConnectionRow.EndNodeID == deepConnectEndNodeRows[0].FID;
        bool deepConnectAnchorsAndDraftShapeReadback = deepConnected.IsDirty && deepConnected.CanUndo &&
            ArcMapTopologySignature(deepConnected) != ArcMapTopologySignature(smartAdded) &&
            deepConnectPriorArcFIDsDense && deepConnectAppendOrderMatchesHostContract &&
            deepConnectEndAnchorOutsideExistingNodeHitWindow && deepConnectConnectionJoinsCapturedAnchors &&
            deepConnectCapturedStartNodeMatchesSourceArc &&
            deepConnectConnectionNodeReferencesReadback && deepConnectEndNodeReadback &&
            deepConnectSourceRowsAndAttributesReadback &&
            deepConnectSourcePathReconstructsOriginal && deepConnectOtherArcRowsPreserved &&
            deepConnectOtherMapFeaturesPreserved && deepConnectNodeRowsPreserved &&
            deepConnectRegionRowsPreserved;
        if (!deepConnectAnchorsAndDraftShapeReadback)
        {
            object diagnostics = new
            {
                sourceFID = deepConnectSourceFID,
                sourceContinuationFID = deepConnectSourceContinuationFID,
                connectionFID = deepConnectArcFID,
                connectionRow = deepConnectConnectionRow,
                sourcePath = deepConnectSourcePoints,
                endAnchor = expectedDeepEnd,
                connectionPath = deepConnectPath,
                sourcePieces = deepConnectSourceFeatures.Select(feature => new
                {
                    feature.FID,
                    path = ArcMapPath(feature),
                    row = deepConnected.Arcs.Rows.Single(candidate => candidate.FID == feature.FID),
                }),
                allCreatedArcRows = deepConnectCreatedIDs.Select(fid =>
                {
                    GeometryTopologyFeatureRow row = deepConnected.Arcs.Rows.Single(candidate => candidate.FID == fid);
                    GeometryTopologyMapFeature? feature = ArcMapFeatures(deepConnected, "arc")
                        .SingleOrDefault(candidate => candidate.FID == fid);
                    return new
                    {
                        role = fid == deepConnectArcFID ? "deep-connect-connection" :
                            fid == deepConnectSourceContinuationFID ? "end-source-continuation" : "unclassified",
                        row.ID,
                        row.FID,
                        row.StartNodeID,
                        row.EndNodeID,
                        row.Values,
                        path = feature?.Paths,
                    };
                }).ToArray(),
                newEndNodeRows = deepConnectEndNodeRows,
                predicates = new
                {
                    deepConnected.IsDirty,
                    deepConnected.CanUndo,
                    deepConnectPriorArcFIDsDense,
                    deepConnectAppendOrderMatchesHostContract,
                    deepConnectEndAnchorOutsideExistingNodeHitWindow,
                    deepConnectConnectionJoinsCapturedAnchors,
                    deepConnectCapturedStartNodeMatchesSourceArc,
                    deepConnectConnectionNodeReferencesReadback,
                    deepConnectEndNodeReadback,
                    deepConnectSourceRowsAndAttributesReadback,
                    deepConnectSourcePathReconstructsOriginal,
                    deepConnectOtherArcRowsPreserved,
                    deepConnectOtherMapFeaturesPreserved,
                    deepConnectNodeRowsPreserved,
                    deepConnectRegionRowsPreserved,
                },
                sourceRowDifferences = deepConnectSourceRows.Select(row => new
                {
                    row.FID,
                    differences = SmartConnectCopiedArcAttributeDifferences(deepConnectSourceRowBefore.Values, row.Values),
                    row.Values,
                }),
                arcRowDiffs = SmartConnectRowDiffs(
                    restoredAfterInternalStart.Arcs.Rows.Where(row => row.FID != deepConnectSourceFID),
                    deepConnected.Arcs.Rows),
                nodeRowDiffs = SmartConnectRowDiffs(restoredAfterInternalStart.Nodes.Rows, deepConnected.Nodes.Rows),
                regionRowDiffs = SmartConnectRowDiffs(restoredAfterInternalStart.Regions.Rows, deepConnected.Regions.Rows),
            };
            throw new InvalidDataException("Endpoint-start DeepConnect did not preserve its two source pieces, copied attributes, exact anchors, and unaffected Geometry rows: " +
                JsonSerializer.Serialize(diagnostics, JsonOptions));
        }
        string afterDeepConnectSignature = ArcMapTopologySignature(deepConnected);

        OperationReceipt undo = await SubmitOperationAsync(client, "topology.edit", new GeometryTopologyEditRequest(
            geometryModel, deepConnected.Revision, deepConnected.DraftState,
            new GeometryTopologyMutation("undo", "history", [])), cancellation);
        operations.Add(new { action = "undoDeepConnect", id = undo.Id, state = undo.State });
        GeometryTopologySnapshot undone = ArcMapReadTopology(undo.Result);
        topologyStageDiagnostics.Add(SmartConnectTopologyStageDiagnostics("undoDeepConnect", deepConnected, undone));
        bool draftUndoReadback = ArcMapTopologySignature(undone) == afterSmartAddSignature &&
            undone.CanUndo && undone.CanRedo;
        if (!draftUndoReadback)
            throw new InvalidDataException("Undo did not remove DeepConnect while restoring the exact SmartAdd draft.");

        OperationReceipt redo = await SubmitOperationAsync(client, "topology.edit", new GeometryTopologyEditRequest(
            geometryModel, undone.Revision, undone.DraftState,
            new GeometryTopologyMutation("redo", "history", [])), cancellation);
        operations.Add(new { action = "redoDeepConnect", id = redo.Id, state = redo.State });
        GeometryTopologySnapshot redone = ArcMapReadTopology(redo.Result);
        topologyStageDiagnostics.Add(SmartConnectTopologyStageDiagnostics("redoDeepConnect", undone, redone));
        bool draftRedoReadback = ArcMapTopologySignature(redone) == afterDeepConnectSignature &&
            redone.CanUndo && !redone.CanRedo;
        if (!draftRedoReadback)
            throw new InvalidDataException("Redo did not restore the accepted DeepConnect draft and captured anchors.");

        bool originalSourceRowsAndAttributesPreserved =
            SmartConnectSourceSplitRowsReadback(redone, deepConnectSourceFID,
                deepConnectSourceContinuationFID, deepConnectSourceRowBefore.Values,
                deepConnectSourceRowBefore.StartNodeID, deepConnectSourceRowBefore.EndNodeID,
                deepConnectEndNodeRows.Length == 1 ? deepConnectEndNodeRows[0].FID : null) &&
            SmartConnectSingleSplitReconstructsOriginalPath(deepConnectSourcePoints,
                ArcMapFeatures(redone, "arc")
                    .Where(feature => feature.FID == deepConnectSourceFID ||
                        feature.FID == deepConnectSourceContinuationFID)
                    .Select(ArcMapPath).ToArray(), endSegmentIndex, expectedDeepEnd) &&
            ArcMapBaselineRowsPreserved(
                restoredAfterInternalStart.Arcs.Rows.Where(row => row.FID != deepConnectSourceFID),
                redone.Arcs.Rows) &&
            ArcMapBaselineRowsPreserved(restoredAfterInternalStart.Nodes.Rows, redone.Nodes.Rows) &&
            ArcMapBaselineRowsPreserved(restoredAfterInternalStart.Regions.Rows, redone.Regions.Rows) &&
            SmartConnectSourceMapFeaturesPreserved(
                (restoredAfterInternalStart.Map?.Features ?? []).Where(feature =>
                    feature.FeatureKind != "arc" || feature.FID != deepConnectSourceFID),
                redone.Map?.Features ?? []);
        if (!originalSourceRowsAndAttributesPreserved)
        {
            object diagnostics = new
            {
                sourceFID = deepConnectSourceFID,
                sourceContinuationFID = deepConnectSourceContinuationFID,
                sourcePaths = ArcMapFeatures(redone, "arc")
                    .Where(feature => feature.FID == deepConnectSourceFID ||
                        feature.FID == deepConnectSourceContinuationFID)
                    .Select(feature => new { feature.FID, path = ArcMapPath(feature) }),
                sourceRowDiffs = SmartConnectRowDiffs([deepConnectSourceRowBefore],
                    redone.Arcs.Rows.Where(row => row.FID == deepConnectSourceFID ||
                        row.FID == deepConnectSourceContinuationFID)),
                unchangedArcDiffs = SmartConnectRowDiffs(
                    restoredAfterInternalStart.Arcs.Rows.Where(row => row.FID != deepConnectSourceFID),
                    redone.Arcs.Rows),
                nodeDiffs = SmartConnectRowDiffs(restoredAfterInternalStart.Nodes.Rows, redone.Nodes.Rows),
                regionDiffs = SmartConnectRowDiffs(restoredAfterInternalStart.Regions.Rows, redone.Regions.Rows),
                sourceAttributesPreserved = SmartConnectSourceSplitRowsReadback(redone, deepConnectSourceFID,
                    deepConnectSourceContinuationFID, deepConnectSourceRowBefore.Values,
                    deepConnectSourceRowBefore.StartNodeID, deepConnectSourceRowBefore.EndNodeID,
                    deepConnectEndNodeRows.Length == 1 ? deepConnectEndNodeRows[0].FID : null),
                sourcePathReconstructsOriginal = SmartConnectSingleSplitReconstructsOriginalPath(
                    deepConnectSourcePoints,
                    ArcMapFeatures(redone, "arc")
                        .Where(feature => feature.FID == deepConnectSourceFID ||
                            feature.FID == deepConnectSourceContinuationFID)
                        .Select(ArcMapPath).ToArray(), endSegmentIndex, expectedDeepEnd),
            };
            throw new InvalidDataException("The endpoint-start DeepConnect redo failed source-row/attribute preservation: " +
                JsonSerializer.Serialize(diagnostics, JsonOptions));
        }

        bool sourceHdfUnchangedBeforeSave = FileSHA256(geometryPath)
                .Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase);
        if (!sourceHdfUnchangedBeforeSave)
            throw new InvalidDataException("SmartAdd or DeepConnect changed a saved source before the parent model.save operation.");

        OperationReceipt save = await SubmitOperationAsync(client, "model.save", new
        {
            assetPath = geometryPath,
            kind = "Geometry",
            projectPath,
            revision = geometryRevision,
            fields = new Dictionary<string, string>(),
            images = Property(modelLoad.Result, "images"),
            projectXml,
            sourceState = OptionalString(modelLoad.Result, "sourceState"),
            geometryTopologyState = redone.DraftState,
            coreLayerContext,
        }, cancellation);
        string savedRevision = StringProperty(save.Result, "revision");
        bool parentSaveChangedHdfRevision = IsSHA256(savedRevision) &&
            !savedRevision.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(geometryPath).Equals(savedRevision, StringComparison.OrdinalIgnoreCase);
        if (!parentSaveChangedHdfRevision)
            throw new InvalidDataException("The parent model.save did not publish SmartAdd and DeepConnect to the Geometry HDF.");

        OperationReceipt reopenedProject = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        string reopenedProjectXml = StringProperty(reopenedProject.Result, "projectXml");
        if (!StringProperty(reopenedProject.Result, "revision").Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            reopenedProjectXml != projectXml || !FileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !FileSHA256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Geometry parent save unexpectedly changed .ras or .vis bytes.");
        JsonElement reopenedCoreContextElement = PropertyOrNull(reopenedProject.Result, "coreLayerContext");
        CoreLayerRequestContext? reopenedCoreLayerContext = reopenedCoreContextElement.ValueKind == JsonValueKind.Object
            ? reopenedCoreContextElement.Deserialize<CoreLayerRequestContext>(JsonOptions)
            : null;
        var reopenedGeometryModel = new GeometryTopologyModelRequest(geometryPath, "Geometry", projectPath,
            reopenedProjectXml, reopenedCoreLayerContext);
        OperationReceipt reopenedModel = await SubmitOperationAsync(client, "model.load", reopenedGeometryModel, cancellation);
        if (!StringProperty(reopenedModel.Result, "revision").Equals(savedRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("model.load did not reopen the committed SmartConnect Geometry revision.");
        OperationReceipt reopenedTopologyOperation = await SubmitOperationAsync(client, "topology.load",
            new GeometryTopologyLoadRequest(reopenedGeometryModel, savedRevision), cancellation);
        GeometryTopologySnapshot reopenedTopology = ArcMapReadTopology(reopenedTopologyOperation.Result);
        RequireTopologyOwner(reopenedTopologyOperation.Result, savedRevision);
        topologyStageDiagnostics.Add(SmartConnectTopologyStageDiagnostics("reopened", redone,
            reopenedTopology));
        GeometryTopologyMapFeature reopenedSmartAdd = ArcMapFeatures(reopenedTopology, "arc")
            .SingleOrDefault(feature => feature.FID == smartAddArcFID)
            ?? throw new InvalidDataException("The committed Geometry HDF did not reopen the SmartAdd arc.");
        GeometryTopologyMapFeature reopenedDeepConnect = ArcMapFeatures(reopenedTopology, "arc")
            .SingleOrDefault(feature => feature.FID == deepConnectArcFID)
            ?? throw new InvalidDataException("The committed Geometry HDF did not reopen the DeepConnect arc.");
        GeometryTopologyFeatureRow[] reopenedConnectionRows = reopenedTopology.Arcs.Rows
            .Where(row => row.FID == deepConnectArcFID).ToArray();
        GeometryTopologyMapFeature[] reopenedSourceFeatures = ArcMapFeatures(reopenedTopology, "arc")
            .Where(feature => feature.FID == deepConnectSourceFID ||
                feature.FID == deepConnectSourceContinuationFID).ToArray();
        GeometryTopologyFeatureRow[] reopenedEndNodeRows = reopenedTopology.Nodes.Rows
            .Where(row => deepConnectEndNodeRows.Any(expected => expected.FID == row.FID &&
                expected.X == row.X && expected.Y == row.Y))
            .ToArray();
        GeometryTopologyMapFeature[] reopenedEndNodeFeatures = reopenedEndNodeRows.Length == 1
            ? ArcMapFeatures(reopenedTopology, "node")
                .Where(feature => feature.FID == reopenedEndNodeRows[0].FID).ToArray()
            : [];
        bool reopenedDeepConnectEndNodeReadback = deepConnectEndNodeRows.Length == 1 &&
            reopenedEndNodeRows.Length == 1 && reopenedEndNodeFeatures.Length == 1 &&
            reopenedEndNodeFeatures[0].Paths.Length == 1 && reopenedEndNodeFeatures[0].Paths[0].Length == 1 &&
            SmartConnectNear(reopenedEndNodeFeatures[0].Paths[0][0], expectedDeepEnd) &&
            ArcMapMapFeaturesEqual(deepConnectEndNodeFeatures, reopenedEndNodeFeatures);
        bool reopenedDeepConnectConnectionNodeReferencesReadback = reopenedConnectionRows.Length == 1 &&
            reopenedConnectionRows[0].StartNodeID == deepConnectSourceRowBefore.StartNodeID &&
            reopenedConnectionRows[0].EndNodeID == deepConnectEndNodeRows.SingleOrDefault()?.FID;
        GeometryTopologyMapFeature[] redoneSourceFeatures = ArcMapFeatures(redone, "arc")
            .Where(feature => feature.FID == deepConnectSourceFID ||
                feature.FID == deepConnectSourceContinuationFID).ToArray();
        bool reopenedSmartAddAndDeepConnectReadback = !reopenedTopology.IsDirty &&
            !reopenedTopology.CanUndo && !reopenedTopology.CanRedo &&
            SmartConnectSourceSplitRowsReadback(reopenedTopology, deepConnectSourceFID,
                deepConnectSourceContinuationFID, deepConnectSourceRowBefore.Values,
                deepConnectSourceRowBefore.StartNodeID, deepConnectSourceRowBefore.EndNodeID,
                deepConnectEndNodeRows.Length == 1 ? deepConnectEndNodeRows[0].FID : null) &&
            SmartConnectSingleSplitReconstructsOriginalPath(deepConnectSourcePoints,
                reopenedSourceFeatures.Select(ArcMapPath).ToArray(), endSegmentIndex, expectedDeepEnd) &&
            ArcMapMapFeaturesEqual(redoneSourceFeatures, reopenedSourceFeatures) &&
            reopenedDeepConnectEndNodeReadback && reopenedDeepConnectConnectionNodeReferencesReadback &&
            ArcMapBaselineRowsPreserved(
                restoredAfterInternalStart.Arcs.Rows.Where(row => row.FID != deepConnectSourceFID),
                reopenedTopology.Arcs.Rows) &&
            ArcMapBaselineRowsPreserved(restoredAfterInternalStart.Nodes.Rows, reopenedTopology.Nodes.Rows) &&
            ArcMapBaselineRowsPreserved(restoredAfterInternalStart.Regions.Rows, reopenedTopology.Regions.Rows) &&
            SmartConnectSourceMapFeaturesPreserved(
                (restoredAfterInternalStart.Map?.Features ?? []).Where(feature =>
                    feature.FeatureKind != "arc" || feature.FID != deepConnectSourceFID),
                reopenedTopology.Map?.Features ?? []) &&
            ArcMapMapFeaturesEqual([smartAddFeature], [reopenedSmartAdd]) &&
            ArcMapMapFeaturesEqual([deepConnectedFeature], [reopenedDeepConnect]);
        if (!reopenedSmartAddAndDeepConnectReadback)
        {
            object diagnostics = new
            {
                sourceFID = deepConnectSourceFID,
                sourceContinuationFID = deepConnectSourceContinuationFID,
                connectionFID = deepConnectArcFID,
                reopenedSourcePieces = reopenedSourceFeatures.Select(feature => new
                {
                    feature.FID,
                    path = ArcMapPath(feature),
                }),
                expectedSourcePath = deepConnectSourcePoints,
                endAnchor = expectedDeepEnd,
                reopenedRowsAndAttributesPreserved = SmartConnectSourceSplitRowsReadback(
                    reopenedTopology, deepConnectSourceFID, deepConnectSourceContinuationFID,
                    deepConnectSourceRowBefore.Values, deepConnectSourceRowBefore.StartNodeID,
                    deepConnectSourceRowBefore.EndNodeID,
                    deepConnectEndNodeRows.Length == 1 ? deepConnectEndNodeRows[0].FID : null),
                reopenedSourcePathReconstructsOriginal = SmartConnectSingleSplitReconstructsOriginalPath(
                    deepConnectSourcePoints, reopenedSourceFeatures.Select(ArcMapPath).ToArray(),
                    endSegmentIndex, expectedDeepEnd),
                reopenedEndNode = new
                {
                    matchesExpected = reopenedDeepConnectEndNodeReadback,
                    rows = reopenedEndNodeRows,
                    features = reopenedEndNodeFeatures,
                },
                reopenedConnectionRows,
                reopenedDeepConnectConnectionNodeReferencesReadback,
                sourceRows = SmartConnectRowDiffs(
                    redone.Arcs.Rows.Where(row => row.FID == deepConnectSourceFID ||
                        row.FID == deepConnectSourceContinuationFID),
                    reopenedTopology.Arcs.Rows),
                changedUnrelatedRows = SmartConnectRowDiffs(
                    restoredAfterInternalStart.Arcs.Rows.Where(row => row.FID != deepConnectSourceFID),
                    reopenedTopology.Arcs.Rows),
            };
            throw new InvalidDataException("The saved Geometry HDF did not retain endpoint-start DeepConnect source pieces, connection, or anchor node: " +
                JsonSerializer.Serialize(diagnostics, JsonOptions));
        }

        OperationReceipt reopenedTreeOperation = await SubmitOperationAsync(client, "map.tree.load",
            mapOwner.MapRequest, cancellation);
        MapTreeSnapshot reopenedTree = JsonSerializer.Deserialize<MapTreeSnapshot>(
            reopenedTreeOperation.Result.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("The reopened saved map tree response was empty.");
        MapGeometryOwner? reopenedOwner = FindGeometryOwner(mapOwner.MapID, mapOwner.MapIndex,
            mapOwner.MapName, mapOwner.MapType, visualizationRevision, reopenedTree.Roots,
            mapOwner.MapRequest, TopologyMapFixturePlanName, TopologyMapFixtureGeometryName, geometryPath);
        if (reopenedOwner is null)
            throw new InvalidDataException("The reopened saved map no longer resolves the exact Solver Geometry owner.");
        OperationReceipt reopenedMapGeometry = await SubmitOperationAsync(client, "map.geometry.load", new
        {
            map = reopenedOwner.MapRequest,
            geometryNode = reopenedOwner.GeometryNode,
            pageSize = 512,
            skipFlowAreasPage = true,
            skipCulvertsPage = true,
            planNodes = reopenedOwner.PlanNodes,
        }, cancellation);
        string reopenedContextToken = StringProperty(reopenedMapGeometry.Result, "contextToken");
        if (!StringProperty(reopenedMapGeometry.Result, "geometryRevision")
                .Equals(savedRevision, StringComparison.OrdinalIgnoreCase) ||
            !IsSHA256(reopenedContextToken) || reopenedContextToken.Equals(contextToken, StringComparison.Ordinal))
            throw new InvalidDataException("The reopened map Geometry owner did not bind the saved revision to a fresh context token.");

        bool originalFixtureUnchanged = ArcMapFixtureSnapshotEquals(sourceFixtureSnapshot,
                ArcMapFixtureSnapshot(sourceDirectory)) &&
            FileSHA256(sourceProjectPath).Equals(sourceProjectSHA, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(sourceGeometryPath).Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(sourceVisualizationPath).Equals(sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase);
        if (!originalFixtureUnchanged)
            throw new InvalidDataException("The accepted source fixture changed; SmartConnect edits must remain inside the isolated copy.");

        string manifestPath = Path.Combine(outputDirectory, "geometry-smart-connect-http-e2e-manifest.json");
        var report = new GeometrySmartConnectAcceptanceReport(
            State: "completed",
            ManifestPath: manifestPath,
            SourceManifestPath: sourceManifestPath,
            SourceProjectPath: sourceProjectPath,
            SourceGeometryPath: sourceGeometryPath,
            ProjectPath: projectPath,
            GeometryPath: geometryPath,
            MapID: mapOwner.MapID,
            MapName: mapOwner.MapName,
            InitialContextToken: contextToken,
            ReopenedContextToken: reopenedContextToken,
            SourceRevision: geometryRevision,
            SavedRevision: savedRevision,
            TerrainCapture: terrainCapture,
            RasterWindow: rasterWindow,
            SmartAddOptions: smartAddOptions,
            SmartAddArcFID: smartAddArcFID,
            SmartAddPath: smartAddPath,
            DeepConnectArcFID: deepConnectArcFID,
            DeepConnectSourceContinuationFID: deepConnectSourceContinuationFID,
            DeepConnectSourceLocator: "arc:" + deepConnectSourceFID.ToString(CultureInfo.InvariantCulture),
            StartSegmentIndex: startSegmentIndex,
            StartSegmentFraction: startSegmentFraction,
            EndSegmentIndex: endSegmentIndex,
            EndSegmentFraction: endSegmentFraction,
            DeepConnectPath: deepConnectPath,
            Operations: operations.ToArray(),
            SavedMapCatalogReadback: true,
            ExactGeometryMapContextAndSourceReadback: true,
            AssociatedTerrainAndRasterWindowReadback: true,
            StaleTerrainCaptureRejected: staleTerrainCaptureRejected,
            SmartAddDraftShapeReadback: smartAddDraftShapeReadback,
            InvalidDeepConnectAnchorRejected: invalidDeepConnectAnchorRejected,
            DeepConnectInternalStartArcTargetReadback: deepConnectInternalStartArcTargetReadback,
            DeepConnectAnchorsAndDraftShapeReadback: deepConnectAnchorsAndDraftShapeReadback,
            OriginalSourceRowsAndAttributesPreserved: originalSourceRowsAndAttributesPreserved,
            DraftUndoReadback: draftUndoReadback,
            DraftRedoReadback: draftRedoReadback,
            SourceHdfUnchangedBeforeSave: sourceHdfUnchangedBeforeSave,
            ParentSaveChangedHdfRevision: parentSaveChangedHdfRevision,
            ReopenedSmartAddAndDeepConnectReadback: reopenedSmartAddAndDeepConnectReadback,
            OriginalFixtureUnchanged: originalFixtureUnchanged,
            TopologyStageDiagnostics: topologyStageDiagnostics.ToArray(),
            RejectedRequests: rejected.ToArray());
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(report, JsonOptions),
            new UTF8Encoding(false), cancellation);
        return report;
    }

    private static GeometryTopologyCoordinate SmartConnectInterpolate(
        GeometryTopologyCoordinate[] points, int segmentIndex, double fraction)
    {
        if (segmentIndex < 0 || segmentIndex >= points.Length - 1 ||
            !double.IsFinite(fraction) || fraction is < 0d or > 1d)
            throw new InvalidDataException("The captured DeepConnect source anchor is outside its source segment.");
        GeometryTopologyCoordinate first = points[segmentIndex];
        GeometryTopologyCoordinate second = points[segmentIndex + 1];
        return new GeometryTopologyCoordinate(first.X + (second.X - first.X) * fraction,
            first.Y + (second.Y - first.Y) * fraction);
    }

    private static bool SmartConnectNear(GeometryTopologyCoordinate actual,
        GeometryTopologyCoordinate expected) =>
        Math.Abs(actual.X - expected.X) <= 1e-9d && Math.Abs(actual.Y - expected.Y) <= 1e-9d;

    // MapData derives region-boundary polygons from CompletedRegions. They are
    // ephemeral topology output, unlike source arcs, nodes, and control points.
    // Compare all source features exactly and record derived-boundary transitions
    // separately with the Host's completion diagnostics.
    private static bool SmartConnectSourceMapFeaturesPreserved(
        IEnumerable<GeometryTopologyMapFeature> baseline, IEnumerable<GeometryTopologyMapFeature> current) =>
        ArcMapMapFeaturesPreserved(SmartConnectPersistentSourceMapFeatures(baseline),
            SmartConnectPersistentSourceMapFeatures(current));

    private static GeometryTopologyMapFeature[] SmartConnectPersistentSourceMapFeatures(
        IEnumerable<GeometryTopologyMapFeature> features) => features
        .Where(feature => !feature.ID.StartsWith("region-boundary:", StringComparison.Ordinal))
        .ToArray();

    private static GeometrySmartConnectTopologyStageDiagnostics SmartConnectTopologyStageDiagnostics(
        string stage, GeometryTopologySnapshot before, GeometryTopologySnapshot after)
    {
        string[] beforeBoundaries = SmartConnectDerivedBoundaryFeatureSignatures(before);
        string[] afterBoundaries = SmartConnectDerivedBoundaryFeatureSignatures(after);
        return new GeometrySmartConnectTopologyStageDiagnostics(stage, beforeBoundaries,
            afterBoundaries, !beforeBoundaries.SequenceEqual(afterBoundaries, StringComparer.Ordinal),
            before.Completion?.IsComplete, before.Completion?.Messages ?? [],
            after.Completion?.IsComplete, after.Completion?.Messages ?? []);
    }

    private static string[] SmartConnectDerivedBoundaryFeatureSignatures(GeometryTopologySnapshot snapshot) =>
        (snapshot.Map?.Features ?? []).Where(feature =>
                feature.ID.StartsWith("region-boundary:", StringComparison.Ordinal))
            .OrderBy(feature => feature.ID, StringComparer.Ordinal)
            .Select(feature => feature.ID + ":" + TextSHA256(JsonSerializer.Serialize(new
            {
                feature.FID,
                feature.GeometryType,
                feature.SelectionIDs,
                feature.Paths,
            }, JsonOptions)))
            .ToArray();

    private static bool SmartConnectPathTouchesAnchor(GeometryTopologyMapFeature feature,
        GeometryTopologyCoordinate anchor)
    {
        GeometryTopologyCoordinate[] path = ArcMapPath(feature);
        return path.Length >= 2 &&
            (SmartConnectNear(path[0], anchor) || SmartConnectNear(path[^1], anchor));
    }

    private static bool SmartConnectAnchorOutsideNodeHitWindow(
        IEnumerable<GeometryTopologyFeatureRow> nodes, GeometryTopologyCoordinate anchor, double cellSize)
    {
        double radius = 6d * cellSize;
        if (!double.IsFinite(radius) || radius < 0d) return false;
        foreach (GeometryTopologyFeatureRow node in nodes)
        {
            if (node.X is not double x || node.Y is not double y) return false;
            // HitTestNode uses an Extent.CenteredOn(..., 2 * radius), so a node is hit
            // when both coordinate deltas are within this axis-aligned radius.
            if (Math.Abs(x - anchor.X) <= radius && Math.Abs(y - anchor.Y) <= radius)
                return false;
        }
        return true;
    }

    private static bool SmartConnectSourceSplitRowsReadback(GeometryTopologySnapshot snapshot,
        int sourceFID, int continuationFID, IReadOnlyDictionary<string, string> originalSourceValues,
        int? originalStartNodeFID, int? originalEndNodeFID, int? splitNodeFID)
    {
        if (sourceFID == continuationFID || originalStartNodeFID is null ||
            originalEndNodeFID is null || splitNodeFID is null) return false;
        GeometryTopologyFeatureRow[] rows = snapshot.Arcs.Rows
            .Where(row => row.FID == sourceFID || row.FID == continuationFID).ToArray();
        GeometryTopologyFeatureRow[] sourceRows = rows.Where(row => row.FID == sourceFID).ToArray();
        GeometryTopologyFeatureRow[] continuationRows = rows.Where(row => row.FID == continuationFID).ToArray();
        return rows.Length == 2 && sourceRows.Length == 1 && continuationRows.Length == 1 &&
            sourceRows[0].StartNodeID == originalStartNodeFID &&
            sourceRows[0].EndNodeID == splitNodeFID &&
            continuationRows[0].StartNodeID == splitNodeFID &&
            continuationRows[0].EndNodeID == originalEndNodeFID &&
            rows.All(row => ArcMapCopiedArcAttributesEqual(originalSourceValues, row.Values));
    }

    private static bool SmartConnectSingleSplitReconstructsOriginalPath(
        GeometryTopologyCoordinate[] original, GeometryTopologyCoordinate[][] pieces,
        int splitSegmentIndex, GeometryTopologyCoordinate splitPoint)
    {
        if (pieces.Length != 2 || original.Length < 2 || pieces.Any(piece => piece.Length < 2) ||
            splitSegmentIndex < 0 || splitSegmentIndex >= original.Length - 1)
            return false;
        int splitPointIndex = splitSegmentIndex + 1;
        foreach ((GeometryTopologyCoordinate[] first, GeometryTopologyCoordinate[] second) in new[]
        {
            (pieces[0], pieces[1]), (pieces[1], pieces[0]),
        })
        foreach (bool reverseFirst in new[] { false, true })
        foreach (bool reverseSecond in new[] { false, true })
        {
            GeometryTopologyCoordinate[] directedFirst = reverseFirst ? first.Reverse().ToArray() : first;
            GeometryTopologyCoordinate[] directedSecond = reverseSecond ? second.Reverse().ToArray() : second;
            if (!directedFirst[0].Equals(original[0]) || !SmartConnectNear(directedFirst[^1], splitPoint) ||
                !SmartConnectNear(directedSecond[0], splitPoint) ||
                !directedSecond[^1].Equals(original[^1]))
                continue;
            GeometryTopologyCoordinate[] joined = directedFirst.Concat(directedSecond.Skip(1)).ToArray();
            if (joined.Length != original.Length + 1 ||
                !SmartConnectNear(joined[splitPointIndex], splitPoint)) continue;
            if (joined.Where((_, index) => index != splitPointIndex).SequenceEqual(original)) return true;
        }
        return false;
    }

    private static bool SmartConnectSplitReconstructsOriginalPath(GeometryTopologyCoordinate[] original,
        GeometryTopologyCoordinate[][] pieces, int startSegmentIndex, GeometryTopologyCoordinate startAnchor,
        int endSegmentIndex, GeometryTopologyCoordinate endAnchor)
    {
        if (pieces.Length != 3 || original.Length < 3 || pieces.Any(piece => piece.Length < 2) ||
            startSegmentIndex < 0 || endSegmentIndex <= startSegmentIndex || endSegmentIndex >= original.Length - 1)
            return false;

        GeometryTopologyCoordinate[] expectedWithAnchors = original.Take(startSegmentIndex + 1)
            .Append(startAnchor)
            .Concat(original.Skip(startSegmentIndex + 1).Take(endSegmentIndex - startSegmentIndex))
            .Append(endAnchor)
            .Concat(original.Skip(endSegmentIndex + 1))
            .ToArray();
        int startAnchorIndex = startSegmentIndex + 1;
        int endAnchorIndex = endSegmentIndex + 2;
        int[][] orders =
        [
            [0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0],
        ];
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        for (int orientation = 0; orientation < 8; orientation++)
        {
            GeometryTopologyCoordinate[][] directed = new GeometryTopologyCoordinate[3][];
            for (int pieceIndex = 0; pieceIndex < 3; pieceIndex++)
            {
                GeometryTopologyCoordinate[] piece = pieces[orders[orderIndex][pieceIndex]];
                directed[pieceIndex] = (orientation & (1 << pieceIndex)) == 0
                    ? piece : piece.Reverse().ToArray();
            }
            GeometryTopologyCoordinate[] first = directed[0];
            GeometryTopologyCoordinate[] second = directed[1];
            GeometryTopologyCoordinate[] third = directed[2];
            if (!first[0].Equals(original[0]) || !SmartConnectNear(first[^1], startAnchor) ||
                !SmartConnectNear(second[0], startAnchor) || !SmartConnectNear(second[^1], endAnchor) ||
                !SmartConnectNear(third[0], endAnchor) || !third[^1].Equals(original[^1]))
                continue;

            GeometryTopologyCoordinate[] joined = first.Concat(second.Skip(1)).Concat(third.Skip(1)).ToArray();
            if (joined.Length != expectedWithAnchors.Length) continue;
            bool matchesExpected = true;
            for (int index = 0; index < joined.Length; index++)
            {
                bool matches = index == startAnchorIndex
                    ? SmartConnectNear(joined[index], startAnchor)
                    : index == endAnchorIndex
                        ? SmartConnectNear(joined[index], endAnchor)
                        : joined[index].Equals(expectedWithAnchors[index]);
                if (!matches)
                {
                    matchesExpected = false;
                    break;
                }
            }
            if (!matchesExpected) continue;

            GeometryTopologyCoordinate[] withoutInsertedAnchors = joined
                .Where((_, index) => index != startAnchorIndex && index != endAnchorIndex).ToArray();
            if (withoutInsertedAnchors.SequenceEqual(original)) return true;
        }
        return false;
    }

    private static string[] SmartConnectCopiedArcAttributeDifferences(
        IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        HashSet<string> geometryColumns = ["FID", "StartNodeID", "EndNodeID", "Length"];
        Dictionary<string, string> expectedAttributes = expected.Where(pair => !geometryColumns.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Dictionary<string, string> actualAttributes = actual.Where(pair => !geometryColumns.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return ArcMapValueDifferences(expectedAttributes, actualAttributes);
    }

    private static object SmartConnectRowDiffs(IEnumerable<GeometryTopologyFeatureRow> baseline,
        IEnumerable<GeometryTopologyFeatureRow> current)
    {
        GeometryTopologyFeatureRow[] expectedRows = baseline.ToArray();
        GeometryTopologyFeatureRow[] actualRows = current.ToArray();
        var changedRows = new List<object>();
        foreach (GeometryTopologyFeatureRow expected in expectedRows)
        {
            GeometryTopologyFeatureRow[] matches = actualRows.Where(row => row.FID == expected.FID).ToArray();
            if (matches.Length != 1)
            {
                changedRows.Add(new { expected.FID, matchCount = matches.Length,
                    actual = matches.Select(row => new { row.ID, row.Values, row.X, row.Y, row.StartNodeID, row.EndNodeID }) });
                continue;
            }

            GeometryTopologyFeatureRow actual = matches[0];
            var differences = new List<string>();
            if (expected.ID != actual.ID) differences.Add($"ID: {expected.ID} -> {actual.ID}");
            if (!ArcMapValuesEqual(expected.Values, actual.Values))
                differences.AddRange(ArcMapValueDifferences(expected.Values, actual.Values)
                    .Select(value => "Values." + value));
            if (expected.X != actual.X) differences.Add($"X: {expected.X} -> {actual.X}");
            if (expected.Y != actual.Y) differences.Add($"Y: {expected.Y} -> {actual.Y}");
            if (expected.StartNodeID != actual.StartNodeID)
                differences.Add($"StartNodeID: {expected.StartNodeID} -> {actual.StartNodeID}");
            if (expected.EndNodeID != actual.EndNodeID)
                differences.Add($"EndNodeID: {expected.EndNodeID} -> {actual.EndNodeID}");
            if (differences.Count > 0) changedRows.Add(new { expected.FID, differences });
        }

        HashSet<int> expectedFIDs = expectedRows.Select(row => row.FID).ToHashSet();
        int[] extraFIDs = actualRows.Where(row => !expectedFIDs.Contains(row.FID))
            .Select(row => row.FID).Order().ToArray();
        return new
        {
            baselineCount = expectedRows.Length,
            actualCount = actualRows.Length,
            changedRows,
            extraFIDs,
        };
    }

    private static object SmartConnectMapFeatureDiffs(IEnumerable<GeometryTopologyMapFeature> baseline,
        IEnumerable<GeometryTopologyMapFeature> current)
    {
        GeometryTopologyMapFeature[] expectedFeatures = baseline.Where(feature => feature.FID is not null).ToArray();
        GeometryTopologyMapFeature[] actualFeatures = current.Where(feature => feature.FID is not null).ToArray();
        var changedFeatures = new List<object>();
        foreach (GeometryTopologyMapFeature expected in expectedFeatures)
        {
            GeometryTopologyMapFeature[] bySourceIdentity = actualFeatures.Where(feature =>
                feature.FeatureKind == expected.FeatureKind && feature.FID == expected.FID).ToArray();
            GeometryTopologyMapFeature[] exactIDMatches = bySourceIdentity
                .Where(feature => feature.ID == expected.ID).ToArray();
            GeometryTopologyMapFeature[] matches = exactIDMatches.Length == 1
                ? exactIDMatches : bySourceIdentity;
            if (matches.Length != 1)
            {
                changedFeatures.Add(new
                {
                    expected.FeatureKind,
                    expected.FID,
                    expected.ID,
                    matchCount = matches.Length,
                    actual = matches.Select(feature => new { feature.ID, feature.GeometryType,
                        feature.SelectionIDs, feature.Paths, feature.IncidentArcs }),
                });
                continue;
            }

            GeometryTopologyMapFeature actual = matches[0];
            var differences = new List<object>();
            if (expected.ID != actual.ID)
                differences.Add(new { field = "ID", expected = expected.ID, actual = actual.ID });
            if (expected.GeometryType != actual.GeometryType)
                differences.Add(new { field = "GeometryType", expected = expected.GeometryType, actual = actual.GeometryType });
            if (!expected.SelectionIDs.SequenceEqual(actual.SelectionIDs, StringComparer.Ordinal))
                differences.Add(new { field = "SelectionIDs", expected = expected.SelectionIDs, actual = actual.SelectionIDs });
            bool pathsEqual = expected.Paths.Length == actual.Paths.Length &&
                expected.Paths.Zip(actual.Paths).All(paths => paths.First.SequenceEqual(paths.Second));
            if (!pathsEqual)
                differences.Add(new { field = "Paths", expected = expected.Paths, actual = actual.Paths });
            bool incidentArcsEqual = (expected.IncidentArcs ?? []).SequenceEqual(actual.IncidentArcs ?? []);
            if (!incidentArcsEqual)
                differences.Add(new { field = "IncidentArcs", expected = expected.IncidentArcs, actual = actual.IncidentArcs });
            if (differences.Count > 0)
                changedFeatures.Add(new { expected.FeatureKind, expected.FID, expected.ID, differences });
        }

        HashSet<(string Kind, int FID, string ID)> expectedIDs = expectedFeatures
            .Select(feature => (feature.FeatureKind, feature.FID!.Value, feature.ID)).ToHashSet();
        var extraFeatures = actualFeatures.Where(feature =>
                !expectedIDs.Contains((feature.FeatureKind, feature.FID!.Value, feature.ID)))
            .Select(feature => new { feature.FeatureKind, feature.FID, feature.ID,
                feature.GeometryType, feature.SelectionIDs, feature.Paths, feature.IncidentArcs })
            .ToArray();
        return new
        {
            baselineCount = expectedFeatures.Length,
            actualCount = actualFeatures.Length,
            changedFeatures,
            extraFeatures,
        };
    }

    private static int? SmartConnectExactEndpointNodeFID(GeometryTopologySnapshot snapshot,
        GeometryTopologyCoordinate point)
    {
        GeometryTopologyFeatureRow[] rows = snapshot.Nodes.Rows
            .Where(row => row.X == point.X && row.Y == point.Y && row.FID >= 0).ToArray();
        if (rows.Length != 1) return null;
        int fid = rows[0].FID;
        GeometryTopologyMapFeature[] features = ArcMapFeatures(snapshot, "node")
            .Where(feature => feature.FID == fid).ToArray();
        if (features.Length != 1) return null;
        GeometryTopologyMapFeature feature = features[0];
        return string.Equals(feature.GeometryType, "point", StringComparison.OrdinalIgnoreCase) &&
            feature.Paths.Length == 1 && feature.Paths[0].Length == 1 &&
            feature.Paths[0][0].X == point.X && feature.Paths[0][0].Y == point.Y
            ? fid : null;
    }
}
