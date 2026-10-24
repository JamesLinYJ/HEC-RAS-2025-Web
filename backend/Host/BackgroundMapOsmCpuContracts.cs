namespace Ras.Backend;

/// <summary>
/// A tile coordinate using the TMS row convention used by the renamed source
/// OSM service.
/// </summary>
public readonly record struct OsmCpuTileCoordinate(int Zoom, int Column, int TmsRow);

/// <summary>A point in project world coordinates, or in EPSG:900913 when the frame says so.</summary>
public readonly record struct OsmCpuPoint(double X, double Y);

/// <summary>RGBA color in the same channel order exposed to SwiftUI.</summary>
public readonly record struct OsmCpuColor(byte Red, byte Green, byte Blue, byte Alpha)
{
    public static OsmCpuColor Transparent => new(0, 0, 0, 0);

    internal static OsmCpuColor FromArgb(int argb) => new(
        (byte)(argb >> 16),
        (byte)(argb >> 8),
        (byte)argb,
        (byte)(argb >> 24));

    internal int ToArgb() => (Alpha << 24) | (Red << 16) | (Green << 8) | Blue;
}

/// <summary>
/// Configuration that affects the original MBTileReader's style zoom and
/// label visibility. The source uses a 4096-unit MVT extent and TMS tile rows.
/// </summary>
public sealed record OsmCpuRenderOptions
{
    public int Density { get; init; } = 1;
    public bool ShowLabels { get; init; } = true;
    public int TileSize { get; init; } = 4096;
    public double HalfWorldWidth { get; init; } = 20037508.34;
}

public enum OsmCpuCoordinateSpace
{
    WebMercator900913,
    ProjectWorld
}

public enum OsmCpuGeometryType
{
    Point,
    LineString,
    Polygon
}

/// <summary>A decoded path part. Polygon parts have IsClosed set by ClosePath.</summary>
public sealed record OsmCpuPathPart(OsmCpuPoint[] Points, bool IsClosed);

/// <summary>
/// Appearance resolved at the same effective style zoom used by the original
/// MBTileReader. FillColor and StrokeColor already include the source opacity
/// calculation.
/// </summary>
public sealed record OsmCpuAppearance(
    OsmCpuColor FillColor,
    OsmCpuColor StrokeColor,
    double StrokeWidth,
    double LineWidth,
    double LineGapWidth,
    bool IsDashed,
    float[] DashArray,
    bool IsTranslated,
    OsmCpuPoint Translation);

/// <summary>A style-matched geometry item ready for a native Canvas renderer.</summary>
public sealed record OsmCpuFeaturePath(
    int FeatureIndex,
    ulong? SourceFeatureId,
    string SourceLayer,
    string StyleLayerId,
    OsmCpuGeometryType GeometryType,
    int ZIndex,
    OsmCpuAppearance Appearance,
    OsmCpuPathPart[] Parts);

/// <summary>A source symbol label, with either a point anchor or a line-follow path.</summary>
public sealed record OsmCpuLabel(
    string Text,
    string SourceLayer,
    string StyleLayerId,
    OsmCpuPoint? Anchor,
    OsmCpuPoint[]? FollowPath,
    int ZIndex,
    string FontFamily,
    bool Bold,
    bool Italic,
    double FontSize,
    int MaximumWidth,
    OsmCpuColor Color,
    OsmCpuColor HaloColor,
    double HaloWidth);

/// <summary>
/// The CPU-decoded output for one real OSM vector tile. Geometry and labels
/// use one coordinate space for the entire frame.
/// </summary>
public sealed record OsmCpuTileFrame(
    OsmCpuTileCoordinate Tile,
    int StyleZoomLevel,
    OsmCpuCoordinateSpace CoordinateSpace,
    OsmCpuColor BackgroundColor,
    OsmCpuPoint[] ClipPolygon,
    OsmCpuFeaturePath[] Features,
    OsmCpuLabel[] Labels);

/// <summary>
/// Parsed source style. Retain and reuse this value for all tiles using the
/// same style JSON; it contains no network or platform state.
/// </summary>
public sealed class OsmCpuStyleDocument
{
    internal OsmCpuStyleLayer[] Layers { get; }

    public OsmCpuColor BackgroundColor { get; }

    internal OsmCpuStyleDocument(OsmCpuColor backgroundColor, OsmCpuStyleLayer[] layers)
    {
        BackgroundColor = backgroundColor;
        Layers = layers;
    }
}

internal enum OsmCpuStyleType
{
    Background,
    Fill,
    Line,
    Symbol
}

internal enum OsmCpuTextTransform
{
    None,
    Uppercase,
    Lowercase
}

internal sealed record OsmCpuStyleLayer(
    string Id,
    string SourceLayer,
    OsmCpuStyleType Type,
    int MinimumZoom,
    int MaximumZoom,
    int ZIndex,
    bool IsCasing,
    OsmCpuFilter? Filter,
    OsmCpuStylePaint Paint);

internal sealed record OsmCpuStylePaint(
    OsmCpuColor FillColor,
    OsmCpuColor StrokeColor,
    SortedDictionary<int, int>? FillColors,
    SortedDictionary<int, int>? StrokeColors,
    SortedDictionary<int, double> Size,
    SortedDictionary<int, double> GapWidth,
    SortedDictionary<int, int> Opacity,
    bool IsDashed,
    float[] DashArray,
    bool IsTranslated,
    SortedDictionary<int, float[]> Translation,
    string FontFamily,
    bool Bold,
    bool Italic,
    OsmCpuTextTransform TextTransform,
    int MaximumTextWidth,
    float HaloWidth,
    OsmCpuColor HaloColor);

internal sealed record OsmCpuFilter(OsmCpuFilterNode Root);

internal abstract record OsmCpuFilterNode;
internal sealed record OsmCpuFilterGroup(string Operator, OsmCpuFilterNode[] Children) : OsmCpuFilterNode;
internal sealed record OsmCpuFilterCondition(string Operator, object?[] Arguments) : OsmCpuFilterNode;

