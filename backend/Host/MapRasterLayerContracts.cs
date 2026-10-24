namespace Ras.Backend;

/// <summary>Reads the retained GDAL dataset and resolves the exact original map context type.</summary>
public sealed record MapRasterLayerInspectRequest(string SourcePath);

public sealed record MapRasterLayerInspectResult(
    string SourcePath,
    string SourceCanonicalPath,
    string SourceRevision,
    string Driver,
    string DataType,
    int Width,
    int Height,
    int BandCount,
    string MapLayerType,
    string[] SupportedMapLayerTypes,
    string? ProjectionWKT,
    string? UnitSystem,
    double CellSizeX,
    bool CanRenderPreview,
    RasterImportContextInfo Info,
    RasterImportContextStats Stats);

public sealed record MapRasterLayerPreviewRequest(
    string SourcePath,
    string SourceRevision,
    string MapLayerType,
    string ProjectPath,
    string? ProjectXml,
    string? ProjectXmlSHA,
    RasterMapViewExtent? Viewport = null,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record MapRasterLayerImportRequest(
    MapDocumentRequest Map,
    string SourcePath,
    string SourceRevision,
    string MapLayerType,
    string ExpectedProjectRevision,
    string ExpectedSourceCanonicalPath);

public sealed record MapRasterLayerReadRequest(
    RasterMapSourceIdentity Source,
    string? ProjectXml,
    RasterMapViewExtent? Viewport = null);

public sealed record MapRasterLayerReadResult(
    string PreviousVisualizationRevision,
    string VisualizationRevision,
    string SourceRevision,
    RasterMapLayerPayload Raster,
    RasterImportContextSource? RasterInfoStats = null);
