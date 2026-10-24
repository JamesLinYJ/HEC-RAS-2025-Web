namespace Ras.Backend;

/// <summary>Connects to the source application's -stream=&lt;pipe&gt; endpoint.</summary>
public sealed record StreamModeConnectRequest(string PipeName, Guid SessionID);

/// <summary>
/// Opaque reference to the exact source .stream record for one streamed TIN.
/// The asset ID is random and the file is stored below the application's private
/// support directory; callers cannot supply a filesystem path.
/// </summary>
public sealed record StreamModeTinAssetReference(Guid SessionID, long Sequence, Guid AssetID, string Sha256);

public sealed record StreamModeTinOptionAssignment(string Name, string Value);

public sealed record StreamModeTinAssetRequest(StreamModeTinAssetReference Asset);

/// <summary>Releases every replayable TIN record owned by one completed stream session.</summary>
public sealed record StreamModeTinSessionReleaseRequest(Guid SessionID);

public sealed record StreamModeTinOptionsRequest(
    StreamModeTinAssetReference[] Assets,
    int SelectedProfileIndex);

public sealed record StreamModeTinOptionDescriptor(
    string Name,
    string Label,
    string Description,
    string ValueKind,
    string CurrentValue,
    string DefaultValue,
    string[] EnumValues,
    string? EnabledIf,
    bool IsEnabled,
    bool IsDebug,
    string SourceEffect);

public sealed record StreamModeTinOptionsResult(
    StreamModeTinAssetReference[] Assets,
    int SelectedProfileIndex,
    StreamModeTinOptionDescriptor[] Options);

public sealed record StreamModeTinViewport(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    int PixelWidth,
    int PixelHeight);

public sealed record StreamModeTinRenderRequest(
    StreamModeTinAssetReference[] Assets,
    int SelectedProfileIndex,
    Guid RequestID,
    StreamModeTinViewport Viewport,
    StreamModeTinOptionAssignment[] Options);

public sealed record StreamModeTinLabel(string Text, double X, double Y, double Rotation);

/// <summary>Rendered source pixels use straight-alpha RGBA from the retained PlotRequest buffer.</summary>
public sealed record StreamModeTinRenderResult(
    StreamModeTinAssetReference[] Assets,
    int SelectedProfileIndex,
    Guid RequestID,
    int Width,
    int Height,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    string RgbaBase64,
    StreamModeTinLabel[] Labels);

public sealed record StreamModeTinAssetReleaseResult(bool Released);

public sealed record StreamModeTinSessionReleaseResult(bool Released);

public sealed record StreamModeCoordinate(double X, double Y, double? Z = null, double? M = null);

/// <summary>
/// One actual retained IStreamableObject converted to native map primitives.
/// Coordinate indices, paths, and triangles preserve source geometry order.
/// </summary>
public sealed record StreamModeMapFrame(
    long Sequence,
    string ObjectType,
    int CoordinateCount,
    StreamModeCoordinate[] Coordinates,
    int[][] Paths,
    int[][] Triangles,
    bool IsClosed,
    bool HasZ = false,
    bool HasM = false,
    double? ZFillValue = null,
    double? MFillValue = null,
    StreamModeTinAssetReference? TinAsset = null);

public sealed record StreamModeConnectResult(string PipeName, long ObjectsReceived);
