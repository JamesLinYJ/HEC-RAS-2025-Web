using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ras.Backend;

namespace Ras.Backend.ServerChecks;

internal sealed record GeometryPolylineTraceRejection(string Action, string State, string? Code, string Message);

internal sealed record GeometryPolylineTraceAcceptanceReport(
    string State,
    string ManifestPath,
    string SourceManifestPath,
    string SourceProjectPath,
    string SourceGeometryPath,
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string GeometryPath,
    string VisualizationPath,
    string VisualizationRevision,
    string? ProjectSessionID,
    string? CoreLayerContextIdentityToken,
    int MapIndex,
    string MapType,
    string VectorPath,
    string VectorNodeType,
    CatalogTreeNodeLocator VectorNode,
    MapSourceFamilyCapture VectorSourceFamily,
    string MapID,
    string MapName,
    string InitialGeometryRevision,
    string FinalGeometryRevision,
    string VectorSourceSHA256,
    string VectorFamilyRevision,
    int SmartAddArcFID,
    GeometryTopologyCoordinate[] SmartAddWaypoints,
    GeometryTopologyCoordinate[] SmartAddResolvedPath,
    int MultiAnchorArcFID,
    int MultiAnchorFeatureFID,
    GeometryTopologyDeepConnectAnchor[] MultiAnchorSequence,
    GeometryTopologyCoordinate[] MultiAnchorResolvedPath,
    int LegacyArcFID,
    int LegacyFeatureFID,
    GeometryTopologyCoordinate[] LegacyResolvedPath,
    bool ExactSavedMapAndVectorSourceReadback,
    bool VectorDescriptorOwnerAndVisibilityReadback,
    bool VectorDescriptorGeometryMatchesSourceReader,
    bool SmartAddPreviewAndCaptureReadback,
    bool SmartAddWaypointOrderReadback,
    bool SmartAddSingleArcSingleMutationReadback,
    bool DeepConnectPreviewSourceAndOwnerReadback,
    bool DeepConnectThreeAnchorFoldbackOrderReadback,
    bool DeepConnectSnappedEndpointPrunedInteriorPoint,
    double DeepConnectEndpointDistanceInCells,
    bool ExpectedPathSHA256MismatchRejected,
    bool DeepConnectSingleArcSingleMutationReadback,
    bool LegacyTwoAnchorCompatibilityReadback,
    bool DraftUndoRedoReadback,
    bool HdfAndDraftUnchangedBeforeParentSave,
    bool ParentSaveChangedGeometryRevision,
    bool ReopenedAllThreeTraceArcsReadback,
    bool VectorSourceUnchanged,
    bool SourceFixtureUnchanged,
    GeometryPolylineTraceRejection[] Rejections,
    object[] Operations);

