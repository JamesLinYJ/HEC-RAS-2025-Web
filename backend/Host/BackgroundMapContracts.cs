namespace Ras.Backend;

public sealed record BackgroundMapSettings(
    bool AreBackgroundMapsOn,
    bool IsOpenStreetMapOn,
    bool IsSatelliteImageryOn,
    bool IsShadedReliefOn,
    string OsmStyle,
    int Density,
    bool ShowLabels,
    double SatelliteOpacity);

public sealed record BackgroundMapRenderRequest(
    string RequestID,
    string MapID,
    string ProjectSessionID,
    string ProjectionType,
    string ProjectionValue,
    RasterMapViewExtent Viewport,
    double WorldUnitsPerPixel,
    BackgroundMapSettings Settings);

public sealed record BackgroundMapRaster(
    string ID,
    string Source,
    int Width,
    int Height,
    byte[] Rgba,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    double Opacity);

public sealed record BackgroundMapPathPart(BackgroundMapCoordinate[] Points, bool IsClosed);

public sealed record BackgroundMapCoordinate(double X, double Y);

public sealed record BackgroundMapFeature(
    string ID,
    string SourceLayer,
    string StyleLayer,
    string GeometryType,
    BackgroundMapPathPart[] Parts,
    string? StrokeColor,
    string? FillColor,
    double StrokeOpacity,
    double FillOpacity,
    double StrokeWidth,
    double LineWidth,
    double LineGapWidth,
    bool IsDashed,
    double[] Dash,
    int ZIndex,
    BackgroundMapCoordinate[] ClipPolygon,
    bool IsTranslated,
    double TranslationX,
    double TranslationY);

public sealed record BackgroundMapLabel(
    string ID,
    string Text,
    BackgroundMapCoordinate? Point,
    BackgroundMapCoordinate[] Line,
    string? FontFamily,
    double FontSize,
    double Rotation,
    string? TextColor,
    string? HaloColor,
    double HaloWidth,
    int ZIndex,
    bool Bold,
    bool Italic,
    int MaximumWidth);

public sealed record BackgroundMapSourceStatus(
    string ID,
    string State,
    string? Message,
    string? Attribution);

public sealed record BackgroundMapRenderResult(
    string RequestID,
    string MapID,
    string ProjectSessionID,
    RasterMapViewExtent Viewport,
    BackgroundMapRaster[] Rasters,
    BackgroundMapFeature[] Features,
    BackgroundMapLabel[] Labels,
    BackgroundMapSourceStatus[] Sources,
    string? OsmBackgroundColor = null);

public sealed record BackgroundMapStylesRequest(string RequestID);

public sealed record BackgroundMapStyle(
    string ID,
    string Name,
    string Description,
    string? PreviewContentType,
    byte[]? Preview,
    string? Warning);

public sealed record BackgroundMapStylesResult(
    string RequestID,
    BackgroundMapStyle[] Styles,
    string? Warning);

public sealed record BackgroundMapCacheRequest(bool Clear);

public sealed record BackgroundMapCacheResult(string Directory, long Bytes, bool CanClear, bool Cleared);
