namespace Ras.Backend;

/// <summary>
/// Inspects the original OSM vector tile under a clicked project-world point.
/// Capture is the exact map viewport and source configuration used by the
/// background-map renderer.
/// </summary>
public sealed record BackgroundMapTileJsonRequest(
    BackgroundMapRenderRequest Capture,
    BackgroundMapCoordinate ClickedWorldPoint);

public sealed record BackgroundMapTileAddress(int Zoom, int Column, int TmsRow);

public sealed record BackgroundMapTileJsonResult(
    BackgroundMapRenderRequest Capture,
    BackgroundMapCoordinate ClickedWorldPoint,
    string SourceID,
    string Status,
    string? Message,
    BackgroundMapTileAddress? Tile,
    string? SourceRevision,
    string? RawJson);