/// <summary>
/// Exercises continuous SmartAdd and persisted-vector DeepConnect through the
/// production HTTP Host. The accepted topology project is copied before the
/// runner adds an isolated GeoJSON source or edits its Geometry draft.
/// </summary>
internal static partial class ServerAcceptance
{
    public static async Task<GeometryPolylineTraceAcceptanceReport> RunGeometryPolylineTraceAsync(
        Uri baseAddress, string sourceManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Geometry polyline trace acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string manifestPath = Path.GetFullPath(sourceManifestPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("The accepted topology manifest was not found.", manifestPath);
        using JsonDocument manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        JsonElement manifest = manifestDocument.RootElement;
        string sourceProjectPath = Path.GetFullPath(ArcMapManifestString(manifest, "projectPath", "ProjectPath"));
        string sourceGeometryPath = Path.GetFullPath(ArcMapManifestString(manifest, "geometryPath", "GeometryPath"));
        if (!File.Exists(sourceProjectPath) || !Path.GetExtension(sourceProjectPath).Equals(".ras", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The topology manifest must identify its saved .ras fixture.", sourceProjectPath);
        string sourceDirectory = Path.GetDirectoryName(sourceProjectPath)
            ?? throw new IOException("The saved .ras fixture has no containing directory.");
        string sourceVisualizationPath = Path.ChangeExtension(sourceProjectPath, ".vis");
        if (!File.Exists(sourceVisualizationPath) || !File.Exists(sourceGeometryPath) ||
            !ArcMapPathIsInside(sourceGeometryPath, sourceDirectory))
            throw new InvalidDataException("The accepted manifest must identify the saved map catalog and contained Geometry HDF.");

        string sourceProjectSHA = FileSHA256(sourceProjectPath);
        string sourceVisualizationSHA = FileSHA256(sourceVisualizationPath);
        string sourceGeometrySHA = FileSHA256(sourceGeometryPath);
        string? acceptedProjectSHA = ArcMapOptionalString(manifest, "projectRevision")
            ?? ArcMapOptionalString(manifest, "sourceProjectSHA256");
        string? acceptedVisualizationSHA = ArcMapOptionalString(manifest, "visualizationRevision")
            ?? ArcMapOptionalString(manifest, "sourceVisualizationSHA256");
        string? acceptedGeometrySHA = ArcMapOptionalString(manifest, "savedGeometrySHA256")
            ?? ArcMapOptionalString(manifest, "savedRevision");
        if ((acceptedProjectSHA is not null && !acceptedProjectSHA.Equals(sourceProjectSHA, StringComparison.OrdinalIgnoreCase)) ||
            (acceptedVisualizationSHA is not null && !acceptedVisualizationSHA.Equals(sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase)) ||
            (acceptedGeometrySHA is not null && !acceptedGeometrySHA.Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase)))
            throw new SourceFileChangedException();

        SortedDictionary<string, string> sourceFixtureSnapshot = ArcMapFixtureSnapshot(sourceDirectory);
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Geometry polyline trace acceptance requires a new output directory; existing data is never overwritten.");
        if (ArcMapPathsOverlap(outputDirectory, sourceDirectory))
            throw new IOException("The isolated trace output must not overlap the accepted source fixture.");
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
            throw new InvalidDataException("The isolated copy does not preserve the accepted .ras, Geometry HDF, and .vis bytes.");

        using var timeout = new CancellationTokenSource(GeometryArcMapTimeout);
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# Host did not report ready.");

        var operations = new List<object>();
        var rejections = new List<GeometryPolylineTraceRejection>();
        OperationReceipt opened = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        operations.Add(OperationEntry("project.open", opened));
        string projectRevision = StringProperty(opened.Result, "revision");
        string projectXml = StringProperty(opened.Result, "projectXml");
        string projectXmlSHA = TextSHA256(projectXml);
        if (!IsSHA256(projectRevision) || !projectRevision.Equals(FileSHA256(projectPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("project.open did not bind the copied .ras bytes to its SHA revision.");
        CoreLayerRequestContext? coreLayerContext = PropertyOrNull(opened.Result, "coreLayerContext").ValueKind == JsonValueKind.Object
            ? Property(opened.Result, "coreLayerContext").Deserialize<CoreLayerRequestContext>(JsonOptions)
            : null;

        OperationReceipt listed = await SubmitOperationAsync(client, "map.document.list", new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            expectedProjectRevision = projectRevision,
            coreLayerContext,
        }, cancellation);
        operations.Add(OperationEntry("map.document.list", listed));
        string visualizationRevision = StringProperty(listed.Result, "visualizationRevision");
        if (!IsSHA256(visualizationRevision) || !visualizationRevision.Equals(FileSHA256(visualizationPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("map.document.list did not capture the exact copied .vis revision.");

        JsonElement[] planMaps = Property(listed.Result, "maps").EnumerateArray()
            .Where(map => StringProperty(map, "mapType") == "Plan")
            .ToArray();
        MapGeometryOwner? owner = null;
        MapTreeRequest? mapRequest = null;
        foreach (JsonElement map in planMaps)
        {
            int mapIndex = IntProperty(map, "mapIndex");
            string mapName = StringProperty(map, "mapName");
            string mapType = StringProperty(map, "mapType");
            string mapID = Guid.NewGuid().ToString("D");
            var candidateRequest = new MapTreeRequest(projectPath, projectXml, projectXmlSHA,
                mapID, mapIndex, mapName, mapType, visualizationRevision,
                ProjectSessionID: coreLayerContext?.ProjectSessionID,
                CoreLayerContext: coreLayerContext);
            OperationReceipt treeReceipt = await SubmitOperationAsync(client, "map.tree.load", candidateRequest, cancellation);
            operations.Add(OperationEntry("map.tree.load", treeReceipt));
            MapTreeSnapshot tree = JsonSerializer.Deserialize<MapTreeSnapshot>(treeReceipt.Result.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("The saved Plan map tree response was empty.");
            MapGeometryOwner? match = FindGeometryOwner(mapID, mapIndex, mapName, mapType,
                visualizationRevision, tree.Roots, candidateRequest,
                TopologyMapFixturePlanName, TopologyMapFixtureGeometryName, geometryPath);
            if (match is null) continue;
            if (owner is not null) throw new InvalidDataException("The accepted .vis catalog resolved more than one matching Plan Geometry owner.");
            owner = match;
            mapRequest = candidateRequest;
        }
        if (owner is null || mapRequest is null)
            throw new InvalidDataException("The accepted map catalog did not resolve its exact Plan CPU / Solver Geometry owner.");

        object geometryModel = new GeometryTopologyModelRequest(geometryPath, "Geometry", projectPath,
            projectXml, coreLayerContext);

        async Task<(GeometryTopologySnapshot Topology, string ContextToken, string GeometryRevision, double CellSize)> LoadMapAndTopologyAsync(
            MapGeometryOwner exactOwner, MapTreeRequest exactMapRequest)
        {
            OperationReceipt geometryReceipt = await SubmitOperationAsync(client, "map.geometry.load", new
            {
                map = exactMapRequest,
                geometryNode = exactOwner.GeometryNode,
                pageSize = 512,
                skipFlowAreasPage = true,
                skipCulvertsPage = true,
                planNodes = exactOwner.PlanNodes,
            }, cancellation);
            operations.Add(OperationEntry("map.geometry.load", geometryReceipt));
            string loadedRevision = StringProperty(geometryReceipt.Result, "geometryRevision");
            string loadedContext = StringProperty(geometryReceipt.Result, "contextToken");
            if (!PathEquals(StringProperty(geometryReceipt.Result, "geometryCanonicalPath"), geometryPath) ||
                !loadedRevision.Equals(FileSHA256(geometryPath), StringComparison.OrdinalIgnoreCase) ||
                !IsSHA256(loadedContext))
                throw new InvalidDataException("map.geometry.load did not bind the exact copied Geometry path, revision, and context.");

            OperationReceipt modelLoad = await SubmitOperationAsync(client, "model.load", geometryModel, cancellation);
            operations.Add(OperationEntry("model.load", modelLoad));
            if (!StringProperty(modelLoad.Result, "revision").Equals(loadedRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            OperationReceipt topologyReceipt = await SubmitOperationAsync(client, "topology.load",
                new GeometryTopologyLoadRequest((GeometryTopologyModelRequest)geometryModel, loadedRevision), cancellation);
            operations.Add(OperationEntry("topology.load", topologyReceipt));
            GeometryTopologySnapshot snapshot = ArcMapReadTopology(topologyReceipt.Result);
            RequireTopologyOwner(topologyReceipt.Result, loadedRevision);
            if (!snapshot.CanEdit || snapshot.Map is null || snapshot.Completion is null ||
                snapshot.SmartAddTerrainCapture is null || !snapshot.SmartAddTerrainCapture.GeometryRevision.Equals(loadedRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("topology.load omitted editable topology or its exact associated Terrain capture.");
            return (snapshot, loadedContext, loadedRevision, MapCellSize(Property(topologyReceipt.Result, "map")));
        }

        var initialOwnerLoad = await LoadMapAndTopologyAsync(owner, mapRequest);
        GeometryTopologySnapshot beforeImport = initialOwnerLoad.Topology;
        string initialGeometryRevision = initialOwnerLoad.GeometryRevision;
        double mapCellSize = initialOwnerLoad.CellSize;
        if (!double.IsFinite(mapCellSize) || mapCellSize <= 0d)
            throw new InvalidDataException("The saved Geometry map did not return a positive finite cell size.");
        GeometryTopologyExtent extent = beforeImport.Map!.Extent
            ?? throw new InvalidDataException("The saved Geometry topology did not return a finite map extent.");

        GeometryTopologyFeatureRow[] sourceNodes = beforeImport.Nodes.Rows
            .Where(row => row.X is double x && double.IsFinite(x) && row.Y is double y && double.IsFinite(y))
            .OrderBy(row => row.FID).ToArray();
        if (sourceNodes.Length < 2)
            throw new InvalidDataException("The accepted Geometry source must contain two stable topology nodes for independent vector traces.");
        GeometryTopologyFeatureRow smartAddStartNode = sourceNodes[0];
        GeometryTopologyFeatureRow primaryStartNode = sourceNodes
            .OrderBy(row => Math.Pow(row.X!.Value - extent.MinX, 2d) + Math.Pow(row.Y!.Value - extent.MinY, 2d))
            .ThenBy(row => row.FID).First();
        GeometryTopologyCoordinate primaryStart = new(primaryStartNode.X!.Value, primaryStartNode.Y!.Value);
        GeometryTopologyFeatureRow legacyStartNode = sourceNodes
            .Where(row => row.FID != primaryStartNode.FID)
            .OrderByDescending(row => Math.Pow(row.X!.Value - primaryStart.X, 2d) + Math.Pow(row.Y!.Value - primaryStart.Y, 2d))
            .ThenBy(row => row.FID).First();
        GeometryTopologyCoordinate legacyStart = new(legacyStartNode.X!.Value, legacyStartNode.Y!.Value);
        if (primaryStartNode.FID == legacyStartNode.FID)
            throw new InvalidDataException("The vector trace fixture requires two distinct source topology nodes.");

        GeometryTopologyCoordinate[] BuildExternalTrace(GeometryTopologyCoordinate start,
            GeometryTopologyCoordinate end, double expansionMultiplier)
        {
            double width = extent.MaxX - extent.MinX;
            double height = extent.MaxY - extent.MinY;
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0d || height <= 0d)
                throw new InvalidDataException("The saved map Geometry extent cannot define an isolated external line source.");
            int startSideX = Math.Abs(start.X - extent.MinX) <= Math.Abs(extent.MaxX - start.X) ? -1 : 1;
            int startSideY = Math.Abs(start.Y - extent.MinY) <= Math.Abs(extent.MaxY - start.Y) ? -1 : 1;
            int endSideX = Math.Abs(end.X - extent.MinX) <= Math.Abs(extent.MaxX - end.X) ? -1 : 1;
            int endSideY = Math.Abs(end.Y - extent.MinY) <= Math.Abs(extent.MaxY - end.Y) ? -1 : 1;
            double margin = Math.Max(width, height) * 0.18d * expansionMultiplier;
            if (mapCellSize > 0d && double.IsFinite(mapCellSize))
                margin = Math.Max(margin, mapCellSize * 12d * expansionMultiplier);
            double minX = extent.MinX - margin;
            double maxX = extent.MaxX + margin;
            double minY = extent.MinY - margin;
            double maxY = extent.MaxY + margin;
            GeometryTopologyCoordinate[] corners =
            [
                new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY),
            ];
            int CornerIndex(int sideX, int sideY) => sideX < 0 ? (sideY < 0 ? 0 : 3) : (sideY < 0 ? 1 : 2);
            int startCorner = CornerIndex(startSideX, startSideY);
            int endCorner = CornerIndex(endSideX, endSideY);
            var points = new List<GeometryTopologyCoordinate>
            {
                start,
                new(start.X + startSideX * margin, start.Y + startSideY * margin),
            };
            int corner = startCorner;
            do
            {
                corner = (corner + 1) % corners.Length;
                points.Add(corners[corner]);
            } while (corner != endCorner);
            // The last source segment approaches the target node from its
            // exterior quadrant. The two final interior anchors then reverse
            // within 2-3 map cells, exercising Host endpoint completion.
            double approach = Math.Max(1.4d * mapCellSize, Math.Max(width, height) * 0.0001d);
            double past = Math.Max(4d * mapCellSize, approach * 2d);
            points.Add(new(end.X + endSideX * past, end.Y + endSideY * past));
            points.Add(new(end.X + endSideX * approach, end.Y + endSideY * approach));
            return points.ToArray();
        }

        GeometryTopologyFeatureRow primaryTargetNode = sourceNodes
            .Where(row => row.FID != primaryStartNode.FID)
            .OrderByDescending(row => Math.Pow(row.X!.Value - primaryStart.X, 2d) + Math.Pow(row.Y!.Value - primaryStart.Y, 2d))
            .First();
        GeometryTopologyFeatureRow legacyTargetNode = sourceNodes
            .Where(row => row.FID != legacyStartNode.FID)
            .OrderByDescending(row => Math.Pow(row.X!.Value - legacyStart.X, 2d) + Math.Pow(row.Y!.Value - legacyStart.Y, 2d))
            .First();
        GeometryTopologyCoordinate primaryTarget = new(primaryTargetNode.X!.Value, primaryTargetNode.Y!.Value);
        GeometryTopologyCoordinate legacyTarget = new(legacyTargetNode.X!.Value, legacyTargetNode.Y!.Value);
        GeometryTopologyCoordinate[] primarySourcePoints = BuildExternalTrace(primaryStart, primaryTarget, 1d);
        GeometryTopologyCoordinate[] legacySourcePoints = BuildExternalTrace(legacyStart, legacyTarget, 1.65d);
        string vectorPath = Path.Combine(fixtureDirectory, "Polyline Trace Source.geojson");
        string geoJson = JsonSerializer.Serialize(new
        {
            type = "FeatureCollection",
            features = new[]
            {
                new { type = "Feature", id = "polyline-trace-primary", properties = new { purpose = "geometry-polyline-trace-acceptance" },
                    geometry = new { type = "LineString", coordinates = primarySourcePoints.Select(point => new[] { point.X, point.Y }).ToArray() } },
                new { type = "Feature", id = "polyline-trace-legacy", properties = new { purpose = "geometry-polyline-trace-acceptance-legacy" },
                    geometry = new { type = "LineString", coordinates = legacySourcePoints.Select(point => new[] { point.X, point.Y }).ToArray() } },
            },
        });
        await File.WriteAllTextAsync(vectorPath, geoJson, new UTF8Encoding(false), cancellation);
        string vectorSHA = FileSHA256(vectorPath);

        var documentMap = new MapDocumentRequest(projectPath, projectXml, projectXmlSHA,
            visualizationRevision, "Plan", MapID: owner.MapID, MapIndex: owner.MapIndex,
            MapName: owner.MapName, MapType: owner.MapType, CoreLayerContext: coreLayerContext);
        OperationReceipt imported = await SubmitOperationAsync(client, "map.vector.import", new MapVectorImportRequest(
            documentMap, vectorPath, vectorSHA, projectRevision, Path.GetFullPath(vectorPath)), cancellation);
        operations.Add(OperationEntry("map.vector.import", imported));
        if (!string.Equals(OptionalString(imported.Result, "previousVisualizationRevision"), visualizationRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The isolated GeoJSON import did not consume the exact saved .vis revision.");
        visualizationRevision = StringProperty(imported.Result, "visualizationRevision");
        if (!IsSHA256(visualizationRevision) || !visualizationRevision.Equals(FileSHA256(visualizationPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The GeoJSON import receipt does not match the copied .vis bytes.");

        async Task<(MapGeometryOwner Owner, MapTreeRequest Request, MapTreeSnapshot Tree)> ReloadMapTreeAsync(string currentVisualizationRevision)
        {
            var exactRequest = new MapTreeRequest(projectPath, projectXml, projectXmlSHA,
                owner.MapID, owner.MapIndex, owner.MapName, owner.MapType, currentVisualizationRevision,
                ProjectSessionID: coreLayerContext?.ProjectSessionID,
                CoreLayerContext: coreLayerContext);
            OperationReceipt treeReceipt = await SubmitOperationAsync(client, "map.tree.load", exactRequest, cancellation);
            operations.Add(OperationEntry("map.tree.load/refresh", treeReceipt));
            MapTreeSnapshot tree = JsonSerializer.Deserialize<MapTreeSnapshot>(treeReceipt.Result.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("The refreshed saved map tree response was empty.");
            MapGeometryOwner refreshedOwner = FindGeometryOwner(owner.MapID, owner.MapIndex, owner.MapName,
                owner.MapType, currentVisualizationRevision, tree.Roots, exactRequest,
                TopologyMapFixturePlanName, TopologyMapFixtureGeometryName, geometryPath)
                ?? throw new InvalidDataException("The Geometry owner did not survive the isolated GeoJSON import.");
            return (refreshedOwner, exactRequest, tree);
        }

        (owner, mapRequest, MapTreeSnapshot importedTree) = await ReloadMapTreeAsync(visualizationRevision);
        CatalogTreeNode[] vectorNodes = WalkNodes(importedTree.Roots).ToArray();
        CatalogTreeNode vectorNode = vectorNodes.SingleOrDefault(node => node.Locator is { NodeType: "MapLayer", MapType: "PolylinesVector" } locator &&
            ((locator.AssetID is { Length: > 0 } asset && PathEquals(asset, vectorPath)) ||
             (locator.Filename is { Length: > 0 } filename && PathEquals(filename, vectorPath))))
            ?? throw new InvalidDataException("The imported saved map tree did not expose exactly one persisted Polyline map source.");
        CatalogTreeNodeLocator vectorLocator = vectorNode.Locator!;
        if (!string.Equals(vectorNode.Checked, "checked", StringComparison.Ordinal))
            throw new InvalidDataException("The imported persisted Polyline source is not checked/visible in the saved map tree.");

        GeometryTopologyCoordinate[] sourceCoordinates(MapVectorSourceFeature feature)
        {
            if (feature.Parts.Length != 1 || feature.Parts[0].Vertices.Length < 2)
                throw new InvalidDataException("Each acceptance GeoJSON feature must resolve to one retained Polyline part.");
            return feature.Parts[0].Vertices.Select(vertex => new GeometryTopologyCoordinate(vertex.X, vertex.Y)).ToArray();
        }

        var vectorLoadRequest = new MapVectorSourceLoadRequest(mapRequest, vectorLocator, null);
        OperationReceipt vectorLoadReceipt = await SubmitOperationAsync(client, "map.vector.source.load", vectorLoadRequest, cancellation);
        operations.Add(OperationEntry("map.vector.source.load", vectorLoadReceipt));
        MapVectorSourceLoadResult vectorLoad = JsonSerializer.Deserialize<MapVectorSourceLoadResult>(
            vectorLoadReceipt.Result.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("The retained map vector source reader returned no source capture.");
        if (vectorLoad.Data.ShapeType != "Polyline" || vectorLoad.Data.Features.Length != 2 ||
            !PathEquals(vectorLoad.Source.Family.SourcePath, vectorPath) ||
            !FileSHA256(vectorPath).Equals(vectorSHA, StringComparison.OrdinalIgnoreCase) ||
            !vectorLoad.Source.Family.Files.Any(file => file.Role == "source" && file.Revision == vectorSHA))
            throw new InvalidDataException("The imported source reader did not return the exact two-feature GeoJSON line and primary-file revision.");

        string inspectRequestID = Guid.NewGuid().ToString("D");
        OperationReceipt vectorInspectReceipt = await SubmitOperationAsync(client, "topology.vectorSource.inspect", new
        {
            requestID = inspectRequestID,
            map = mapRequest,
            sources = new[] { new { node = vectorLocator, expectedFamily = vectorLoad.Source.Family } },
        }, cancellation);
        operations.Add(OperationEntry("topology.vectorSource.inspect", vectorInspectReceipt));
        JsonElement vectorInspection = vectorInspectReceipt.Result;
        JsonElement inspectedSources = Property(vectorInspection, "sources");
        JsonElement inspectedOwner = Property(vectorInspection, "owner");
        bool inspectionRequestIDMatches = OptionalString(vectorInspection, "requestID") == inspectRequestID;
        bool inspectedProjectPathMatches = OptionalString(inspectedOwner, "projectPath") is { } inspectedProjectPath &&
            PathEquals(inspectedProjectPath, projectPath);
        bool inspectedProjectRevisionMatches = string.Equals(OptionalString(inspectedOwner, "projectRevision"),
            projectRevision, StringComparison.OrdinalIgnoreCase);
        bool inspectedProjectXmlSHAMatches = string.Equals(OptionalString(inspectedOwner, "projectXmlSHA"),
            projectXmlSHA, StringComparison.OrdinalIgnoreCase);
        bool inspectedSessionMatches = OptionalString(inspectedOwner, "projectSessionID") == mapRequest.ProjectSessionID;
        bool inspectedMapIDMatches = OptionalString(inspectedOwner, "mapID") == owner.MapID;
        bool inspectedMapIndexMatches = PropertyOrNull(inspectedOwner, "mapIndex").ValueKind == JsonValueKind.Number &&
            PropertyOrNull(inspectedOwner, "mapIndex").TryGetInt32(out int inspectedMapIndex) && inspectedMapIndex == owner.MapIndex;
        bool inspectedMapNameMatches = OptionalString(inspectedOwner, "mapName") == owner.MapName;
        bool inspectedMapTypeMatches = OptionalString(inspectedOwner, "mapType") == owner.MapType;
        bool inspectedVisualizationPathMatches = OptionalString(inspectedOwner, "visualizationPath") is { } inspectedVisualizationPath &&
            PathEquals(inspectedVisualizationPath, visualizationPath);
        bool inspectedVisualizationRevisionMatches = string.Equals(OptionalString(inspectedOwner, "visualizationRevision"),
            visualizationRevision, StringComparison.OrdinalIgnoreCase);
        bool inspectedCoreLayerContextMatches = OptionalString(inspectedOwner, "coreLayerContextIdentityToken") == coreLayerContext?.IdentityToken;
        bool inspectedOneSource = inspectedSources.ValueKind == JsonValueKind.Array && inspectedSources.GetArrayLength() == 1;
        if (!inspectionRequestIDMatches || !inspectedProjectPathMatches || !inspectedProjectRevisionMatches ||
            !inspectedProjectXmlSHAMatches || !inspectedSessionMatches || !inspectedMapIDMatches ||
            !inspectedMapIndexMatches || !inspectedMapNameMatches || !inspectedMapTypeMatches ||
            !inspectedVisualizationPathMatches || !inspectedVisualizationRevisionMatches ||
            !inspectedCoreLayerContextMatches || !inspectedOneSource)
        {
            string ownerDiagnostic = JsonSerializer.Serialize(new
            {
                requestID = new { expected = inspectRequestID, actual = OptionalString(vectorInspection, "requestID"), matches = inspectionRequestIDMatches },
                expectedOwner = new
                {
                    projectPath, projectRevision, projectXmlSHA, projectSessionID = mapRequest.ProjectSessionID,
                    mapID = owner.MapID, mapIndex = owner.MapIndex, mapName = owner.MapName, mapType = owner.MapType,
                    visualizationPath, visualizationRevision,
                    coreLayerContextIdentityToken = coreLayerContext?.IdentityToken,
                },
                actualOwner = inspectedOwner.Clone(),
                ownerChecks = new
                {
                    projectPath = inspectedProjectPathMatches, projectRevision = inspectedProjectRevisionMatches,
                    projectXmlSHA = inspectedProjectXmlSHAMatches, projectSessionID = inspectedSessionMatches,
                    mapID = inspectedMapIDMatches, mapIndex = inspectedMapIndexMatches, mapName = inspectedMapNameMatches,
                    mapType = inspectedMapTypeMatches, visualizationPath = inspectedVisualizationPathMatches,
                    visualizationRevision = inspectedVisualizationRevisionMatches,
                    coreLayerContextIdentityToken = inspectedCoreLayerContextMatches, exactlyOneSource = inspectedOneSource,
                },
                sources = inspectedSources.Clone(),
            }, JsonOptions);
            throw new InvalidDataException("The read-only vector source descriptor lost its exact project/map/session/context owner: " + ownerDiagnostic);
        }

        JsonElement vectorDescriptor = inspectedSources[0];
        JsonElement componentsProperty = PropertyOrNull(vectorDescriptor, "components");
        JsonElement[] descriptorComponents = componentsProperty.ValueKind == JsonValueKind.Array
            ? componentsProperty.EnumerateArray().ToArray()
            : [];
        JsonElement unsupportedGeometryProperty = PropertyOrNull(vectorDescriptor, "unsupportedGeometryTypes");
        JsonElement oversizedProperty = PropertyOrNull(vectorDescriptor, "oversizedComponentCount");
        int oversizedComponentCount = oversizedProperty.ValueKind == JsonValueKind.Number && oversizedProperty.TryGetInt32(out int oversizedCount)
            ? oversizedCount : -1;
        string? descriptorSourcePath = OptionalString(vectorDescriptor, "canonicalSourcePath");
        string? descriptorSourceSHA = OptionalString(vectorDescriptor, "sourceSHA256");
        string? descriptorDiagnosticCode = OptionalString(vectorDescriptor, "diagnosticCode");
        string? descriptorDiagnostic = OptionalString(vectorDescriptor, "diagnostic");
        bool descriptorChecked = PropertyOrNull(vectorDescriptor, "checked").ValueKind == JsonValueKind.True;
        bool descriptorVisible = PropertyOrNull(vectorDescriptor, "effectiveVisible").ValueKind == JsonValueKind.True;
        bool descriptorCanDeepConnect = PropertyOrNull(vectorDescriptor, "canDeepConnect").ValueKind == JsonValueKind.True;
        bool descriptorPathMatches = descriptorSourcePath is not null && PathEquals(descriptorSourcePath, vectorPath);
        bool descriptorSourceSHAMatches = string.Equals(descriptorSourceSHA, vectorSHA, StringComparison.OrdinalIgnoreCase);
        bool descriptorComponentsPresent = componentsProperty.ValueKind == JsonValueKind.Array;
        bool descriptorComponentCountMatches = descriptorComponents.Length == 2;
        bool descriptorUnsupportedTypesEmpty = unsupportedGeometryProperty.ValueKind == JsonValueKind.Array &&
            unsupportedGeometryProperty.GetArrayLength() == 0;
        bool descriptorHasNoOversizedComponents = oversizedComponentCount == 0;
        bool descriptorDiagnosticCodeIsReady = descriptorDiagnosticCode == "ready";
        bool descriptorHasNoDiagnostic = descriptorDiagnostic is null;
        if (!descriptorChecked || !descriptorVisible || !descriptorCanDeepConnect || !descriptorPathMatches ||
            !descriptorSourceSHAMatches || !descriptorComponentsPresent || !descriptorComponentCountMatches ||
            !descriptorUnsupportedTypesEmpty || !descriptorHasNoOversizedComponents ||
            !descriptorDiagnosticCodeIsReady || !descriptorHasNoDiagnostic)
        {
            string descriptorDiagnosticDetails = JsonSerializer.Serialize(new
            {
                requestID = inspectRequestID,
                expectedOwner = new
                {
                    projectPath, projectRevision, projectXmlSHA, projectSessionID = mapRequest.ProjectSessionID,
                    mapID = owner.MapID, mapIndex = owner.MapIndex, mapName = owner.MapName, mapType = owner.MapType,
                    visualizationPath, visualizationRevision,
                    coreLayerContextIdentityToken = coreLayerContext?.IdentityToken,
                },
                actualOwner = inspectedOwner.Clone(),
                vectorNode = vectorLocator,
                expectedDescriptor = new
                {
                    sourceKind = "geojson", canonicalSourcePath = vectorPath, sourceSHA256 = vectorSHA,
                    sourceFamilyRevision = vectorLoad.Source.Family.Revision,
                    checkedState = true, effectiveVisible = true, canDeepConnect = true,
                    componentCount = 2, unsupportedGeometryTypes = Array.Empty<string>(), oversizedComponentCount = 0,
                    diagnosticCode = "ready", diagnostic = (string?)null,
                },
                vectorReader = new
                {
                    vectorLoad.Data.ShapeType, vectorLoad.Data.Count,
                    featureCount = vectorLoad.Data.Features.Length,
                    features = vectorLoad.Data.Features.Select(feature => new
                    {
                        feature.SourceOrdinal, feature.ShapeType, feature.SourceID,
                        parts = feature.Parts.Select(part => new
                        {
                            part.PartOrdinal,
                            vertices = part.Vertices.Select(vertex => new { vertex.X, vertex.Y, vertex.SourceX, vertex.SourceY }).ToArray(),
                        }).ToArray(),
                    }).ToArray(),
                },
                descriptorFields = new
                {
                    sourceKind = OptionalString(vectorDescriptor, "sourceKind"),
                    canonicalSourcePath = descriptorSourcePath,
                    sourceSHA256 = descriptorSourceSHA,
                    sourceFamily = PropertyOrNull(vectorDescriptor, "sourceFamily").Clone(),
                    checkedState = PropertyOrNull(vectorDescriptor, "checked").Clone(),
                    effectiveVisible = PropertyOrNull(vectorDescriptor, "effectiveVisible").Clone(),
                    canDeepConnect = PropertyOrNull(vectorDescriptor, "canDeepConnect").Clone(),
                    componentArrayPresent = descriptorComponentsPresent,
                    componentCount = descriptorComponents.Length,
                    unsupportedGeometryTypes = unsupportedGeometryProperty.Clone(),
                    oversizedComponentCount,
                    diagnosticCode = descriptorDiagnosticCode,
                    diagnostic = descriptorDiagnostic,
                    components = descriptorComponents.Select(component => new
                    {
                        component = component.Clone(),
                        featureFID = TraceOptionalInt(component, "featureFID"),
                        pathIndex = TraceOptionalInt(component, "pathIndex"),
                        componentIdentity = OptionalString(component, "componentIdentity"),
                        sourceFeatureOrdinal = TraceOptionalInt(component, "sourceFeatureOrdinal"),
                        sourcePartOrdinal = TraceOptionalInt(component, "sourcePartOrdinal"),
                        points = PropertyOrNull(component, "points").Clone(),
                    }).ToArray(),
                },
                fieldChecks = new
                {
                    checkedState = descriptorChecked, effectiveVisible = descriptorVisible,
                    canDeepConnect = descriptorCanDeepConnect, canonicalSourcePath = descriptorPathMatches,
                    sourceSHA256 = descriptorSourceSHAMatches, componentsArrayPresent = descriptorComponentsPresent,
                    componentCount = descriptorComponentCountMatches, unsupportedGeometryTypes = descriptorUnsupportedTypesEmpty,
                    oversizedComponentCount = descriptorHasNoOversizedComponents,
                    diagnosticCode = descriptorDiagnosticCodeIsReady, diagnostic = descriptorHasNoDiagnostic,
                },
            }, JsonOptions);
            throw new InvalidDataException("The Host vector descriptor did not match the exact checked, visible, supported two-component GeoJSON reader result: " + descriptorDiagnosticDetails);
        }
        MapSourceFamilyCapture descriptorFamily = Property(vectorDescriptor, "sourceFamily").Deserialize<MapSourceFamilyCapture>(JsonOptions)
            ?? throw new InvalidDataException("The vector descriptor omitted its source-family capture.");
        if (!descriptorFamily.Revision.Equals(vectorLoad.Source.Family.Revision, StringComparison.OrdinalIgnoreCase) ||
            !PathEquals(descriptorFamily.SourcePath, vectorPath))
            throw new InvalidDataException("The vector descriptor and retained vector reader disagree on the exact source family.");

        JsonElement DescriptorForFeature(int featureFID) => descriptorComponents.SingleOrDefault(component =>
            IntProperty(component, "featureFID") == featureFID);

        GeometryTopologyCoordinate[] DescriptorPoints(JsonElement component) => Property(component, "points").EnumerateArray()
            .Select(point => new GeometryTopologyCoordinate(Property(point, "x").GetDouble(), Property(point, "y").GetDouble()))
            .ToArray();

        for (int index = 0; index < vectorLoad.Data.Features.Length; index++)
        {
            JsonElement component = DescriptorForFeature(index);
            if (component.ValueKind != JsonValueKind.Object || IntProperty(component, "pathIndex") != 0 ||
                IntProperty(component, "sourceFeatureOrdinal") != index || IntProperty(component, "sourcePartOrdinal") != 0 ||
                StringProperty(component, "componentIdentity") != $"feature:{index}:part:0" ||
                !TraceCoordinateSequencesEqual(DescriptorPoints(component), sourceCoordinates(vectorLoad.Data.Features[index])))
                throw new InvalidDataException("topology.vectorSource.inspect did not preserve the exact line-reader ordinal, component identity, or vertex sequence.");
        }

        var postImportOwnerLoad = await LoadMapAndTopologyAsync(owner, mapRequest);
        GeometryTopologySnapshot current = postImportOwnerLoad.Topology;
        string contextToken = postImportOwnerLoad.ContextToken;
        string geometryRevision = postImportOwnerLoad.GeometryRevision;
        if (!geometryRevision.Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase) ||
            !ArcMapMapFeaturesEqual(beforeImport.Map!.Features, current.Map!.Features))
            throw new InvalidDataException("Adding an external vector source changed the Geometry HDF topology or context unexpectedly.");

        GeometryTopologySmartAddTerrainCapture terrainCapture = current.SmartAddTerrainCapture!
            ?? throw new InvalidDataException("The current topology omitted its source Terrain capture.");
        if (!PathEquals(terrainCapture.GeometryAssetPath, geometryPath) ||
            !PathEquals(terrainCapture.ProjectPath, projectPath) ||
            !terrainCapture.GeometryRevision.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) ||
            !IsSHA256(terrainCapture.SourceRevision))
            throw new InvalidDataException("The SmartAdd terrain capture is not bound to this exact project and Geometry source.");
        double mapWidth = extent.MaxX - extent.MinX;
        double mapHeight = extent.MaxY - extent.MinY;
        if (!double.IsFinite(mapWidth) || !double.IsFinite(mapHeight) || mapWidth <= 0d || mapHeight <= 0d)
            throw new InvalidDataException("The saved Geometry map extent cannot define SmartAdd waypoints.");
        GeometryTopologyCoordinate[] waypointCandidates =
        [
            new(extent.MinX + mapWidth * 0.79d, extent.MinY + mapHeight * 0.31d),
            new(extent.MinX + mapWidth * 0.23d, extent.MinY + mapHeight * 0.81d),
            new(extent.MinX + mapWidth * 0.77d, extent.MinY + mapHeight * 0.73d),
            new(extent.MinX + mapWidth * 0.21d, extent.MinY + mapHeight * 0.27d),
        ];
        GeometryTopologyCoordinate smartAddStart = new(smartAddStartNode.X!.Value, smartAddStartNode.Y!.Value);
        GeometryTopologyCoordinate smartAddEnd = waypointCandidates.OrderByDescending(point =>
            Math.Pow(point.X - smartAddStart.X, 2d) + Math.Pow(point.Y - smartAddStart.Y, 2d)).First();
        GeometryTopologyCoordinate smartAddMiddle = waypointCandidates.Where(point => point != smartAddEnd)
            .OrderByDescending(point => Math.Abs((smartAddEnd.X - smartAddStart.X) * (point.Y - smartAddStart.Y) -
                (smartAddEnd.Y - smartAddStart.Y) * (point.X - smartAddStart.X)))
            .First();
        double smartAddDistance = Math.Sqrt(Math.Pow(smartAddEnd.X - smartAddStart.X, 2d) +
            Math.Pow(smartAddEnd.Y - smartAddStart.Y, 2d));
        if (smartAddDistance < mapCellSize * 12d)
            throw new InvalidDataException("The saved map extent cannot provide a measurable HighGround SmartAdd path.");
        GeometryTopologyCoordinate[] smartAddWaypoints = [smartAddStart, smartAddMiddle, smartAddEnd];
        var rasterWindow = new GeometryTopologySmartAddWindow(extent, 512, 512);
        var smartAddOptions = new GeometryTopologySmartAddOptions(
            Algorithm: "HighGround", DpTolerancePixels: 0d, MaxSegmentPixels: 1000d,
            HighGroundSnapRadiusCells: 0);

        GeometryTopologyMapRequestContext NewMapContext(GeometryTopologySnapshot snapshot)
        {
            string draftIdentity = snapshot.Completion?.DraftIdentity
                ?? throw new InvalidDataException("The current topology snapshot has no draft identity.");
            return new GeometryTopologyMapRequestContext(Guid.NewGuid().ToString("D"), owner.MapID,
                contextToken, geometryRevision, draftIdentity);
        }

        JsonElement lastPreviewEnvelope = default;
        async Task<GeometryTopologyMapCommandPreviewResponse> PreviewAsync(GeometryTopologySnapshot snapshot,
            GeometryTopologyMapCommand command, string action)
        {
            GeometryTopologyMapRequestContext mapContext = NewMapContext(snapshot);
            var request = new GeometryTopologyEditRequest((GeometryTopologyModelRequest)geometryModel,
                snapshot.Revision, snapshot.DraftState,
                new GeometryTopologyMutation("mapCommand", "map", []) { MapCommand = command }, mapContext);
            OperationReceipt previewReceipt = await SubmitOperationAsync(client, "topology.mapCommand.preview", request, cancellation);
            operations.Add(OperationEntry("topology.mapCommand.preview/" + action, previewReceipt));
            lastPreviewEnvelope = previewReceipt.Result.Clone();
            GeometryTopologyMapCommandPreviewResponse preview = JsonSerializer.Deserialize<GeometryTopologyMapCommandPreviewResponse>(
                previewReceipt.Result.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException(action + " preview returned no path capture.");
            if (preview.RequestID != mapContext.RequestID || preview.MapID != owner.MapID ||
                preview.ContextToken != contextToken || !preview.GeometryRevision.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) ||
                !preview.Revision.Equals(snapshot.Revision, StringComparison.OrdinalIgnoreCase) ||
                preview.DraftIdentity != mapContext.ExpectedDraftIdentity || preview.Action != action ||
                preview.Coordinates.Length < 2 || !preview.Coordinates.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y)) ||
                !IsSHA256(preview.PathSHA256))
                throw new InvalidDataException(action + " preview did not echo its exact map, draft, source, or resolved path identity.");
            return preview;
        }

        async Task<GeometryTopologySnapshot> ApplyMapCommandAsync(GeometryTopologySnapshot snapshot,
            GeometryTopologyMapCommand command, string action)
        {
            GeometryTopologyMapRequestContext mapContext = NewMapContext(snapshot);
            var request = new GeometryTopologyEditRequest((GeometryTopologyModelRequest)geometryModel,
                snapshot.Revision, snapshot.DraftState,
                new GeometryTopologyMutation("mapCommand", "map", []) { MapCommand = command }, mapContext);
            OperationReceipt applied = await SubmitOperationAsync(client, "topology.edit", request, cancellation);
            operations.Add(OperationEntry("topology.edit/" + action, applied));
            GeometryTopologySnapshot next = ArcMapReadTopology(applied.Result);
            RequireTopologyOwner(applied.Result, geometryRevision);
            if (next.Completion?.SourceRevision is not string source ||
                !source.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) ||
                next.MapRequestContext is not { } echoed || echoed.RequestID != mapContext.RequestID ||
                echoed.MapID != owner.MapID || echoed.ContextToken != contextToken ||
                !echoed.GeometryRevision.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) ||
                echoed.ExpectedDraftIdentity != mapContext.ExpectedDraftIdentity)
                throw new InvalidDataException(action + " edit did not echo the exact map owner, source revision, and prior draft identity.");
            current = next;
            return next;
        }

        async Task<GeometryPolylineTraceRejection> ExpectMapCommandRejectedAsync(
            GeometryTopologySnapshot snapshot, GeometryTopologyMapCommand command, string action)
        {
            GeometryTopologyMapRequestContext mapContext = NewMapContext(snapshot);
            var request = new GeometryTopologyEditRequest((GeometryTopologyModelRequest)geometryModel,
                snapshot.Revision, snapshot.DraftState,
                new GeometryTopologyMutation("mapCommand", "map", []) { MapCommand = command }, mapContext);
            using HttpResponseMessage response = await client.PostAsJsonAsync("api/operations",
                new { method = "topology.edit", parameters = request }, JsonOptions, cancellation);
            string body = await response.Content.ReadAsStringAsync(cancellation);
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                if ((int)response.StatusCode is >= 400 and < 500)
                {
                    var direct = new GeometryPolylineTraceRejection(action, "rejected", response.StatusCode.ToString(), body);
                    rejections.Add(direct);
                    return direct;
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
                if (!IsTerminal(state))
                {
                    await Task.Delay(100, cancellation);
                    continue;
                }
                if (!state.Equals("failed", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(action + " with a mismatched path SHA was accepted by the Host.");
                JsonElement error = PropertyOrNull(root, "error");
                var failed = new GeometryPolylineTraceRejection(action, state,
                    OptionalString(error, "code"), OptionalString(error, "message") ?? string.Empty);
                if (string.IsNullOrWhiteSpace(failed.Message))
                    throw new InvalidDataException(action + " rejection omitted its Host error.");
                rejections.Add(failed);
                operations.Add(new { method = "topology.edit/reject/" + action, id = operationID, state, failed.Code });
                return failed;
            }
        }

        var smartCommand = new GeometryTopologyMapCommand("smartAdd", "arc", [],
            Coordinates: smartAddWaypoints, MapCellSize: mapCellSize,
            SmartAddSource: new GeometryTopologySmartAddSource(terrainCapture, rasterWindow, smartAddOptions))
            { StartNodeFID = smartAddStartNode.FID };
        GeometryTopologyMapCommandPreviewResponse smartPreview = await PreviewAsync(current, smartCommand, "smartAdd");
        bool smartAddWaypointOrderReadback = TraceContainsWaypointsInOrder(
            smartPreview.Coordinates, smartAddWaypoints.Take(2).ToArray());
        if (smartPreview.SmartAddTerrainCapture != terrainCapture ||
            !smartPreview.SourceRevision.Equals(terrainCapture.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
            !smartAddWaypointOrderReadback)
            throw new InvalidDataException("The SmartAdd preview did not retain its captured start/intermediate waypoints and exact Terrain source.");
        bool smartPreviewReadback = smartPreview.DraftIdentity == current.Completion!.DraftIdentity &&
            smartPreview.Revision.Equals(current.Revision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(geometryPath).Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase) &&
            current.DraftState == beforeImport.DraftState;
        if (!smartPreviewReadback) throw new InvalidDataException("SmartAdd preview changed the saved HDF or topology draft.");

        string invalidHash = smartPreview.PathSHA256.All(character => character == '0')
            ? new string('1', 64) : new string('0', 64);
        GeometryPolylineTraceRejection smartHashRejection = await ExpectMapCommandRejectedAsync(current,
            smartCommand with { ExpectedPathSHA256 = invalidHash }, "smartAdd.expectedPathSHA256Mismatch");
        if ((!smartHashRejection.State.Equals("failed", StringComparison.OrdinalIgnoreCase) &&
            !smartHashRejection.State.Equals("rejected", StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(smartHashRejection.Code, "sourceChanged", StringComparison.OrdinalIgnoreCase) ||
            current.DraftState != beforeImport.DraftState ||
            !FileSHA256(geometryPath).Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The stale SmartAdd path hash did not reject before changing the draft.");

        GeometryTopologySnapshot smartAdded = await ApplyMapCommandAsync(current,
            smartCommand with { ExpectedPathSHA256 = smartPreview.PathSHA256 }, "smartAdd");
        HashSet<int> initialArcIDs = beforeImport.Arcs.Rows.Select(row => row.FID).ToHashSet();
        int[] smartNewArcIDs = smartAdded.Arcs.Rows.Where(row => !initialArcIDs.Contains(row.FID)).Select(row => row.FID).ToArray();
        if (smartNewArcIDs.Length != 1)
            throw new InvalidDataException("A three-waypoint SmartAdd did not append exactly one source Geometry arc.");
        int smartAddArcFID = smartNewArcIDs[0];
        GeometryTopologyCoordinate[] smartAddedPath = ArcMapPath(ArcMapFeatures(smartAdded, "arc")
            .Single(feature => feature.FID == smartAddArcFID));
        bool smartMutationShape = TraceMutationCount(smartAdded.DraftState) == 1 &&
            TraceLastMutationAction(smartAdded.DraftState) == "smartAdd" &&
            TraceCoordinateSequencesEqual(smartAddedPath, smartPreview.Coordinates);
        if (!smartMutationShape)
            throw new InvalidDataException("SmartAdd did not preserve its resolved preview path as exactly one replayable map-command mutation and one arc.");

        OperationReceipt undoSmart = await SubmitOperationAsync(client, "topology.edit", new GeometryTopologyEditRequest(
            (GeometryTopologyModelRequest)geometryModel, smartAdded.Revision, smartAdded.DraftState,
            new GeometryTopologyMutation("undo", "history", [])), cancellation);
        operations.Add(OperationEntry("topology.edit/undoSmartAdd", undoSmart));
        GeometryTopologySnapshot smartUndone = ArcMapReadTopology(undoSmart.Result);
        bool smartUndoReadback = ArcMapTopologySignature(smartUndone) == ArcMapTopologySignature(beforeImport) &&
            !smartUndone.CanUndo && smartUndone.CanRedo;
        if (!smartUndoReadback) throw new InvalidDataException("Undo did not remove the single SmartAdd draft mutation.");
        OperationReceipt redoSmart = await SubmitOperationAsync(client, "topology.edit", new GeometryTopologyEditRequest(
            (GeometryTopologyModelRequest)geometryModel, smartUndone.Revision, smartUndone.DraftState,
            new GeometryTopologyMutation("redo", "history", [])), cancellation);
        operations.Add(OperationEntry("topology.edit/redoSmartAdd", redoSmart));
        current = ArcMapReadTopology(redoSmart.Result);
        if (ArcMapTopologySignature(current) != ArcMapTopologySignature(smartAdded) ||
            !current.Arcs.Rows.Any(row => row.FID == smartAddArcFID))
            throw new InvalidDataException("Redo did not restore the same SmartAdd arc and ordered waypoints.");

        JsonElement primaryComponent = DescriptorForFeature(0);
        JsonElement legacyComponent = DescriptorForFeature(1);
        GeometryTopologyCoordinate[] multiSourcePoints = DescriptorPoints(primaryComponent);
        GeometryTopologyCoordinate[] legacySourceCoordinates = DescriptorPoints(legacyComponent);
        string canonicalVectorPath = StringProperty(vectorDescriptor, "canonicalSourcePath");
        GeometryTopologyDeepConnectAnchor[] multiAnchors =
        [
            new(0, 0d),
            new(multiSourcePoints.Length - 2, 0.95d),
            new(multiSourcePoints.Length - 2, 0.75d),
        ];
        GeometryTopologyDeepConnectSource DeepSource(int featureFID, JsonElement component,
            GeometryTopologyCoordinate[] points, GeometryTopologyFeatureRow startNode,
            GeometryTopologyDeepConnectAnchor[]? anchors)
        {
            if (points.Length < 2 || points[0].X != startNode.X || points[0].Y != startNode.Y)
                throw new InvalidDataException("A persisted vector trace must start at the exact selected source node coordinate.");
            GeometryTopologyDeepConnectAnchor first = anchors is { Length: >= 2 }
                ? anchors[0] : new GeometryTopologyDeepConnectAnchor(0, 0d);
            GeometryTopologyDeepConnectAnchor last = anchors is { Length: >= 2 }
                ? anchors[^1] : new GeometryTopologyDeepConnectAnchor(points.Length - 2, 0.75d);
            return new GeometryTopologyDeepConnectSource("vector", canonicalVectorPath, vectorSHA,
                featureFID, 0, StringProperty(component, "componentIdentity"), points,
                first.SegmentIndex, first.Fraction, last.SegmentIndex, last.Fraction,
                new GeometryTopologyDeepConnectVectorSource(mapRequest, vectorLocator, descriptorFamily))
            { Anchors = anchors };
        }
        GeometryTopologyMapCommand multiDeepCommand = new("deepConnect", "arc", [], MapCellSize: mapCellSize,
            DeepConnectSource: DeepSource(0, primaryComponent, multiSourcePoints, primaryStartNode, multiAnchors))
        { StartNodeFID = primaryStartNode.FID };
        GeometryTopologyMapCommandPreviewResponse deepPreview = await PreviewAsync(current, multiDeepCommand, "deepConnect");
        GeometryTopologyCoordinate[] expectedMultiPath = TraceBuildAnchoredPath(multiSourcePoints, multiAnchors);
        double endpointDistanceInCells = Math.Sqrt(
            Math.Pow(expectedMultiPath[^1].X - primaryTarget.X, 2d) +
            Math.Pow(expectedMultiPath[^1].Y - primaryTarget.Y, 2d)) / mapCellSize;
        double lastInteriorPointDistanceInCells = Math.Sqrt(
            Math.Pow(expectedMultiPath[^2].X - primaryTarget.X, 2d) +
            Math.Pow(expectedMultiPath[^2].Y - primaryTarget.Y, 2d)) / mapCellSize;
        JsonElement deepPreviewEnvelope = lastPreviewEnvelope;
        int? previewConnectionFID = TraceOptionalInt(deepPreviewEnvelope, "connectionArcFID");
        int? previewStartNodeFID = TraceOptionalInt(deepPreviewEnvelope, "startNodeFID");
        int? previewEndNodeFID = TraceOptionalInt(deepPreviewEnvelope, "endNodeFID");
        GeometryTopologyCoordinate[] expectedFinalMultiPath = TraceBuildSnappedEndpointPath(
            expectedMultiPath, primaryTarget, 10d * mapCellSize);
        bool snappedInteriorPointPruned = expectedFinalMultiPath.Length == expectedMultiPath.Length - 1 &&
            lastInteriorPointDistanceInCells <= 10d;
        bool deepPreviewReadback = deepPreview.DeepConnectSource is not null &&
            deepPreview.DeepConnectSource.SourceRevision.Equals(vectorSHA, StringComparison.OrdinalIgnoreCase) &&
            deepPreview.DeepConnectSource.FeatureFID == 0 && deepPreview.DeepConnectSource.PathIndex == 0 &&
            deepPreview.DeepConnectSource.ComponentIdentity == StringProperty(primaryComponent, "componentIdentity") &&
            deepPreview.SourceRevision.Equals(vectorSHA, StringComparison.OrdinalIgnoreCase) &&
            TraceCoordinateSequencesEqual(deepPreview.Coordinates, expectedFinalMultiPath) &&
            endpointDistanceInCells is >= 2d and <= 3d &&
            snappedInteriorPointPruned &&
            previewConnectionFID is not null && previewStartNodeFID == primaryStartNode.FID &&
            previewEndNodeFID == primaryTargetNode.FID &&
            IsSHA256(deepPreview.PathSHA256) &&
            FileSHA256(geometryPath).Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase) &&
            current.DraftState == smartAdded.DraftState;
        if (!deepPreviewReadback)
            throw new InvalidDataException("DeepConnect preview did not preserve its exact vector source, draft, and multi-anchor path.");
        bool foldbackReadback = deepPreview.Coordinates.Length > 4 &&
            TraceCoordinateSequencesEqual(deepPreview.Coordinates, expectedFinalMultiPath) &&
            TraceHasDirectionReversal(expectedMultiPath);
        if (!foldbackReadback)
            throw new InvalidDataException("The three-anchor DeepConnect path did not preserve source segment order through its foldback.");

        GeometryPolylineTraceRejection deepHashRejection = await ExpectMapCommandRejectedAsync(current,
            multiDeepCommand with { ExpectedPathSHA256 = invalidHash }, "deepConnect.expectedPathSHA256Mismatch");
        if ((!deepHashRejection.State.Equals("failed", StringComparison.OrdinalIgnoreCase) &&
            !deepHashRejection.State.Equals("rejected", StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(deepHashRejection.Code, "sourceChanged", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DeepConnect accepted an ExpectedPathSHA256 value that differs from its preview.");
        if (current.DraftState != smartAdded.DraftState || !FileSHA256(geometryPath).Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A mismatched DeepConnect path hash changed the draft or saved HDF.");

        GeometryTopologySnapshot deepConnected = await ApplyMapCommandAsync(current,
            multiDeepCommand with { ExpectedPathSHA256 = deepPreview.PathSHA256 }, "deepConnect.multiAnchor");
        int[] afterMultiNewArcIDs = deepConnected.Arcs.Rows.Where(row => !smartAdded.Arcs.Rows.Any(previous => previous.FID == row.FID))
            .Select(row => row.FID).ToArray();
        if (afterMultiNewArcIDs.Length != 1) throw new InvalidDataException("The three-anchor vector DeepConnect did not create exactly one new Geometry arc.");
        int multiAnchorArcFID = afterMultiNewArcIDs[0];
        if (previewConnectionFID != multiAnchorArcFID)
            throw new InvalidDataException("The DeepConnect preview connection FID does not match the actual appended Geometry arc.");
        GeometryTopologyFeatureRow multiArcRow = deepConnected.Arcs.Rows.Single(row => row.FID == multiAnchorArcFID);
        if (multiArcRow.StartNodeID != primaryStartNode.FID || multiArcRow.EndNodeID != primaryTargetNode.FID)
            throw new InvalidDataException("The actual DeepConnect arc did not retain the previewed existing-node snap endpoints.");
        GeometryTopologyCoordinate[] multiAnchorPath = ArcMapPath(ArcMapFeatures(deepConnected, "arc")
            .Single(feature => feature.FID == multiAnchorArcFID));
        bool multiMutationShape = TraceMutationCount(deepConnected.DraftState) == 2 &&
            TraceLastMutationAction(deepConnected.DraftState) == "deepConnect" &&
            TraceCoordinateSequencesEqual(multiAnchorPath, deepPreview.Coordinates);
        if (!multiMutationShape)
            throw new InvalidDataException("The multi-anchor DeepConnect edit did not retain the preview sequence as one map-command mutation and one new arc.");

        GeometryTopologyDeepConnectSource legacySource = DeepSource(1, legacyComponent, legacySourceCoordinates,
            legacyStartNode, anchors: null);
        GeometryTopologyMapCommand legacyCommand = new("deepConnect", "arc", [], MapCellSize: mapCellSize,
            DeepConnectSource: legacySource)
        { StartNodeFID = legacyStartNode.FID };
        GeometryTopologyMapCommandPreviewResponse legacyPreview = await PreviewAsync(deepConnected, legacyCommand, "deepConnect");
        GeometryTopologyCoordinate[] expectedLegacyPath = TraceBuildAnchoredPath(legacySourceCoordinates,
        [
            new GeometryTopologyDeepConnectAnchor(0, 0d),
            new GeometryTopologyDeepConnectAnchor(legacySourceCoordinates.Length - 2, 0.75d),
        ]);
        GeometryTopologyCoordinate[] expectedFinalLegacyPath = TraceBuildSnappedEndpointPath(
            expectedLegacyPath, legacyTarget, 10d * mapCellSize);
        JsonElement legacyPreviewEnvelope = lastPreviewEnvelope;
        int? legacyPreviewConnectionFID = TraceOptionalInt(legacyPreviewEnvelope, "connectionArcFID");
        int? legacyPreviewStartNodeFID = TraceOptionalInt(legacyPreviewEnvelope, "startNodeFID");
        int? legacyPreviewEndNodeFID = TraceOptionalInt(legacyPreviewEnvelope, "endNodeFID");
        if (!TraceCoordinateSequencesEqual(legacyPreview.Coordinates, expectedFinalLegacyPath) ||
            legacyPreview.DeepConnectSource?.Anchors is not null ||
            !legacyPreview.SourceRevision.Equals(vectorSHA, StringComparison.OrdinalIgnoreCase) ||
            legacyPreviewConnectionFID is null ||
            legacyPreviewStartNodeFID != legacyStartNode.FID || legacyPreviewEndNodeFID != legacyTargetNode.FID)
            throw new InvalidDataException("The legacy two-anchor DeepConnect wire shape did not resolve its original start/end segment contract.");
        GeometryTopologySnapshot legacyConnected = await ApplyMapCommandAsync(deepConnected,
            legacyCommand with { ExpectedPathSHA256 = legacyPreview.PathSHA256 }, "deepConnect.legacyTwoAnchor");
        int[] afterLegacyArcIDs = legacyConnected.Arcs.Rows.Where(row => !deepConnected.Arcs.Rows.Any(previous => previous.FID == row.FID))
            .Select(row => row.FID).ToArray();
        if (afterLegacyArcIDs.Length != 1 || TraceMutationCount(legacyConnected.DraftState) != 3 ||
            TraceLastMutationAction(legacyConnected.DraftState) != "deepConnect")
            throw new InvalidDataException("The legacy two-anchor request did not remain a single compatible journal mutation and arc.");
        int legacyArcFID = afterLegacyArcIDs[0];
        if (legacyPreviewConnectionFID != legacyArcFID)
            throw new InvalidDataException("The legacy DeepConnect preview connection FID does not match the actual appended Geometry arc.");
        GeometryTopologyFeatureRow legacyArcRow = legacyConnected.Arcs.Rows.Single(row => row.FID == legacyArcFID);
        if (legacyArcRow.StartNodeID != legacyStartNode.FID || legacyArcRow.EndNodeID != legacyTargetNode.FID)
            throw new InvalidDataException("The actual legacy DeepConnect arc did not retain its previewed endpoint snaps.");
        GeometryTopologyCoordinate[] legacyPath = ArcMapPath(ArcMapFeatures(legacyConnected, "arc")
            .Single(feature => feature.FID == legacyArcFID));
        if (!TraceCoordinateSequencesEqual(legacyPath, legacyPreview.Coordinates))
            throw new InvalidDataException("The legacy two-anchor edit changed its resolved source path.");

        OperationReceipt undoDeep = await SubmitOperationAsync(client, "topology.edit", new GeometryTopologyEditRequest(
            (GeometryTopologyModelRequest)geometryModel, legacyConnected.Revision, legacyConnected.DraftState,
            new GeometryTopologyMutation("undo", "history", [])), cancellation);
        operations.Add(OperationEntry("topology.edit/undoLegacyDeepConnect", undoDeep));
        GeometryTopologySnapshot undone = ArcMapReadTopology(undoDeep.Result);
        bool undoReadback = ArcMapTopologySignature(undone) == ArcMapTopologySignature(deepConnected) && undone.CanRedo;
        if (!undoReadback) throw new InvalidDataException("Undo did not restore the exact multi-anchor DeepConnect draft after the legacy command.");
        OperationReceipt redoDeep = await SubmitOperationAsync(client, "topology.edit", new GeometryTopologyEditRequest(
            (GeometryTopologyModelRequest)geometryModel, undone.Revision, undone.DraftState,
            new GeometryTopologyMutation("redo", "history", [])), cancellation);
        operations.Add(OperationEntry("topology.edit/redoLegacyDeepConnect", redoDeep));
        current = ArcMapReadTopology(redoDeep.Result);
        bool redoReadback = ArcMapTopologySignature(current) == ArcMapTopologySignature(legacyConnected) &&
            ArcMapFeatures(current, "arc").Any(feature => feature.FID == smartAddArcFID) &&
            ArcMapFeatures(current, "arc").Any(feature => feature.FID == multiAnchorArcFID) &&
            ArcMapFeatures(current, "arc").Any(feature => feature.FID == legacyArcFID);
        if (!redoReadback) throw new InvalidDataException("Redo did not restore all three ordered map-command arcs.");

        bool sourcesUnchangedBeforeSave = FileSHA256(geometryPath).Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(vectorPath).Equals(vectorSHA, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase);
        if (!sourcesUnchangedBeforeSave)
            throw new InvalidDataException("Topology draft edits changed the Geometry HDF, project XML, GeoJSON, or map catalog before parent save.");

        OperationReceipt finalModelLoad = await SubmitOperationAsync(client, "model.load", geometryModel, cancellation);
        operations.Add(OperationEntry("model.load/before-parent-save", finalModelLoad));
        if (!StringProperty(finalModelLoad.Result, "revision").Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        OperationReceipt save = await SubmitOperationAsync(client, "model.save", new
        {
            assetPath = geometryPath,
            kind = "Geometry",
            projectPath,
            revision = initialGeometryRevision,
            fields = new Dictionary<string, string>(),
            images = Property(finalModelLoad.Result, "images"),
            projectXml,
            sourceState = OptionalString(finalModelLoad.Result, "sourceState"),
            geometryTopologyState = current.DraftState,
            coreLayerContext,
        }, cancellation);
        operations.Add(OperationEntry("model.save/parent", save));
        string finalGeometryRevision = StringProperty(save.Result, "revision");
        bool parentSaveChangedRevision = IsSHA256(finalGeometryRevision) &&
            !finalGeometryRevision.Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(geometryPath).Equals(finalGeometryRevision, StringComparison.OrdinalIgnoreCase);
        if (!parentSaveChangedRevision)
            throw new InvalidDataException("The parent model.save did not publish the three trace arcs to the copied Geometry HDF.");

        OperationReceipt reopenedProject = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        string reopenedProjectXml = StringProperty(reopenedProject.Result, "projectXml");
        if (!StringProperty(reopenedProject.Result, "revision").Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            reopenedProjectXml != projectXml || !FileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !FileSHA256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Parent save changed the project XML or persisted map catalog unexpectedly.");
        var reopenedModel = new GeometryTopologyModelRequest(geometryPath, "Geometry", projectPath,
            reopenedProjectXml, coreLayerContext);
        OperationReceipt reopenedLoad = await SubmitOperationAsync(client, "model.load", reopenedModel, cancellation);
        operations.Add(OperationEntry("model.load/reopen", reopenedLoad));
        OperationReceipt reopenedTopologyReceipt = await SubmitOperationAsync(client, "topology.load",
            new GeometryTopologyLoadRequest(reopenedModel, finalGeometryRevision), cancellation);
        operations.Add(OperationEntry("topology.load/reopen", reopenedTopologyReceipt));
        GeometryTopologySnapshot reopenedTopology = ArcMapReadTopology(reopenedTopologyReceipt.Result);
        RequireTopologyOwner(reopenedTopologyReceipt.Result, finalGeometryRevision);
        bool reopenedReadback = !reopenedTopology.IsDirty &&
            ArcMapFeatures(reopenedTopology, "arc").Any(feature => feature.FID == smartAddArcFID &&
                TraceCoordinateSequencesEqual(ArcMapPath(feature), smartPreview.Coordinates)) &&
            ArcMapFeatures(reopenedTopology, "arc").Any(feature => feature.FID == multiAnchorArcFID &&
                TraceCoordinateSequencesEqual(ArcMapPath(feature), deepPreview.Coordinates)) &&
            ArcMapFeatures(reopenedTopology, "arc").Any(feature => feature.FID == legacyArcFID &&
                TraceCoordinateSequencesEqual(ArcMapPath(feature), legacyPreview.Coordinates));
        if (!reopenedReadback)
            throw new InvalidDataException("Reopening the parent-saved Geometry HDF did not recover the SmartAdd, multi-anchor, and legacy two-anchor traces.");

        bool originalFixtureUnchanged = ArcMapFixtureSnapshot(sourceDirectory).SequenceEqual(sourceFixtureSnapshot) &&
            FileSHA256(sourceProjectPath).Equals(sourceProjectSHA, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(sourceGeometryPath).Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(sourceVisualizationPath).Equals(sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase);
        if (!originalFixtureUnchanged) throw new InvalidDataException("The runner modified the accepted source fixture instead of its isolated copy.");
        bool vectorSourceUnchanged = FileSHA256(vectorPath).Equals(vectorSHA, StringComparison.OrdinalIgnoreCase);
        if (!vectorSourceUnchanged) throw new InvalidDataException("The topology edit changed its external persisted GeoJSON source.");

        string outputManifestPath = Path.Combine(outputDirectory, "geometry-polyline-trace-acceptance.json");
        var report = new GeometryPolylineTraceAcceptanceReport(
            State: "passed",
            ManifestPath: outputManifestPath,
            SourceManifestPath: manifestPath,
            SourceProjectPath: sourceProjectPath,
            SourceGeometryPath: sourceGeometryPath,
            ProjectPath: projectPath,
            ProjectRevision: projectRevision,
            ProjectXmlSHA: projectXmlSHA,
            GeometryPath: geometryPath,
            VisualizationPath: visualizationPath,
            VisualizationRevision: visualizationRevision,
            ProjectSessionID: mapRequest.ProjectSessionID,
            CoreLayerContextIdentityToken: coreLayerContext?.IdentityToken,
            MapIndex: owner.MapIndex,
            MapType: owner.MapType,
            VectorPath: vectorPath,
            VectorNodeType: vectorLocator.NodeType,
            VectorNode: vectorLocator,
            VectorSourceFamily: descriptorFamily,
            MapID: owner.MapID,
            MapName: owner.MapName,
            InitialGeometryRevision: initialGeometryRevision,
            FinalGeometryRevision: finalGeometryRevision,
            VectorSourceSHA256: vectorSHA,
            VectorFamilyRevision: descriptorFamily.Revision,
            SmartAddArcFID: smartAddArcFID,
            SmartAddWaypoints: smartAddWaypoints,
            SmartAddResolvedPath: smartPreview.Coordinates,
            MultiAnchorArcFID: multiAnchorArcFID,
            MultiAnchorFeatureFID: 0,
            MultiAnchorSequence: multiAnchors,
            MultiAnchorResolvedPath: deepPreview.Coordinates,
            LegacyArcFID: legacyArcFID,
            LegacyFeatureFID: 1,
            LegacyResolvedPath: legacyPreview.Coordinates,
            ExactSavedMapAndVectorSourceReadback: true,
            VectorDescriptorOwnerAndVisibilityReadback: true,
            VectorDescriptorGeometryMatchesSourceReader: true,
            SmartAddPreviewAndCaptureReadback: smartPreviewReadback,
            SmartAddWaypointOrderReadback: smartAddWaypointOrderReadback,
            SmartAddSingleArcSingleMutationReadback: smartMutationShape,
            DeepConnectPreviewSourceAndOwnerReadback: deepPreviewReadback,
            DeepConnectThreeAnchorFoldbackOrderReadback: foldbackReadback,
            DeepConnectSnappedEndpointPrunedInteriorPoint: snappedInteriorPointPruned,
            DeepConnectEndpointDistanceInCells: endpointDistanceInCells,
            ExpectedPathSHA256MismatchRejected:
                (smartHashRejection.State is "failed" or "rejected") &&
                string.Equals(smartHashRejection.Code, "sourceChanged", StringComparison.OrdinalIgnoreCase) &&
                (deepHashRejection.State is "failed" or "rejected") &&
                string.Equals(deepHashRejection.Code, "sourceChanged", StringComparison.OrdinalIgnoreCase),
            DeepConnectSingleArcSingleMutationReadback: multiMutationShape,
            LegacyTwoAnchorCompatibilityReadback: true,
            DraftUndoRedoReadback: smartUndoReadback && redoReadback && undoReadback,
            HdfAndDraftUnchangedBeforeParentSave: sourcesUnchangedBeforeSave,
            ParentSaveChangedGeometryRevision: parentSaveChangedRevision,
            ReopenedAllThreeTraceArcsReadback: reopenedReadback,
            VectorSourceUnchanged: vectorSourceUnchanged,
            SourceFixtureUnchanged: originalFixtureUnchanged,
            Rejections: rejections.ToArray(),
            Operations: operations.ToArray());
        await File.WriteAllTextAsync(outputManifestPath, JsonSerializer.Serialize(report, JsonOptions),
            new UTF8Encoding(false), cancellation);
        return report;
    }

    private static bool TraceCoordinateSequencesEqual(IReadOnlyList<GeometryTopologyCoordinate> left,
        IReadOnlyList<GeometryTopologyCoordinate> right) => left.Count == right.Count &&
        left.Zip(right).All(pair => pair.First.X == pair.Second.X && pair.First.Y == pair.Second.Y);

    private static bool TraceContainsWaypointsInOrder(IReadOnlyList<GeometryTopologyCoordinate> points,
        IReadOnlyList<GeometryTopologyCoordinate> waypoints)
    {
        int pointIndex = 0;
        foreach (GeometryTopologyCoordinate waypoint in waypoints)
        {
            while (pointIndex < points.Count &&
                (points[pointIndex].X != waypoint.X || points[pointIndex].Y != waypoint.Y))
                pointIndex++;
            if (pointIndex == points.Count) return false;
            pointIndex++;
        }
        return waypoints.Count > 0;
    }

    private static int? TraceOptionalInt(JsonElement element, string name)
    {
        JsonElement value = PropertyOrNull(element, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
            ? number : null;
    }

    private static GeometryTopologyCoordinate[] TraceBuildSnappedEndpointPath(
        IReadOnlyList<GeometryTopologyCoordinate> sourcePath, GeometryTopologyCoordinate endpoint,
        double removeLastInteriorPointDistance)
    {
        if (sourcePath.Count < 2 || !double.IsFinite(removeLastInteriorPointDistance) ||
            removeLastInteriorPointDistance < 0d)
            throw new InvalidDataException("A completed trace requires a valid source path and endpoint hit radius.");
        var finalPath = sourcePath.Take(sourcePath.Count - 1).ToList();
        if (finalPath.Count > 1)
        {
            GeometryTopologyCoordinate lastInternal = finalPath[^1];
            double distance = Math.Sqrt(Math.Pow(lastInternal.X - endpoint.X, 2d) +
                Math.Pow(lastInternal.Y - endpoint.Y, 2d));
            if (distance <= removeLastInteriorPointDistance) finalPath.RemoveAt(finalPath.Count - 1);
        }
        finalPath.Add(endpoint);
        return finalPath.ToArray();
    }

    private static bool TraceHasDirectionReversal(IReadOnlyList<GeometryTopologyCoordinate> points)
    {
        if (points.Count < 3) return false;
        for (int index = 1; index + 1 < points.Count; index++)
        {
            GeometryTopologyCoordinate previous = points[index - 1];
            GeometryTopologyCoordinate current = points[index];
            GeometryTopologyCoordinate next = points[index + 1];
            double incomingX = current.X - previous.X;
            double incomingY = current.Y - previous.Y;
            double outgoingX = next.X - current.X;
            double outgoingY = next.Y - current.Y;
            if (incomingX * outgoingX + incomingY * outgoingY < 0d) return true;
        }
        return false;
    }

    private static GeometryTopologyCoordinate[] TraceBuildAnchoredPath(
        IReadOnlyList<GeometryTopologyCoordinate> source, IReadOnlyList<GeometryTopologyDeepConnectAnchor> anchors)
    {
        if (source.Count < 2 || anchors.Count < 2) throw new InvalidDataException("An anchored line path requires a source and at least two anchors.");
        var resolved = new List<GeometryTopologyCoordinate>();
        for (int anchorIndex = 1; anchorIndex < anchors.Count; anchorIndex++)
        {
            GeometryTopologyDeepConnectAnchor from = anchors[anchorIndex - 1];
            GeometryTopologyDeepConnectAnchor to = anchors[anchorIndex];
            if (from.SegmentIndex < 0 || to.SegmentIndex < 0 || from.SegmentIndex >= source.Count - 1 ||
                to.SegmentIndex >= source.Count - 1 || from.Fraction is < 0d or > 1d || to.Fraction is < 0d or > 1d)
                throw new InvalidDataException("An acceptance DeepConnect anchor is outside its captured source line.");
            GeometryTopologyCoordinate start = TraceInterpolate(source, from);
            GeometryTopologyCoordinate end = TraceInterpolate(source, to);
            var segment = new List<GeometryTopologyCoordinate> { start };
            if (from.SegmentIndex < to.SegmentIndex ||
                (from.SegmentIndex == to.SegmentIndex && from.Fraction <= to.Fraction))
            {
                for (int pointIndex = from.SegmentIndex + 1; pointIndex <= to.SegmentIndex; pointIndex++)
                    segment.Add(source[pointIndex]);
            }
            else
            {
                for (int pointIndex = from.SegmentIndex; pointIndex > to.SegmentIndex; pointIndex--)
                    segment.Add(source[pointIndex]);
            }
            if (segment[^1].X != end.X || segment[^1].Y != end.Y) segment.Add(end);
            if (segment.Count < 2) throw new InvalidDataException("Consecutive acceptance DeepConnect anchors form a zero-length source section.");
            if (resolved.Count == 0) resolved.AddRange(segment);
            else
            {
                if (resolved[^1].X != segment[0].X || resolved[^1].Y != segment[0].Y)
                    throw new InvalidDataException("The acceptance DeepConnect source sections are not contiguous.");
                resolved.AddRange(segment.Skip(1));
            }
        }
        return resolved.ToArray();
    }

    private static GeometryTopologyCoordinate TraceInterpolate(IReadOnlyList<GeometryTopologyCoordinate> source,
        GeometryTopologyDeepConnectAnchor anchor)
    {
        GeometryTopologyCoordinate first = source[anchor.SegmentIndex];
        GeometryTopologyCoordinate second = source[anchor.SegmentIndex + 1];
        return new(first.X + (second.X - first.X) * anchor.Fraction,
            first.Y + (second.Y - first.Y) * anchor.Fraction);
    }

    private static int TraceMutationCount(string draftState)
    {
        using JsonDocument document = JsonDocument.Parse(draftState);
        return Property(document.RootElement, "mutations").GetArrayLength();
    }

    private static string TraceLastMutationAction(string draftState)
    {
        using JsonDocument document = JsonDocument.Parse(draftState);
        JsonElement mutations = Property(document.RootElement, "mutations");
        if (mutations.GetArrayLength() == 0) return string.Empty;
        return StringProperty(Property(mutations[mutations.GetArrayLength() - 1], "mapCommand"), "action");
    }
}
