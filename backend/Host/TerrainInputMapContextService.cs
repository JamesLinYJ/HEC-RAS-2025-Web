using Utility.Files;

namespace Ras.Backend;

public sealed record TerrainInputMapContextRequest(MapTreeRequest Map, string InputNodeID,
    string? ExpectedSourceClass = null, string? ExpectedTerrainPath = null,
    TerrainInputMapRenderOptions? RenderOptions = null,
    int? ExpectedInputIndex = null, string? ExpectedTerrainRevision = null);
public sealed record TerrainInputMapRenderOptions(RasterMapViewExtent Viewport,
    bool ShowProfileStations = false, bool ShowTin = false, bool DebugMode = false,
    double RenderScale = 1.0, TerrainInputMapTinRenderOptions? TinOptions = null);
public sealed record TerrainInputMapTinRenderOptions(string SurfaceFillMode,
    bool RenderEdges, bool RenderPoints, bool RenderDeletedTriangles,
    bool LabelTriangles, bool LabelEdges, bool LabelPoints);
public sealed record TerrainInputMapTinPlotOptionDescriptor(string PropertyName, string Name,
    string Label, string Description, string Value, bool IsEnabled, bool HasDrawingImplementation);
public sealed record TerrainInputMapContextFeature(string Id, string Kind, TerrainMapCoordinate[] Points, bool Closed);
public sealed record TerrainInputMapContextData(string InputNodeID, int InputIndex, string TerrainPath, string TerrainRevision,
    string SourceClass, TerrainInputMapContextFeature[] Features,
    TerrainInputMapLeveeRendererData? LeveeRenderer = null);
public sealed record TerrainInputMapLeveeRendererData(RasterMapViewExtent Viewport,
    MapSourceRasterFrame? ProfileStations, MapSourceRasterFrame? Tin,
    int TinFeatureCount, string? TinWarning,
    TerrainInputMapTinPlotOptionDescriptor[] TinPlotOptions);

/// <summary>
/// Reads only the selected terrain input's source vector render layer. It does
/// not sample or substitute the parent Terrain raster. The map tree request
/// proves that the input is still present under the captured saved map before
/// CatalogTreeService resolves the same source input and its project CRS.
/// </summary>
public static class TerrainInputMapContextService
{
    private static readonly HashSet<string> RenderableSourceClasses = new(StringComparer.Ordinal)
    {
        "ChannelModTerrainInputParentVC",
        "LeveeModTerrainInputVC",
        "PolygonRasterModTerrainInputVisualContext"
    };

