using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OSGeo.OGR;
using Ras.Backend;
using Ras.Runtime;

namespace Ras.Backend.ServerChecks;

internal sealed record GeometryGdbDeepConnectRejection(string Action, string State, string? Code, string Message);

internal sealed record GeometryGdbDeepConnectAcceptanceReport(
    string State,
    string ManifestPath,
    string SourceManifestPath,
    string SourceProjectPath,
    string SourceGeometryPath,
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string ProjectSessionID,
    string? CoreLayerContextIdentityToken,
    string GeometryPath,
    string InitialGeometryRevision,
    string FinalGeometryRevision,
    string VisualizationPath,
    string VisualizationRevision,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string GdbPath,
    GdbMapSourceIdentity GdbSource,
    GdbMapLayerDescriptor LineLayer,
    GdbMapLayerDescriptor MultiLineLayer,
    CatalogTreeNodeLocator LineNode,
    CatalogTreeNodeLocator MultiLineNode,
    GeometryTopologyVectorSourceComponent[] LineComponents,
    GeometryTopologyVectorSourceComponent[] MultiLineComponents,
    int DeepConnectFeatureFID,
    int DeepConnectArcFID,
    int DeepConnectStartNodeFID,
    int DeepConnectEndNodeFID,
    GeometryTopologyDeepConnectAnchor[] Anchors,
    GeometryTopologyCoordinate[] SourceCoordinates,
    GeometryTopologyCoordinate[] PreviewCoordinates,
    GeometryTopologyCoordinate[] SavedCoordinates,
    double PreviewEndpointDistanceInCells,
    bool PlainProjectSessionOwnerReadback,
    bool SavedMapCatalogAndGdbImportReadback,
    bool GdbDirectoryManifestReadback,
    bool GdbRendererLineOrderReadback,
    bool PointAndPolygonRemainDiagnosticOnly,
    bool StaleGdbDirectoryRejected,
    bool GdbDirectoryRestored,
    bool DeepConnectPreviewOwnerAndCanonicalPathReadback,
    bool PreviewIncludesNearNodeSnapAndPrunedInteriorPoint,
    bool ExpectedPathSHA256MismatchRejectedAtomically,
    bool SingleParentMutationAndConnectionArcReadback,
    bool DraftUndoRedoReadback,
    bool HdfUnchangedBeforeParentSave,
    bool ParentSaveChangedGeometryRevision,
    bool ReopenedSavedArcReadback,
    bool VisualizationAndProjectUnchangedAfterImport,
    bool GdbSourceUnchanged,
    bool OriginalFixtureUnchanged,
    GeometryGdbDeepConnectRejection[] Rejections,
    object[] Operations);

