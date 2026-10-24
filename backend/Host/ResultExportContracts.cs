using System.Text.Json.Serialization;
using Ras.Layers;

namespace Ras.Backend;

public sealed record RasterRgbExportInspectRequest(RasterMapSourceIdentity Source,
    string? ProjectXml, RasterMapViewExtent? CurrentView);

public sealed record RasterRgbExportInspection(string SourceID, string SourceName,
    string SourceRevision, string VisualizationRevision,
    double FullMinX, double FullMinY, double FullMaxX, double FullMaxY,
    RasterMapViewExtent? CurrentView, double? SuggestedCellSize,
    RasterPlotOptionDescriptor[] PlotOptions);

public sealed record RasterRgbExportPreviewRequest(RasterMapSourceIdentity Source,
    string? ProjectXml, string Region, double CellSize, RasterMapViewExtent? CurrentView,
    Dictionary<string, string>? PlotOptions = null);

public sealed record RasterRgbExportPreview(string SourceID, string SourceRevision,
    string VisualizationRevision, double CellSize,
    double MinX, double MinY, double MaxX, double MaxY,
    int OutputWidth, int OutputHeight, long EstimatedFileSizeBytes,
    RasterMapLayerPayload Raster);

public sealed record RasterRgbExportExecuteRequest(RasterMapSourceIdentity Source,
    string? ProjectXml, string Region, double CellSize, RasterMapViewExtent? CurrentView,
    string OutputPath, Dictionary<string, string>? PlotOptions = null);

public sealed record ExportArtifactReceipt(string Path, long SizeBytes,
    [property: JsonPropertyName("sha256")] string SHA256);

public sealed record RasterRgbExportExecuteResult(string State, string? OutputPath,
    string SourceID, string SourceRevision, string VisualizationRevision,
    double CellSize, int Width, int Height,
    double MinX, double MinY, double MaxX, double MaxY,
    ExportArtifactReceipt[] Artifacts);

public sealed record DepthShapefileExportInspectRequest(MapTreeRequest Map,
    MapResultSelectionOverride[] SelectionOverrides, string ContextToken);

public sealed record DepthShapefileExportProfile(int Index, string Name, string Time);

public sealed record DepthShapefileExportOwner(CatalogTreeNodeLocator Node,
    string NodeKind, string Name, int SelectionIndex,
    string ResultPath, string ResultRevision, string ResultName,
    int? CurrentProfileIndex, DepthShapefileExportProfile[] Profiles);

public sealed record DepthShapefileExportInspection(string MapID, string ContextToken,
    double DefaultCellSize, double DefaultSimplifyDistance,
    DepthShapefileExportOwner[] Owners);

public sealed record DepthShapefileExportPreviewRequest(MapTreeRequest Map,
    MapResultSelectionOverride[] SelectionOverrides, string ContextToken,
    CatalogTreeNodeLocator ResultNode, int SelectionIndex,
    string ResultPath, string ResultRevision, int ProfileIndex,
    RasterMapViewExtent Viewport, double CellSize, double SimplifyDistance);

public sealed record DepthShapefileExportPreview(string MapID, string ContextToken,
    CatalogTreeNodeLocator ResultNode, int SelectionIndex, string ResultPath,
    string ResultRevision, int ProfileIndex, string ProfileName, string ProfileTime,
    RasterMapViewExtent Viewport, double CellSize, double SimplifyDistance,
    MapResultRenderLayerPayload? ResultRaster,
    GeometryTopologyCoordinate[][] Polygons,
    GeometryTopologyCoordinate[][] Polylines);

public sealed record DepthShapefileExportExecuteRequest(MapTreeRequest Map,
    MapResultSelectionOverride[] SelectionOverrides, string ContextToken,
    CatalogTreeNodeLocator ResultNode, int SelectionIndex,
    string ResultPath, string ResultRevision, int ProfileIndex,
    double CellSize, double SimplifyDistance, string OutputPath);

public sealed record DepthShapefileExportExecuteResult(string State,
    string? OutputPath, string MapID, string ContextToken,
    CatalogTreeNodeLocator ResultNode, int SelectionIndex,
    string ResultPath, string ResultRevision, int ProfileIndex,
    string ProfileName, string ProfileTime,
    double CellSize, double SimplifyDistance, int PolygonCount,
    ExportArtifactReceipt[] Artifacts);