    public static TerrainInputMapContextData Render(TerrainInputMapContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        if (string.IsNullOrWhiteSpace(request.InputNodeID) ||
            !request.InputNodeID.StartsWith("terrain-input:", StringComparison.Ordinal))
            throw new ArgumentException("Choose a source terrain input from the current map tree.");

        MapTreeSnapshot mapTree = MapTreeService.Load(request.Map);
        CatalogTreeNode[] matchingNodes = mapTree.Roots.SelectMany(Descendants)
            .Where(node => string.Equals(node.Id, request.InputNodeID, StringComparison.Ordinal)).ToArray();
        if (matchingNodes.Length != 1) throw new SourceFileChangedException();
        CatalogTreeNode selected = matchingNodes[0];
        if (selected.Locator?.NodeType != "TerrainInput" ||
            !RenderableSourceClasses.Contains(selected.SourceClass) ||
            string.IsNullOrWhiteSpace(selected.AssetID))
            throw new NotSupportedException("This terrain input does not expose a source map render layer.");
        if (request.ExpectedSourceClass is { Length: > 0 } expectedSourceClass &&
            !string.Equals(expectedSourceClass, selected.SourceClass, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        if (request.ExpectedTerrainPath is { Length: > 0 } expectedTerrainPath &&
            !SourceFilePaths.Canonical(expectedTerrainPath).Equals(SourceFilePaths.Canonical(selected.AssetID),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new SourceFileChangedException();

        var projectRequest = new CatalogTreeRequest(request.Map.ProjectPath,
            request.Map.ProjectXml, Mode: "Project", ProjectSessionID: request.Map.ProjectSessionID,
            ExternalCoreLayerReferences: request.Map.ExternalCoreLayerReferences,
            CoreLayerContext: request.Map.CoreLayerContext);
        TerrainInputMapContextData data = CatalogTreeService.ReadTerrainInputMapContext(
            projectRequest, request.InputNodeID, selected.SourceClass, selected.AssetID,
            request.RenderOptions);
        if (request.ExpectedInputIndex is int expectedInputIndex && data.InputIndex != expectedInputIndex ||
            request.ExpectedTerrainRevision is { Length: > 0 } expectedTerrainRevision &&
            !string.Equals(data.TerrainRevision, expectedTerrainRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (!string.Equals(data.InputNodeID, selected.Id, StringComparison.Ordinal) ||
            !string.Equals(data.SourceClass, selected.SourceClass, StringComparison.Ordinal) ||
            !SourceFilePaths.Canonical(data.TerrainPath).Equals(SourceFilePaths.Canonical(selected.AssetID),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new SourceFileChangedException();

        MapTreeSnapshot finalTree = MapTreeService.Load(request.Map with
        { ExpectedVisualizationRevision = mapTree.VisualizationRevision });
        CatalogTreeNode[] finalMatches = finalTree.Roots.SelectMany(Descendants)
            .Where(node => string.Equals(node.Id, request.InputNodeID, StringComparison.Ordinal)).ToArray();
        if (finalTree.VisualizationRevision != mapTree.VisualizationRevision || finalMatches.Length != 1 ||
            finalMatches[0].Locator?.NodeType != "TerrainInput" ||
            !string.Equals(finalMatches[0].SourceClass, selected.SourceClass, StringComparison.Ordinal) ||
            !SourceFilePaths.Canonical(finalMatches[0].AssetID ?? string.Empty).Equals(
                SourceFilePaths.Canonical(selected.AssetID),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return data;
    }

    public static TerrainMapCoordinate[] ToTerrainSourceCoordinates(TerrainInputMapContextRequest request,
        int expectedInputIndex, string expectedTerrainRevision, TerrainMapCoordinate[] mapCoordinates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mapCoordinates);
        request = request with { ExpectedInputIndex = expectedInputIndex,
            ExpectedTerrainRevision = expectedTerrainRevision };
        TerrainInputMapContextData capture = Render(request with { RenderOptions = null });
        if (capture.InputIndex != expectedInputIndex ||
            !string.Equals(capture.TerrainRevision, expectedTerrainRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        var projectRequest = new CatalogTreeRequest(request.Map.ProjectPath, request.Map.ProjectXml,
            Mode: "Project", ProjectSessionID: request.Map.ProjectSessionID,
            ExternalCoreLayerReferences: request.Map.ExternalCoreLayerReferences,
            CoreLayerContext: request.Map.CoreLayerContext);
        TerrainMapCoordinate[] transformed = CatalogTreeService.TransformTerrainInputCoordinates(
            projectRequest, request.InputNodeID, capture.SourceClass, capture.TerrainPath,
            capture.InputIndex, mapCoordinates, projectToSource: true);
        MapTreeSnapshot finalTree = MapTreeService.Load(request.Map);
        if (!string.Equals(finalTree.VisualizationRevision,
                request.Map.ExpectedVisualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return transformed;
    }

    public static TerrainMapCoordinate[] ToProjectMapCoordinates(TerrainInputMapContextRequest request,
        int expectedInputIndex, string expectedTerrainRevision, TerrainMapCoordinate[] sourceCoordinates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceCoordinates);
        request = request with { ExpectedInputIndex = expectedInputIndex,
            ExpectedTerrainRevision = expectedTerrainRevision };
        TerrainInputMapContextData capture = Render(request with { RenderOptions = null });
        var projectRequest = new CatalogTreeRequest(request.Map.ProjectPath, request.Map.ProjectXml,
            Mode: "Project", ProjectSessionID: request.Map.ProjectSessionID,
            ExternalCoreLayerReferences: request.Map.ExternalCoreLayerReferences,
            CoreLayerContext: request.Map.CoreLayerContext);
        TerrainMapCoordinate[] transformed = CatalogTreeService.TransformTerrainInputCoordinates(
            projectRequest, request.InputNodeID, capture.SourceClass, capture.TerrainPath,
            capture.InputIndex, sourceCoordinates, projectToSource: false);
        MapTreeSnapshot finalTree = MapTreeService.Load(request.Map);
        if (!string.Equals(finalTree.VisualizationRevision,
                request.Map.ExpectedVisualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return transformed;
    }

    public static double ToTerrainSourceCellSize(TerrainInputMapContextRequest request,
        int expectedInputIndex, string expectedTerrainRevision, TerrainMapCoordinate mapPoint, double mapCellSize)
    {
        if (!double.IsFinite(mapPoint.X) || !double.IsFinite(mapPoint.Y) ||
            !double.IsFinite(mapCellSize) || mapCellSize <= 0)
            throw new ArgumentException("A polygon selection needs a finite map point and cell size.");
        TerrainMapCoordinate[] source = ToTerrainSourceCoordinates(request, expectedInputIndex,
            expectedTerrainRevision, [mapPoint,
                new TerrainMapCoordinate(mapPoint.X + mapCellSize, mapPoint.Y),
                new TerrainMapCoordinate(mapPoint.X, mapPoint.Y + mapCellSize)]);
        double xDistance = double.Hypot(source[1].X - source[0].X, source[1].Y - source[0].Y);
        double yDistance = double.Hypot(source[2].X - source[0].X, source[2].Y - source[0].Y);
        double sourceCellSize = Math.Max(xDistance, yDistance);
        return double.IsFinite(sourceCellSize) && sourceCellSize > 0 ? sourceCellSize
            : throw new InvalidDataException("The map selection tolerance cannot be transformed to the terrain projection.");
    }

    private static IEnumerable<CatalogTreeNode> Descendants(CatalogTreeNode node)
    {
        yield return node;
        foreach (CatalogTreeNode child in node.Children ?? [])
            foreach (CatalogTreeNode descendant in Descendants(child)) yield return descendant;
    }
}