/// <summary>
/// Exercises retained OpenFileGDB line enumeration and source-backed Geometry
/// DeepConnect using a copied saved Plan map and the production loopback Host.
/// </summary>
internal static partial class ServerAcceptance
{
    public static async Task<GeometryGdbDeepConnectAcceptanceReport> RunGeometryGdbDeepConnectAsync(
        Uri baseAddress, string sourceManifestPath, string outputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("GDB Geometry DeepConnect acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string manifestPath = Path.GetFullPath(sourceManifestPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("The accepted clean Geometry-topology map manifest was not found.", manifestPath);
        using JsonDocument manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        JsonElement manifest = manifestDocument.RootElement;
        string sourceProjectPath = Path.GetFullPath(ArcMapManifestString(manifest, "projectPath", "sourceProjectPath"));
        string sourceGeometryPath = Path.GetFullPath(ArcMapManifestString(manifest, "geometryPath", "sourceGeometryPath"));
        if (!File.Exists(sourceProjectPath) || !Path.GetExtension(sourceProjectPath).Equals(".ras", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The source manifest must identify its saved .ras project.", sourceProjectPath);
        string sourceDirectory = Path.GetDirectoryName(sourceProjectPath)
            ?? throw new IOException("The source project has no containing directory.");
        string sourceVisualizationPath = Path.ChangeExtension(sourceProjectPath, ".vis");
        if (!File.Exists(sourceVisualizationPath) || !File.Exists(sourceGeometryPath) ||
            !ArcMapPathIsInside(sourceGeometryPath, sourceDirectory))
            throw new InvalidDataException("The source manifest must identify its saved .vis and contained Geometry HDF.");

        string sourceProjectSHA = FileSHA256(sourceProjectPath);
        string sourceVisualizationSHA = FileSHA256(sourceVisualizationPath);
        string sourceGeometrySHA = FileSHA256(sourceGeometryPath);
        string? acceptedProjectSHA = ArcMapOptionalString(manifest, "projectRevision") ?? ArcMapOptionalString(manifest, "sourceProjectSHA256");
        string? acceptedVisualizationSHA = ArcMapOptionalString(manifest, "visualizationRevision") ?? ArcMapOptionalString(manifest, "sourceVisualizationSHA256");
        string? acceptedGeometrySHA = ArcMapOptionalString(manifest, "savedGeometrySHA256") ??
            ArcMapOptionalString(manifest, "sourceGeometrySHA256") ?? ArcMapOptionalString(manifest, "savedRevision");
        if ((acceptedProjectSHA is not null && !acceptedProjectSHA.Equals(sourceProjectSHA, StringComparison.OrdinalIgnoreCase)) ||
            (acceptedVisualizationSHA is not null && !acceptedVisualizationSHA.Equals(sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase)) ||
            (acceptedGeometrySHA is not null && !acceptedGeometrySHA.Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase)))
            throw new SourceFileChangedException();

        SortedDictionary<string, string> sourceFixtureSnapshot = ArcMapFixtureSnapshot(sourceDirectory);
        string targetOutputDirectory = Path.GetFullPath(outputDirectory);
        if (File.Exists(targetOutputDirectory) || Directory.Exists(targetOutputDirectory))
            throw new IOException("GDB Geometry DeepConnect acceptance requires a new output directory; existing files are never overwritten.");
        if (ArcMapPathsOverlap(targetOutputDirectory, sourceDirectory))
            throw new IOException("The isolated GDB trace output must not overlap the accepted source fixture.");
        Directory.CreateDirectory(targetOutputDirectory);

        string fixtureDirectory = Path.Combine(targetOutputDirectory, "fixture");
        string projectPath = CopyProjectDirectory(sourceDirectory, sourceProjectPath, fixtureDirectory);
        string geometryPath = Path.GetFullPath(Path.Combine(fixtureDirectory,
            Path.GetRelativePath(sourceDirectory, sourceGeometryPath)));
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!File.Exists(geometryPath) || !File.Exists(visualizationPath) ||
            !FileSHA256(projectPath).Equals(sourceProjectSHA, StringComparison.OrdinalIgnoreCase) ||
            !FileSHA256(geometryPath).Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase) ||
            !FileSHA256(visualizationPath).Equals(sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The isolated copy did not preserve the source .ras, Geometry HDF, and .vis bytes.");

        using var timeout = new CancellationTokenSource(GeometryArcMapTimeout);
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# Host did not report ready.");

        var operations = new List<object>();
        var rejections = new List<GeometryGdbDeepConnectRejection>();
        OperationReceipt opened = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        operations.Add(OperationEntry("project.open", opened));
        string projectRevision = StringProperty(opened.Result, "revision");
        string projectXml = StringProperty(opened.Result, "projectXml");
        string projectXmlSHA = TextSHA256(projectXml);
        if (!IsSHA256(projectRevision) || !projectRevision.Equals(FileSHA256(projectPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("project.open did not bind the isolated .ras bytes to their SHA revision.");
        JsonElement coreContextValue = PropertyOrNull(opened.Result, "coreLayerContext");
        if (coreContextValue.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            throw new InvalidDataException("The clean local fixture unexpectedly resolved an external core-layer context.");
        CoreLayerRequestContext? coreLayerContext = null;
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
        if (!IsSHA256(visualizationRevision) || !visualizationRevision.Equals(FileSHA256(visualizationPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("map.document.list did not capture the exact copied .vis revision.");

        MapGeometryOwner? owner = null;
        MapTreeRequest? mapRequest = null;
        foreach (JsonElement map in Property(mapList.Result, "maps").EnumerateArray())
        {
            int mapIndex = IntProperty(map, "mapIndex");
            string mapName = StringProperty(map, "mapName");
            string mapType = StringProperty(map, "mapType");
            if (!string.Equals(mapType, "Plan", StringComparison.Ordinal)) continue;
            string mapID = Guid.NewGuid().ToString("D");
            var candidate = new MapTreeRequest(projectPath, projectXml, projectXmlSHA,
                mapID, mapIndex, mapName, mapType, visualizationRevision,
                ProjectSessionID: projectSessionID, CoreLayerContext: coreLayerContext);
            OperationReceipt treeReceipt = await SubmitOperationAsync(client, "map.tree.load", candidate, cancellation);
            operations.Add(OperationEntry("map.tree.load", treeReceipt));
            MapTreeSnapshot tree = JsonSerializer.Deserialize<MapTreeSnapshot>(treeReceipt.Result.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("The saved Plan map tree response was empty.");
            MapGeometryOwner? match = FindGeometryOwner(mapID, mapIndex, mapName, mapType,
                visualizationRevision, tree.Roots, candidate,
                TopologyMapFixturePlanName, TopologyMapFixtureGeometryName, geometryPath);
            if (match is null) continue;
            if (owner is not null) throw new InvalidDataException("The saved .vis catalog resolved more than one matching Plan Geometry owner.");
            owner = match;
            mapRequest = candidate;
        }
        if (owner is null || mapRequest is null)
            throw new InvalidDataException("The saved map catalog did not resolve the exact Plan CPU / Solver Geometry association.");
        if (mapRequest.ProjectSessionID != projectSessionID || mapRequest.CoreLayerContext is not null)
            throw new InvalidDataException("The topology map request lost the generated project session or acquired an external core context.");

        var geometryModel = new GeometryTopologyModelRequest(geometryPath, "Geometry", projectPath,
            projectXml, coreLayerContext);

        async Task<(GeometryTopologySnapshot Snapshot, string ContextToken, string Revision, double CellSize)> LoadGeometryAsync(
            MapGeometryOwner exactOwner, MapTreeRequest exactMapRequest, string expectedRevision)
        {
            OperationReceipt geometryLoad = await SubmitOperationAsync(client, "map.geometry.load", new
            {
                map = exactMapRequest,
                geometryNode = exactOwner.GeometryNode,
                pageSize = 512,
                skipFlowAreasPage = true,
                skipCulvertsPage = true,
                planNodes = exactOwner.PlanNodes,
            }, cancellation);
            operations.Add(OperationEntry("map.geometry.load", geometryLoad));
            string loadedRevision = StringProperty(geometryLoad.Result, "geometryRevision");
            string contextToken = StringProperty(geometryLoad.Result, "contextToken");
            if (!GdbTracePathEquals(StringProperty(geometryLoad.Result, "geometryCanonicalPath"), geometryPath) ||
                !loadedRevision.Equals(expectedRevision, StringComparison.OrdinalIgnoreCase) ||
                !loadedRevision.Equals(FileSHA256(geometryPath), StringComparison.OrdinalIgnoreCase) || !IsSHA256(contextToken))
                throw new InvalidDataException("map.geometry.load did not bind the exact copied Geometry HDF and map context.");

            OperationReceipt modelLoad = await SubmitOperationAsync(client, "model.load", geometryModel, cancellation);
            operations.Add(OperationEntry("model.load", modelLoad));
            if (!StringProperty(modelLoad.Result, "revision").Equals(loadedRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            OperationReceipt topologyLoad = await SubmitOperationAsync(client, "topology.load",
                new GeometryTopologyLoadRequest(geometryModel, loadedRevision), cancellation);
            operations.Add(OperationEntry("topology.load", topologyLoad));
            GeometryTopologySnapshot snapshot = ArcMapReadTopology(topologyLoad.Result);
            RequireTopologyOwner(topologyLoad.Result, loadedRevision);
            if (!snapshot.CanEdit || snapshot.Map is null || snapshot.Completion is null)
                throw new InvalidDataException("topology.load omitted the editable source Geometry topology.");
            double cellSize = MapCellSize(Property(topologyLoad.Result, "map"));
            if (!double.IsFinite(cellSize) || cellSize <= 0d)
                throw new InvalidDataException("The source Geometry map did not return a positive finite world-units-per-cell value.");
            return (snapshot, contextToken, loadedRevision, cellSize);
        }

        var initialLoad = await LoadGeometryAsync(owner, mapRequest, sourceGeometrySHA);
        GeometryTopologySnapshot initialTopology = initialLoad.Snapshot;
        string initialGeometryRevision = initialLoad.Revision;
        double mapCellSize = initialLoad.CellSize;
        GeometryTopologyExtent extent = initialTopology.Map!.Extent
            ?? throw new InvalidDataException("The source Geometry map did not return a finite extent.");
        GeometryTopologyFeatureRow[] sourceNodes = initialTopology.Nodes.Rows
            .Where(row => row.X is double x && double.IsFinite(x) && row.Y is double y && double.IsFinite(y))
            .OrderBy(row => row.FID).ToArray();
        if (sourceNodes.Length < 2)
            throw new InvalidDataException("The accepted Geometry source must have two stable topology nodes.");
        GeometryTopologyFeatureRow startNode = sourceNodes
            .OrderBy(row => Math.Pow(row.X!.Value - extent.MinX, 2d) + Math.Pow(row.Y!.Value - extent.MinY, 2d))
            .ThenBy(row => row.FID).First();
        GeometryTopologyFeatureRow endNode = sourceNodes.Where(row => row.FID != startNode.FID)
            .OrderByDescending(row => Math.Pow(row.X!.Value - startNode.X!.Value, 2d) + Math.Pow(row.Y!.Value - startNode.Y!.Value, 2d))
            .ThenBy(row => row.FID).First();
        GeometryTopologyCoordinate startPoint = new(startNode.X!.Value, startNode.Y!.Value);
        GeometryTopologyCoordinate endPoint = new(endNode.X!.Value, endNode.Y!.Value);

        GeometryTopologyCoordinate[] BuildTrace(GeometryTopologyCoordinate start,
            GeometryTopologyCoordinate end, double expansionMultiplier)
        {
            double width = extent.MaxX - extent.MinX;
            double height = extent.MaxY - extent.MinY;
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0d || height <= 0d)
                throw new InvalidDataException("The saved Geometry extent cannot define the OpenFileGDB line fixture.");
            int startSideX = Math.Abs(start.X - extent.MinX) <= Math.Abs(extent.MaxX - start.X) ? -1 : 1;
            int startSideY = Math.Abs(start.Y - extent.MinY) <= Math.Abs(extent.MaxY - start.Y) ? -1 : 1;
            int endSideX = Math.Abs(end.X - extent.MinX) <= Math.Abs(extent.MaxX - end.X) ? -1 : 1;
            int endSideY = Math.Abs(end.Y - extent.MinY) <= Math.Abs(extent.MaxY - end.Y) ? -1 : 1;
            double margin = Math.Max(Math.Max(width, height) * 0.18d * expansionMultiplier,
                mapCellSize * 12d * expansionMultiplier);
            GeometryTopologyCoordinate[] corners =
            [
                new(extent.MinX - margin, extent.MinY - margin),
                new(extent.MaxX + margin, extent.MinY - margin),
                new(extent.MaxX + margin, extent.MaxY + margin),
                new(extent.MinX - margin, extent.MaxY + margin),
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
            double approach = 1.4d * mapCellSize;
            double past = 4d * mapCellSize;
            points.Add(new(end.X + endSideX * past, end.Y + endSideY * past));
            points.Add(new(end.X + endSideX * approach, end.Y + endSideY * approach));
            return points.ToArray();
        }

        GeometryTopologyCoordinate[] primaryLine = BuildTrace(startPoint, endPoint, 1d);
        GeometryTopologyFeatureRow secondaryStartNode = sourceNodes.First(row => row.FID != startNode.FID && row.FID != endNode.FID);
        GeometryTopologyFeatureRow secondaryEndNode = sourceNodes.First(row => row.FID != startNode.FID && row.FID != secondaryStartNode.FID);
        GeometryTopologyCoordinate[] secondaryLine = BuildTrace(
            new(secondaryStartNode.X!.Value, secondaryStartNode.Y!.Value),
            new(secondaryEndNode.X!.Value, secondaryEndNode.Y!.Value), 1.35d);

        GeometryTopologyCoordinate[] TinyLine(int featureIndex, int partIndex)
        {
            GeometryTopologyFeatureRow from = sourceNodes[(featureIndex + partIndex) % sourceNodes.Length];
            GeometryTopologyFeatureRow to = sourceNodes[(featureIndex + partIndex + 1) % sourceNodes.Length];
            double dx = to.X!.Value - from.X!.Value;
            double dy = to.Y!.Value - from.Y!.Value;
            if (dx == 0d && dy == 0d) throw new InvalidDataException("The source fixture contains coincident topology nodes.");
            double offset = mapCellSize * (2d + featureIndex + partIndex);
            return
            [
                new(from.X.Value, from.Y.Value),
                new((from.X.Value + to.X.Value) / 2d - dy / Math.Sqrt(dx * dx + dy * dy) * offset,
                    (from.Y.Value + to.Y.Value) / 2d + dx / Math.Sqrt(dx * dx + dy * dy) * offset),
                new(to.X.Value, to.Y.Value),
            ];
        }

        GeometryTopologyCoordinate[][][] multiLines =
        [
            [TinyLine(0, 0), TinyLine(0, 1)],
            [TinyLine(1, 0), TinyLine(1, 1)],
        ];
        string gdbPath = Path.Combine(fixtureDirectory, "Map Sources", "Geometry GDB DeepConnect.gdb");
        Directory.CreateDirectory(Path.GetDirectoryName(gdbPath)!);
        CreateGdbTraceLayers(gdbPath, [primaryLine, secondaryLine], multiLines);
        GdbMapSourceIdentity gdbSource = GdbMapSourceService.CaptureSource(gdbPath);
        if (!gdbSource.Files.Any(file => file.Length > 0) || !IsSHA256(gdbSource.Revision))
            throw new InvalidDataException("The real OpenFileGDB line fixture did not produce a stable directory manifest.");

        GdbMapCatalogResponse catalog = JsonSerializer.Deserialize<GdbMapCatalogResponse>(
            (await SubmitOperationAsync(client, "map.gdb.catalog",
                new GdbMapCatalogRequest(gdbSource.SourcePath, gdbSource.Revision), cancellation)).Result.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("map.gdb.catalog returned no source catalog.");
        operations.Add(new { method = "map.gdb.catalog", state = "completed", revision = catalog.Source.Revision });
        if (!SameGdbTraceIdentity(gdbSource, catalog.Source))
            throw new InvalidDataException("map.gdb.catalog did not return the exact local directory manifest.");
        GdbMapLayerDescriptor lineLayer = catalog.Layers.SingleOrDefault(layer => layer.Name == "TraceLines")
            ?? throw new InvalidDataException("The GDB catalog omitted the real TraceLines layer.");
        GdbMapLayerDescriptor multiLineLayer = catalog.Layers.SingleOrDefault(layer => layer.Name == "TraceMultiLines")
            ?? throw new InvalidDataException("The GDB catalog omitted the real TraceMultiLines layer.");
        GdbMapLayerDescriptor pointLayer = catalog.Layers.SingleOrDefault(layer => layer.Name == "SurveyPoints")
            ?? throw new InvalidDataException("The retained GDB fixture helper's point diagnostic layer was omitted.");
        GdbMapLayerDescriptor polygonLayer = catalog.Layers.SingleOrDefault(layer => layer.Name == "ParcelWithHole")
            ?? throw new InvalidDataException("The retained GDB fixture helper's polygon diagnostic layer was omitted.");
        if (catalog.Layers.Length != 4 || lineLayer.VectorType is not ("Line" or "LineCollection") ||
            multiLineLayer.VectorType != "LineCollection" || lineLayer.FeatureCount != 2 || multiLineLayer.FeatureCount != 2)
            throw new InvalidDataException("The OpenFileGDB catalog did not retain two line-based layers, two ordered features per layer, and the known point/polygon diagnostics. Catalog: " +
                JsonSerializer.Serialize(catalog.Layers, JsonOptions));

        var documentMap = new MapDocumentRequest(projectPath, projectXml, projectXmlSHA,
            visualizationRevision, owner.MapType, MapID: owner.MapID, MapIndex: owner.MapIndex,
            MapName: owner.MapName, MapType: owner.MapType, CoreLayerContext: coreLayerContext);
        GdbMapImportResponse imported = JsonSerializer.Deserialize<GdbMapImportResponse>(
            (await SubmitOperationAsync(client, "map.gdb.import", new GdbMapImportRequest(
                documentMap, projectRevision, catalog.Source), cancellation)).Result.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("map.gdb.import returned no persisted map mutation receipt.");
        operations.Add(new { method = "map.gdb.import", state = "completed", revision = imported.Receipt.VisualizationRevision });
        if (!string.Equals(imported.Receipt.PreviousVisualizationRevision, visualizationRevision, StringComparison.OrdinalIgnoreCase) ||
            imported.ImportedLayers.Length != catalog.Layers.Length || !SameGdbTraceIdentity(imported.Source, gdbSource))
            throw new InvalidDataException("GDB import did not consume the exact catalog and previous saved-map revision.");
        visualizationRevision = imported.Receipt.VisualizationRevision;
        if (!IsSHA256(visualizationRevision) || !visualizationRevision.Equals(FileSHA256(visualizationPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The GDB import receipt does not match the copied .vis bytes.");

        async Task<(MapGeometryOwner Owner, MapTreeRequest Request, MapTreeSnapshot Tree)> ReloadTreeAsync(string expectedVisualizationRevision)
        {
            var exactRequest = new MapTreeRequest(projectPath, projectXml, projectXmlSHA,
                owner.MapID, owner.MapIndex, owner.MapName, owner.MapType, expectedVisualizationRevision,
                ProjectSessionID: projectSessionID, CoreLayerContext: coreLayerContext);
            OperationReceipt treeReceipt = await SubmitOperationAsync(client, "map.tree.load", exactRequest, cancellation);
            operations.Add(OperationEntry("map.tree.load/refresh", treeReceipt));
            MapTreeSnapshot tree = JsonSerializer.Deserialize<MapTreeSnapshot>(treeReceipt.Result.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("The refreshed saved Plan map tree was empty.");
            MapGeometryOwner refreshed = FindGeometryOwner(owner.MapID, owner.MapIndex, owner.MapName,
                owner.MapType, expectedVisualizationRevision, tree.Roots, exactRequest,
                TopologyMapFixturePlanName, TopologyMapFixtureGeometryName, geometryPath)
                ?? throw new InvalidDataException("The exact Plan Geometry owner did not survive the GDB import.");
            return (refreshed, exactRequest, tree);
        }

        (owner, mapRequest, MapTreeSnapshot importedTree) = await ReloadTreeAsync(visualizationRevision);
        CatalogTreeNode[] persistedNodes = WalkNodes(importedTree.Roots).ToArray();
        CatalogTreeNode FindGdbNode(string name) => persistedNodes.SingleOrDefault(node =>
            node.Locator is { NodeType: "GdbVectorLayer", MapType: "GdbVectorLayer" } locator &&
            locator.NodeName == name && GdbTracePathEquals(locator.AssetID, gdbSource.SourcePath) &&
            GdbTracePathEquals(locator.Filename, gdbSource.SourcePath))
            ?? throw new InvalidDataException("The imported .vis did not expose exactly one exact persisted GDB vector leaf: " + name);
        CatalogTreeNode lineNodeEntry = FindGdbNode("TraceLines");
        CatalogTreeNode multiLineNodeEntry = FindGdbNode("TraceMultiLines");
        CatalogTreeNode pointNodeEntry = FindGdbNode("SurveyPoints");
        CatalogTreeNode polygonNodeEntry = FindGdbNode("ParcelWithHole");
        CatalogTreeNodeLocator lineNode = lineNodeEntry.Locator!;
        CatalogTreeNodeLocator multiLineNode = multiLineNodeEntry.Locator!;
        CatalogTreeNodeLocator pointNode = pointNodeEntry.Locator!;
        CatalogTreeNodeLocator polygonNode = polygonNodeEntry.Locator!;
        if (new[] { lineNodeEntry, multiLineNodeEntry, pointNodeEntry, polygonNodeEntry }.Any(node => node.Checked != "checked") ||
            lineNode.FilenameRelative is null || multiLineNode.FilenameRelative != lineNode.FilenameRelative)
            throw new InvalidDataException("The persisted GDB source leaves are not checked children of the same saved dataset directory.");

        async Task<GdbMapSourceLoadResponse> LoadGdbLayerAsync(CatalogTreeNodeLocator node, string label)
        {
            OperationReceipt receipt = await SubmitOperationAsync(client, "map.gdb.source.load",
                new GdbMapSourceLoadRequest(mapRequest, node, gdbSource.Revision), cancellation);
            operations.Add(OperationEntry("map.gdb.source.load/" + label, receipt));
            GdbMapSourceLoadResponse loaded = JsonSerializer.Deserialize<GdbMapSourceLoadResponse>(receipt.Result.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("The exact GDB source reader returned no " + label + " data.");
            if (!SameGdbTraceIdentity(loaded.Source, gdbSource) || !SameGdbTraceNodeLocator(loaded.Node, node))
                throw new InvalidDataException("The GDB source reader changed its persisted layer or directory owner: " + label);
            return loaded;
        }

        GdbMapSourceLoadResponse lineRead = await LoadGdbLayerAsync(lineNode, "TraceLines");
        GdbMapSourceLoadResponse multiLineRead = await LoadGdbLayerAsync(multiLineNode, "TraceMultiLines");
        GdbMapSourceFeature[] lineFeatures = lineRead.Data.Features.OrderBy(feature => feature.SourceOrdinal).ToArray();
        GdbMapSourceFeature[] multiLineFeaturesRead = multiLineRead.Data.Features.OrderBy(feature => feature.SourceOrdinal).ToArray();
        bool lineFeaturesRetainOnePartEach = lineFeatures.Select(feature => feature.SourceOrdinal)
                .SequenceEqual(new[] { 0, 1 }) &&
            lineFeatures.All(feature => GdbTraceLineLeaves(feature.Geometry).Count() == 1);
        bool multiLineFeaturesRetainTwoPartsEach = multiLineFeaturesRead.Select(feature => feature.SourceOrdinal)
                .SequenceEqual(new[] { 0, 1 }) &&
            multiLineFeaturesRead.All(feature => GdbTraceLineLeaves(feature.Geometry).Count() == 2);
        if (lineRead.Data.Count != 2 || lineFeatures.Length != 2 ||
            multiLineRead.Data.Count != 2 || multiLineFeaturesRead.Length != 2 ||
            lineRead.Layer.FeatureCount != 2 || multiLineRead.Layer.FeatureCount != 2 ||
            lineRead.Layer.VectorType is not ("Line" or "LineCollection") ||
            multiLineRead.Layer.VectorType != "LineCollection" ||
            !SameGdbTraceLayer(lineRead.Layer, lineLayer) || !SameGdbTraceLayer(multiLineRead.Layer, multiLineLayer) ||
            !lineFeaturesRetainOnePartEach || !multiLineFeaturesRetainTwoPartsEach)
            throw new InvalidDataException("The persisted GDB reader did not retain exactly two ordered one-part line features and two ordered two-part multiline features. " +
                "Catalog=" + JsonSerializer.Serialize(catalog.Layers, JsonOptions) + "; " +
                "lineLayer=" + JsonSerializer.Serialize(lineRead.Layer, JsonOptions) + "; " +
                "lineFeatureParts=" + JsonSerializer.Serialize(lineFeatures.Select(feature => new
                {
                    feature.SourceOrdinal,
                    sourceID = feature.SourceID,
                    linePartCount = GdbTraceLineLeaves(feature.Geometry).Count(),
                    geometry = feature.Geometry
                }), JsonOptions) + "; " +
                "multiLineLayer=" + JsonSerializer.Serialize(multiLineRead.Layer, JsonOptions) + "; " +
                "multiLineFeatureParts=" + JsonSerializer.Serialize(multiLineFeaturesRead.Select(feature => new
                {
                    feature.SourceOrdinal,
                    sourceID = feature.SourceID,
                    linePartCount = GdbTraceLineLeaves(feature.Geometry).Count(),
                    geometry = feature.Geometry
                }), JsonOptions));

        string inspectRequestID = Guid.NewGuid().ToString("D");
        var inspectRequest = new GeometryTopologyVectorSourceInspectRequest(inspectRequestID, mapRequest,
        [
            new(lineNode, ExpectedFamily: null, ExpectedGdbSource: null),
            new(multiLineNode, ExpectedFamily: null, ExpectedGdbSource: null),
            new(pointNode, ExpectedFamily: null, ExpectedGdbSource: null),
            new(polygonNode, ExpectedFamily: null, ExpectedGdbSource: null),
        ]);
        OperationReceipt inspectReceipt = await SubmitOperationAsync(client, "topology.vectorSource.inspect", inspectRequest, cancellation);
        operations.Add(OperationEntry("topology.vectorSource.inspect", inspectReceipt));
        GeometryTopologyVectorSourceInspectResult inspection = JsonSerializer.Deserialize<GeometryTopologyVectorSourceInspectResult>(
            inspectReceipt.Result.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("topology.vectorSource.inspect returned no source descriptors.");
        AssertGdbTraceInspectionOwner(inspection, inspectRequestID, projectPath, projectRevision,
            projectXmlSHA, projectSessionID, owner, visualizationPath, visualizationRevision);
        if (inspection.Sources.Length != 4) throw new InvalidDataException("The read-only inspect did not return all four exact persisted GDB leaves.");
        GeometryTopologyVectorSourceLayerResult Descriptor(CatalogTreeNodeLocator node) =>
            inspection.Sources.SingleOrDefault(source => SameGdbTraceNodeLocator(source.Node, node))
            ?? throw new InvalidDataException("The read-only inspect omitted an exact GDB source node: " + node.NodeName);
        GeometryTopologyVectorSourceLayerResult lineDescriptor = Descriptor(lineNode);
        GeometryTopologyVectorSourceLayerResult multiLineDescriptor = Descriptor(multiLineNode);
        GeometryTopologyVectorSourceLayerResult pointDescriptor = Descriptor(pointNode);
        GeometryTopologyVectorSourceLayerResult polygonDescriptor = Descriptor(polygonNode);
        AssertGdbTraceLayerDescriptor(lineDescriptor, lineNode, gdbSource, lineLayer, lineRead, expectedComponentCount: 2);
        AssertGdbTraceLayerDescriptor(multiLineDescriptor, multiLineNode, gdbSource, multiLineLayer, multiLineRead, expectedComponentCount: 4);
        bool pointPolygonDiagnosticOnly = !pointDescriptor.CanDeepConnect && pointDescriptor.Components.Length == 0 &&
            !polygonDescriptor.CanDeepConnect && polygonDescriptor.Components.Length == 0 &&
            pointDescriptor.SourceKind == "gdb" && polygonDescriptor.SourceKind == "gdb" &&
            pointDescriptor.GdbLayer?.VectorType == pointLayer.VectorType &&
            polygonDescriptor.GdbLayer?.VectorType == polygonLayer.VectorType &&
            SameGdbTraceIdentity(pointDescriptor.GdbSource, gdbSource) && SameGdbTraceIdentity(polygonDescriptor.GdbSource, gdbSource);
        if (!pointPolygonDiagnosticOnly)
            throw new InvalidDataException("The point or polygon GDB source was fabricated as a DeepConnect line component.");

        bool lineOrderMatches = GdbTraceComponentsMatchRead(lineDescriptor.Components, lineRead.Data) &&
            GdbTraceComponentsMatchRead(multiLineDescriptor.Components, multiLineRead.Data);
        if (!lineOrderMatches)
            throw new InvalidDataException("The GDB source descriptors did not preserve exact renderer line order, source ordinals, or projected vertices.");

        // A sidecar is enough to stale the directory manifest without corrupting
        // any actual GDB table. It is always deleted before the next read.
        string staleMarker = Path.Combine(gdbSource.SourcePath, "__geometry_deep_connect_stale_manifest.bin");
        bool staleDirectoryRejected;
        try
        {
            await File.WriteAllTextAsync(staleMarker, "temporary manifest change", new UTF8Encoding(false), cancellation);
            var staleRequest = inspectRequest with
            {
                RequestID = Guid.NewGuid().ToString("D"),
                Sources = [new(lineNode, ExpectedFamily: null, ExpectedGdbSource: gdbSource)],
            };
            GeometryGdbDeepConnectRejection staleResult = await ExpectGdbTraceOperationFailureAsync(
                client, "topology.vectorSource.inspect", staleRequest, "gdb.expectedManifestMismatch", cancellation);
            staleDirectoryRejected = staleResult.State == "failed" && string.Equals(staleResult.Code, "sourceChanged", StringComparison.OrdinalIgnoreCase);
            if (!staleDirectoryRejected) throw new InvalidDataException("The stale GDB directory manifest was not rejected before returning vector components.");
        }
        finally
        {
            if (File.Exists(staleMarker)) File.Delete(staleMarker);
        }
        GdbMapSourceIdentity restoredGdbSource = GdbMapSourceService.CaptureSource(gdbSource.SourcePath);
        bool gdbDirectoryRestored = SameGdbTraceIdentity(restoredGdbSource, gdbSource);
        if (!gdbDirectoryRestored) throw new InvalidDataException("The temporary stale-manifest marker was not fully removed before later reads.");

        var afterImportLoad = await LoadGeometryAsync(owner, mapRequest, initialGeometryRevision);
        GeometryTopologySnapshot current = afterImportLoad.Snapshot;
        string contextToken = afterImportLoad.ContextToken;
        string geometryRevision = afterImportLoad.Revision;
        if (!geometryRevision.Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase) ||
            !ArcMapTopologySignature(current).Equals(ArcMapTopologySignature(initialTopology), StringComparison.Ordinal))
            throw new InvalidDataException("The saved GDB import changed the pre-existing Geometry topology or HDF revision.");

        GeometryTopologyVectorSourceComponent primaryComponent = lineDescriptor.Components
            .Single(component => component.FeatureFID == 0);
        GeometryTopologyCoordinate[] sourcePoints = primaryComponent.Points;
        if (sourcePoints.Length < 5 || sourcePoints[0].X != startPoint.X || sourcePoints[0].Y != startPoint.Y)
            throw new InvalidDataException("The first retained GDB renderer component did not begin at its exact existing Geometry node.");
        GeometryTopologyDeepConnectAnchor[] anchors =
        [
            new(0, 0d),
            new(sourcePoints.Length - 2, 0.95d),
            new(sourcePoints.Length - 2, 0.75d),
        ];
        GeometryTopologyDeepConnectAnchor firstAnchor = anchors[0];
        GeometryTopologyDeepConnectAnchor lastAnchor = anchors[^1];
        var deepSource = new GeometryTopologyDeepConnectSource("vector", gdbSource.SourcePath,
            gdbSource.Revision, primaryComponent.FeatureFID, 0, primaryComponent.ComponentIdentity,
            sourcePoints, firstAnchor.SegmentIndex, firstAnchor.Fraction,
            lastAnchor.SegmentIndex, lastAnchor.Fraction,
            new GeometryTopologyDeepConnectVectorSource(mapRequest, lineNode, ExpectedFamily: null,
                ExpectedGdbSource: gdbSource))
        { Anchors = anchors };
        var deepCommand = new GeometryTopologyMapCommand("deepConnect", "arc", [], MapCellSize: mapCellSize,
            DeepConnectSource: deepSource) { StartNodeFID = startNode.FID };
        GeometryTopologyMapRequestContext NewMapContext(GeometryTopologySnapshot snapshot) => new(
            Guid.NewGuid().ToString("D"), owner.MapID, contextToken, geometryRevision,
            snapshot.Completion?.DraftIdentity ?? throw new InvalidDataException("The Geometry draft omitted its identity."));

        GeometryTopologyMapRequestContext previewContext = NewMapContext(current);
        var previewRequest = new GeometryTopologyEditRequest(geometryModel, current.Revision, current.DraftState,
            new GeometryTopologyMutation("mapCommand", "map", []) { MapCommand = deepCommand }, previewContext);
        OperationReceipt previewReceipt = await SubmitOperationAsync(client, "topology.mapCommand.preview", previewRequest, cancellation);
        operations.Add(OperationEntry("topology.mapCommand.preview/deepConnect", previewReceipt));
        GeometryTopologyMapCommandPreviewResponse preview = JsonSerializer.Deserialize<GeometryTopologyMapCommandPreviewResponse>(
            previewReceipt.Result.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("The GDB DeepConnect preview returned no canonical final path.");
        GeometryTopologyCoordinate[] expectedAnchoredPath = TraceBuildAnchoredPath(sourcePoints, anchors);
        double endpointDistanceInCells = Math.Sqrt(Math.Pow(expectedAnchoredPath[^1].X - endPoint.X, 2d) +
            Math.Pow(expectedAnchoredPath[^1].Y - endPoint.Y, 2d)) / mapCellSize;
        GeometryTopologyCoordinate[] expectedFinalPath = TraceBuildSnappedEndpointPath(
            expectedAnchoredPath, endPoint, 10d * mapCellSize);
        bool prunedNearEndpointInterior = expectedFinalPath.Length == expectedAnchoredPath.Length - 1 &&
            Math.Sqrt(Math.Pow(expectedAnchoredPath[^2].X - endPoint.X, 2d) +
                Math.Pow(expectedAnchoredPath[^2].Y - endPoint.Y, 2d)) <= 10d * mapCellSize;
        JsonElement expectedGdbCaptureElement = PropertyOrNull(
            PropertyOrNull(PropertyOrNull(previewReceipt.Result, "deepConnectSource"), "vectorSource"), "expectedGdbSource");
        GdbMapSourceIdentity? echoedGdbCapture = expectedGdbCaptureElement.ValueKind == JsonValueKind.Object
            ? expectedGdbCaptureElement.Deserialize<GdbMapSourceIdentity>(JsonOptions)
            : null;
        bool previewOwnerReadback = preview.RequestID == previewContext.RequestID && preview.MapID == owner.MapID &&
            preview.ContextToken == contextToken && preview.GeometryRevision.Equals(geometryRevision, StringComparison.OrdinalIgnoreCase) &&
            preview.Revision.Equals(current.Revision, StringComparison.OrdinalIgnoreCase) &&
            preview.DraftIdentity == previewContext.ExpectedDraftIdentity && preview.Action == "deepConnect" &&
            preview.DeepConnectSource is { SourceKind: "vector", PathIndex: 0 } echoedSource &&
            GdbTracePathEquals(echoedSource.SourceLocator, gdbSource.SourcePath) &&
            echoedSource.SourceRevision.Equals(gdbSource.Revision, StringComparison.OrdinalIgnoreCase) &&
            echoedSource.FeatureFID == primaryComponent.FeatureFID &&
            echoedSource.ComponentIdentity == primaryComponent.ComponentIdentity &&
            TraceCoordinateSequencesEqual(echoedSource.Points, sourcePoints) &&
            preview.SourceRevision.Equals(gdbSource.Revision, StringComparison.OrdinalIgnoreCase) &&
            SameGdbTraceIdentity(echoedGdbCapture, gdbSource) &&
            preview.ConnectionArcFID >= 0 && preview.StartNodeFID == startNode.FID && preview.EndNodeFID == endNode.FID &&
            preview.Coordinates.Length >= 2 && IsSHA256(preview.PathSHA256) &&
            TraceCoordinateSequencesEqual(preview.Coordinates, expectedFinalPath) &&
            FileSHA256(geometryPath).Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase) &&
            current.DraftState == afterImportLoad.Snapshot.DraftState;
        if (!previewOwnerReadback)
            throw new InvalidDataException("The GDB DeepConnect preview lost its exact owner, directory manifest, component, snapped endpoints, or canonical final path.");
        if (endpointDistanceInCells is < 2d or > 3d || !prunedNearEndpointInterior ||
            !TraceHasDirectionReversal(expectedAnchoredPath))
            throw new InvalidDataException("The GDB preview fixture did not exercise a 2–3-cell endpoint snap, near-interior pruning, and three-anchor foldback.");

        string invalidHash = preview.PathSHA256.All(character => character == '0')
            ? new string('1', 64) : new string('0', 64);
        GeometryTopologyMapRequestContext rejectedContext = NewMapContext(current);
        var rejectedRequest = new GeometryTopologyEditRequest(geometryModel, current.Revision, current.DraftState,
            new GeometryTopologyMutation("mapCommand", "map", [])
            { MapCommand = deepCommand with { ExpectedPathSHA256 = invalidHash } }, rejectedContext);
        GeometryGdbDeepConnectRejection hashRejection = await ExpectGdbTraceOperationFailureAsync(
            client, "topology.edit", rejectedRequest, "deepConnect.expectedPathSHA256Mismatch", cancellation);
        rejections.Add(hashRejection);
        bool hashRejectedAtomically = hashRejection.State == "failed" &&
            string.Equals(hashRejection.Code, "sourceChanged", StringComparison.OrdinalIgnoreCase) &&
            current.DraftState == afterImportLoad.Snapshot.DraftState &&
            FileSHA256(geometryPath).Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase);
        if (!hashRejectedAtomically)
            throw new InvalidDataException("The GDB DeepConnect expected-path mismatch did not reject atomically before the draft or HDF changed.");

        GeometryTopologyMapRequestContext applyContext = NewMapContext(current);
        var applyRequest = new GeometryTopologyEditRequest(geometryModel, current.Revision, current.DraftState,
            new GeometryTopologyMutation("mapCommand", "map", [])
            { MapCommand = deepCommand with { ExpectedPathSHA256 = preview.PathSHA256 } }, applyContext);
        OperationReceipt editReceipt = await SubmitOperationAsync(client, "topology.edit", applyRequest, cancellation);
        operations.Add(OperationEntry("topology.edit/deepConnect", editReceipt));
        GeometryTopologySnapshot deepConnected = ArcMapReadTopology(editReceipt.Result);
        RequireTopologyOwner(editReceipt.Result, geometryRevision);
        GeometryTopologyFeatureRow[] addedArcs = deepConnected.Arcs.Rows
            .Where(row => !current.Arcs.Rows.Any(previous => previous.FID == row.FID)).ToArray();
        int mutationCountBefore = TraceMutationCount(current.DraftState);
        int mutationCountAfter = TraceMutationCount(deepConnected.DraftState);
        bool oneConnectionArcAndMutation = addedArcs.Length == 1 && addedArcs[0].FID == preview.ConnectionArcFID &&
            addedArcs[0].StartNodeID == startNode.FID && addedArcs[0].EndNodeID == endNode.FID &&
            mutationCountAfter == mutationCountBefore + 1 && TraceLastMutationAction(deepConnected.DraftState) == "deepConnect";
        GeometryTopologyFeatureRow connectionArc = addedArcs.SingleOrDefault(row => row.FID == preview.ConnectionArcFID)
            ?? throw new InvalidDataException("The canonical preview connectionArcFID was not the one actual new Geometry arc.");
        GeometryTopologyCoordinate[] savedCoordinates = ArcMapPath(ArcMapFeatures(deepConnected, "arc")
            .Single(feature => feature.FID == connectionArc.FID));
        oneConnectionArcAndMutation &= TraceCoordinateSequencesEqual(savedCoordinates, preview.Coordinates);
        if (!oneConnectionArcAndMutation)
            throw new InvalidDataException("The parent draft did not append exactly one mutation/arc matching the canonical GDB preview path and FID.");

        OperationReceipt undoReceipt = await SubmitOperationAsync(client, "topology.edit",
            new GeometryTopologyEditRequest(geometryModel, deepConnected.Revision, deepConnected.DraftState,
                new GeometryTopologyMutation("undo", "history", [])), cancellation);
        operations.Add(OperationEntry("topology.edit/undoDeepConnect", undoReceipt));
        GeometryTopologySnapshot undone = ArcMapReadTopology(undoReceipt.Result);
        bool undoReadback = ArcMapTopologySignature(undone) == ArcMapTopologySignature(current) && undone.CanRedo;
        if (!undoReadback) throw new InvalidDataException("Undo did not restore the exact pre-DeepConnect Geometry draft.");
        OperationReceipt redoReceipt = await SubmitOperationAsync(client, "topology.edit",
            new GeometryTopologyEditRequest(geometryModel, undone.Revision, undone.DraftState,
                new GeometryTopologyMutation("redo", "history", [])), cancellation);
        operations.Add(OperationEntry("topology.edit/redoDeepConnect", redoReceipt));
        GeometryTopologySnapshot redone = ArcMapReadTopology(redoReceipt.Result);
        bool redoReadback = ArcMapTopologySignature(redone) == ArcMapTopologySignature(deepConnected) &&
            ArcMapFeatures(redone, "arc").Any(feature => feature.FID == preview.ConnectionArcFID &&
                TraceCoordinateSequencesEqual(ArcMapPath(feature), preview.Coordinates));
        if (!redoReadback) throw new InvalidDataException("Redo did not restore the exact previewed GDB connection arc.");

        bool unchangedBeforeSave = FileSHA256(geometryPath).Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase) &&
            SameGdbTraceIdentity(GdbMapSourceService.CaptureSource(gdbSource.SourcePath), gdbSource);
        if (!unchangedBeforeSave)
            throw new InvalidDataException("The unsaved Geometry draft changed HDF, project, map catalog, or the persisted GDB source.");
        OperationReceipt saveModelLoad = await SubmitOperationAsync(client, "model.load", geometryModel, cancellation);
        operations.Add(OperationEntry("model.load/before-parent-save", saveModelLoad));
        if (!StringProperty(saveModelLoad.Result, "revision").Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        OperationReceipt parentSave = await SubmitOperationAsync(client, "model.save", new
        {
            assetPath = geometryPath,
            kind = "Geometry",
            projectPath,
            revision = initialGeometryRevision,
            fields = new Dictionary<string, string>(),
            images = Property(saveModelLoad.Result, "images"),
            projectXml,
            sourceState = OptionalString(saveModelLoad.Result, "sourceState"),
            geometryTopologyState = redone.DraftState,
            coreLayerContext,
        }, cancellation);
        operations.Add(OperationEntry("model.save/parent", parentSave));
        string finalGeometryRevision = StringProperty(parentSave.Result, "revision");
        bool parentSaveChangedRevision = IsSHA256(finalGeometryRevision) &&
            !finalGeometryRevision.Equals(initialGeometryRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(geometryPath).Equals(finalGeometryRevision, StringComparison.OrdinalIgnoreCase);
        if (!parentSaveChangedRevision)
            throw new InvalidDataException("The parent model.save did not publish the GDB connection arc into Geometry HDF.");

        OperationReceipt reopenedProject = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        string reopenedProjectXml = StringProperty(reopenedProject.Result, "projectXml");
        if (!StringProperty(reopenedProject.Result, "revision").Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            reopenedProjectXml != projectXml || !FileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !FileSHA256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Parent Geometry save changed the copied project XML or saved map catalog unexpectedly.");
        var reopenedModel = new GeometryTopologyModelRequest(geometryPath, "Geometry", projectPath, reopenedProjectXml, coreLayerContext);
        OperationReceipt reopenedModelLoad = await SubmitOperationAsync(client, "model.load", reopenedModel, cancellation);
        operations.Add(OperationEntry("model.load/reopen", reopenedModelLoad));
        OperationReceipt reopenedTopologyLoad = await SubmitOperationAsync(client, "topology.load",
            new GeometryTopologyLoadRequest(reopenedModel, finalGeometryRevision), cancellation);
        operations.Add(OperationEntry("topology.load/reopen", reopenedTopologyLoad));
        GeometryTopologySnapshot reopenedTopology = ArcMapReadTopology(reopenedTopologyLoad.Result);
        RequireTopologyOwner(reopenedTopologyLoad.Result, finalGeometryRevision);
        bool reopenedArcReadback = !reopenedTopology.IsDirty &&
            ArcMapFeatures(reopenedTopology, "arc").Any(feature => feature.FID == preview.ConnectionArcFID &&
                TraceCoordinateSequencesEqual(ArcMapPath(feature), preview.Coordinates));
        if (!reopenedArcReadback)
            throw new InvalidDataException("Reopening the parent-saved Geometry HDF did not recover the exact GDB-backed connection arc.");

        bool gdbSourceUnchanged = SameGdbTraceIdentity(GdbMapSourceService.CaptureSource(gdbSource.SourcePath), gdbSource);
        bool originalFixtureUnchanged = ArcMapFixtureSnapshotEquals(sourceFixtureSnapshot,
                ArcMapFixtureSnapshot(sourceDirectory)) &&
            FileSHA256(sourceProjectPath).Equals(sourceProjectSHA, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(sourceGeometryPath).Equals(sourceGeometrySHA, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(sourceVisualizationPath).Equals(sourceVisualizationSHA, StringComparison.OrdinalIgnoreCase);
        if (!gdbSourceUnchanged || !originalFixtureUnchanged)
            throw new InvalidDataException("The GDB DeepConnect runner changed an input source fixture or its isolated OpenFileGDB directory.");
        bool projectAndMapUnchanged = FileSHA256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(visualizationPath).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase);
        if (!projectAndMapUnchanged)
            throw new InvalidDataException("The Geometry save changed the project XML or imported map catalog.");

        string outputManifestPath = Path.Combine(targetOutputDirectory, "geometry-gdb-deepconnect-acceptance.json");
        var report = new GeometryGdbDeepConnectAcceptanceReport(
            State: "passed",
            ManifestPath: outputManifestPath,
            SourceManifestPath: manifestPath,
            SourceProjectPath: sourceProjectPath,
            SourceGeometryPath: sourceGeometryPath,
            ProjectPath: projectPath,
            ProjectRevision: projectRevision,
            ProjectXmlSHA: projectXmlSHA,
            ProjectSessionID: projectSessionID,
            CoreLayerContextIdentityToken: coreLayerContext?.IdentityToken,
            GeometryPath: geometryPath,
            InitialGeometryRevision: initialGeometryRevision,
            FinalGeometryRevision: finalGeometryRevision,
            VisualizationPath: visualizationPath,
            VisualizationRevision: visualizationRevision,
            MapID: owner.MapID,
            MapIndex: owner.MapIndex,
            MapName: owner.MapName,
            MapType: owner.MapType,
            GdbPath: gdbSource.SourcePath,
            GdbSource: gdbSource,
            LineLayer: lineDescriptor.GdbLayer!,
            MultiLineLayer: multiLineDescriptor.GdbLayer!,
            LineNode: lineNode,
            MultiLineNode: multiLineNode,
            LineComponents: lineDescriptor.Components,
            MultiLineComponents: multiLineDescriptor.Components,
            DeepConnectFeatureFID: primaryComponent.FeatureFID,
            DeepConnectArcFID: preview.ConnectionArcFID,
            DeepConnectStartNodeFID: preview.StartNodeFID,
            DeepConnectEndNodeFID: preview.EndNodeFID,
            Anchors: anchors,
            SourceCoordinates: sourcePoints,
            PreviewCoordinates: preview.Coordinates,
            SavedCoordinates: savedCoordinates,
            PreviewEndpointDistanceInCells: endpointDistanceInCells,
            PlainProjectSessionOwnerReadback: mapRequest.ProjectSessionID == projectSessionID &&
                coreLayerContext is null && inspection.Owner.CoreLayerContextIdentityToken is null,
            SavedMapCatalogAndGdbImportReadback: imported.Receipt.MapIndex == owner.MapIndex &&
                imported.Receipt.MapName == owner.MapName && imported.Receipt.MapType == owner.MapType,
            GdbDirectoryManifestReadback: SameGdbTraceIdentity(gdbSource, catalog.Source) &&
                SameGdbTraceIdentity(gdbSource, imported.Source) && lineRead.Layer.Name == lineLayer.Name &&
                multiLineRead.Layer.Name == multiLineLayer.Name,
            GdbRendererLineOrderReadback: lineOrderMatches,
            PointAndPolygonRemainDiagnosticOnly: pointPolygonDiagnosticOnly,
            StaleGdbDirectoryRejected: staleDirectoryRejected,
            GdbDirectoryRestored: gdbDirectoryRestored,
            DeepConnectPreviewOwnerAndCanonicalPathReadback: previewOwnerReadback,
            PreviewIncludesNearNodeSnapAndPrunedInteriorPoint: endpointDistanceInCells is >= 2d and <= 3d && prunedNearEndpointInterior,
            ExpectedPathSHA256MismatchRejectedAtomically: hashRejectedAtomically,
            SingleParentMutationAndConnectionArcReadback: oneConnectionArcAndMutation,
            DraftUndoRedoReadback: undoReadback && redoReadback,
            HdfUnchangedBeforeParentSave: unchangedBeforeSave,
            ParentSaveChangedGeometryRevision: parentSaveChangedRevision,
            ReopenedSavedArcReadback: reopenedArcReadback,
            VisualizationAndProjectUnchangedAfterImport: projectAndMapUnchanged,
            GdbSourceUnchanged: gdbSourceUnchanged,
            OriginalFixtureUnchanged: originalFixtureUnchanged,
            Rejections: rejections.ToArray(),
            Operations: operations.ToArray());
        await File.WriteAllTextAsync(outputManifestPath, JsonSerializer.Serialize(report, JsonOptions),
            new UTF8Encoding(false), cancellation);
        return report;
    }

    private static void CreateGdbTraceLayers(string gdbPath,
        GeometryTopologyCoordinate[][] lineFeatures, GeometryTopologyCoordinate[][][] multiLineFeatures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gdbPath);
        RasRuntimeBootstrap.Configure();
        _ = GdbSourceAcceptance.CreateVectorSource(gdbPath);
        using Driver driver = Ogr.GetDriverByName("OpenFileGDB")
            ?? throw new InvalidOperationException("The GDAL OpenFileGDB writer is unavailable.");
        using DataSource dataSource = Ogr.Open(gdbPath, 1)
            ?? throw new InvalidOperationException("The retained OpenFileGDB fixture could not be reopened for line-layer writing.");
        WriteLineLayer(dataSource, "TraceLines", lineFeatures);
        WriteMultiLineLayer(dataSource, "TraceMultiLines", multiLineFeatures);

        static void WriteLineLayer(DataSource dataSource, string name, GeometryTopologyCoordinate[][] features)
        {
            using Layer layer = dataSource.CreateLayer(name, null, wkbGeometryType.wkbLineString, null)
                ?? throw new InvalidOperationException("The OpenFileGDB LineString layer could not be created: " + name);
            using FeatureDefn definition = layer.GetLayerDefn();
            foreach (GeometryTopologyCoordinate[] points in features)
            {
                using var line = new Geometry(wkbGeometryType.wkbLineString);
                AddCoordinates(line, points);
                using var feature = new Feature(definition);
                if (feature.SetGeometry(line) != 0 || layer.CreateFeature(feature) != 0)
                    throw new InvalidDataException("An OpenFileGDB ordered LineString feature could not be written: " + name);
            }
        }

        static void WriteMultiLineLayer(DataSource dataSource, string name, GeometryTopologyCoordinate[][][] features)
        {
            using Layer layer = dataSource.CreateLayer(name, null, wkbGeometryType.wkbMultiLineString, null)
                ?? throw new InvalidOperationException("The OpenFileGDB MultiLineString layer could not be created: " + name);
            using FeatureDefn definition = layer.GetLayerDefn();
            foreach (GeometryTopologyCoordinate[][] parts in features)
            {
                if (parts.Length < 2) throw new InvalidDataException("The multiline GDB fixture requires multiple distinct parts.");
                using var multi = new Geometry(wkbGeometryType.wkbMultiLineString);
                foreach (GeometryTopologyCoordinate[] points in parts)
                {
                    using var line = new Geometry(wkbGeometryType.wkbLineString);
                    AddCoordinates(line, points);
                    if (multi.AddGeometry(line) != 0)
                        throw new InvalidDataException("A MultiLineString component could not be appended to the GDB feature.");
                }
                using var feature = new Feature(definition);
                if (feature.SetGeometry(multi) != 0 || layer.CreateFeature(feature) != 0)
                    throw new InvalidDataException("An OpenFileGDB ordered MultiLineString feature could not be written.");
            }
        }

        static void AddCoordinates(Geometry geometry, IEnumerable<GeometryTopologyCoordinate> points)
        {
            int count = 0;
            foreach (GeometryTopologyCoordinate point in points)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                    throw new InvalidDataException("The GDB line fixture contains a non-finite coordinate.");
                geometry.AddPoint(point.X, point.Y, 0d);
                count++;
            }
            if (count < 2) throw new InvalidDataException("The GDB line fixture requires at least two vertices per component.");
        }
    }

    private static bool GdbTraceComponentsMatchRead(
        GeometryTopologyVectorSourceComponent[] components, GdbMapSourceData data)
    {
        var expected = new List<(int FeatureOrdinal, int PartOrdinal, string? SourceID, GeometryTopologyCoordinate[] Points)>();
        foreach (GdbMapSourceFeature feature in data.Features.OrderBy(feature => feature.SourceOrdinal))
        {
            int partOrdinal = 0;
            foreach (GeometryTopologyCoordinate[] points in GdbTraceLineLeaves(feature.Geometry))
            {
                int currentPart = partOrdinal++;
                if (points.Length >= 2)
                    expected.Add((feature.SourceOrdinal, currentPart, feature.SourceID, points));
            }
        }
        return components.Length == expected.Count && components.Select((component, index) =>
        {
            var item = expected[index];
            return component.FeatureFID == index && component.PathIndex == 0 &&
                component.ComponentIdentity == $"feature:{index}:part:0" &&
                component.SourceFeatureOrdinal == item.FeatureOrdinal && component.SourcePartOrdinal == item.PartOrdinal &&
                component.SourceID == item.SourceID && TraceCoordinateSequencesEqual(component.Points, item.Points);
        }).All(matches => matches);
    }

    private static IEnumerable<GeometryTopologyCoordinate[]> GdbTraceLineLeaves(GdbMapSourceGeometry geometry)
    {
        if (geometry.Vertices.Length >= 2)
            yield return geometry.Vertices.Select(vertex => new GeometryTopologyCoordinate(vertex.X, vertex.Y)).ToArray();
        foreach (GdbMapSourceGeometry child in geometry.Children)
            foreach (GeometryTopologyCoordinate[] points in GdbTraceLineLeaves(child)) yield return points;
    }

    private static void AssertGdbTraceLayerDescriptor(GeometryTopologyVectorSourceLayerResult descriptor,
        CatalogTreeNodeLocator node, GdbMapSourceIdentity expectedSource, GdbMapLayerDescriptor expectedLayer,
        GdbMapSourceLoadResponse reader, int expectedComponentCount)
    {
        if (!SameGdbTraceNodeLocator(descriptor.Node, node) || !descriptor.Checked || !descriptor.EffectiveVisible ||
            !descriptor.CanDeepConnect || descriptor.SourceKind != "gdb" ||
            !GdbTracePathEquals(descriptor.CanonicalSourcePath, expectedSource.SourcePath) ||
            descriptor.SourceFamily is not null || descriptor.SourceSHA256 is not null ||
            !SameGdbTraceIdentity(descriptor.GdbSource, expectedSource) ||
            descriptor.GdbLayer is null || !SameGdbTraceLayer(descriptor.GdbLayer, expectedLayer) ||
            descriptor.Components.Length != expectedComponentCount ||
            descriptor.Components.Any(component => component.Points.Length < 2) ||
            descriptor.OversizedComponentCount != 0 || descriptor.DiagnosticCode != "ready" || descriptor.Diagnostic is not null ||
            !GdbTraceComponentsMatchRead(descriptor.Components, reader.Data))
            throw new InvalidDataException("The exact GDB source descriptor did not match its checked persisted line reader: " + node.NodeName);
    }

    private static void AssertGdbTraceInspectionOwner(GeometryTopologyVectorSourceInspectResult inspection,
        string requestID, string projectPath, string projectRevision, string projectXmlSHA,
        string projectSessionID, MapGeometryOwner owner, string visualizationPath, string visualizationRevision)
    {
        GeometryTopologyVectorSourceMapIdentity captured = inspection.Owner;
        if (inspection.RequestID != requestID || !GdbTracePathEquals(captured.ProjectPath, projectPath) ||
            !captured.ProjectRevision.Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(captured.ProjectXmlSHA, projectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
            captured.ProjectSessionID != projectSessionID || captured.MapID != owner.MapID ||
            captured.MapIndex != owner.MapIndex || captured.MapName != owner.MapName || captured.MapType != owner.MapType ||
            !GdbTracePathEquals(captured.VisualizationPath, visualizationPath) ||
            !captured.VisualizationRevision.Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase) ||
            captured.CoreLayerContextIdentityToken is not null)
            throw new InvalidDataException("topology.vectorSource.inspect did not echo the exact plain project/session/map/.vis owner.");
    }

    private static async Task<GeometryGdbDeepConnectRejection> ExpectGdbTraceOperationFailureAsync(
        HttpClient client, string method, object parameters, string action, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("api/operations",
            new { method, parameters }, JsonOptions, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new HttpRequestException($"POST api/operations ({method}/{action}) returned {(int)response.StatusCode}: {body}");
        using JsonDocument submission = JsonDocument.Parse(body);
        string id = StringProperty(submission.RootElement, "id");
        long cursor = 0;
        while (true)
        {
            using JsonDocument snapshot = await GetJsonAsync(client,
                $"api/operations/{Uri.EscapeDataString(id)}?after={cursor}", cancellation);
            JsonElement root = snapshot.RootElement;
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            if (BooleanProperty(root, "hasMore")) continue;
            string state = StringProperty(root, "state");
            if (IsTerminal(state))
            {
                if (!string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(action + " was expected to fail but Host ended in " + state + ".");
                JsonElement error = PropertyOrNull(root, "error");
                string? code = OptionalString(error, "code");
                string message = OptionalString(error, "message") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(message))
                    throw new InvalidDataException(action + " failure omitted its source code or diagnostic.");
                return new(action, state, code, message);
            }
            await Task.Delay(50, cancellation);
        }
    }

    private static bool SameGdbTraceIdentity(GdbMapSourceIdentity? left, GdbMapSourceIdentity? right) =>
        left is not null && right is not null && GdbTracePathEquals(left.SourcePath, right.SourcePath) &&
        string.Equals(left.Revision, right.Revision, StringComparison.OrdinalIgnoreCase) &&
        left.Files.Length == right.Files.Length && left.Files.Zip(right.Files).All(pair =>
            pair.First.RelativePath == pair.Second.RelativePath && pair.First.Length == pair.Second.Length &&
            string.Equals(pair.First.Revision, pair.Second.Revision, StringComparison.OrdinalIgnoreCase));

    private static bool SameGdbTraceLayer(GdbMapLayerDescriptor? left, GdbMapLayerDescriptor right) =>
        left is not null && left.Name == right.Name && left.GeometryType == right.GeometryType &&
        left.GeometryTypeCode == right.GeometryTypeCode && left.VectorType == right.VectorType &&
        left.FeatureCount == right.FeatureCount && left.ProjectionWKT == right.ProjectionWKT;

    private static bool SameGdbTraceNodeLocator(CatalogTreeNodeLocator? left, CatalogTreeNodeLocator? right) =>
        left is not null && right is not null &&
        string.Equals(JsonSerializer.Serialize(left, JsonOptions), JsonSerializer.Serialize(right, JsonOptions),
            StringComparison.Ordinal);

    private static bool GdbTracePathEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
