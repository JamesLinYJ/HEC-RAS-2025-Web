using System.IO.Compression;
using System.Security.Cryptography;
using Google.Protobuf;
using Geospatial.GDALAssist;
using Geospatial.Transformers;
using Geospatial.Vectors;
using VectorTile;

namespace Ras.Backend;

public static partial class BackgroundMapService
{
    /// <summary>
    /// Returns the exact OSM vector tile below a clicked project-world point.
    /// Tile bytes come through the same cache, endpoint, key and concurrency
    /// policy as normal background rendering.
    /// </summary>
    public static async Task<BackgroundMapTileJsonResult> GetTileJsonAsync(
        BackgroundMapTileJsonRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Capture);
        ArgumentNullException.ThrowIfNull(request.ClickedWorldPoint);
        ValidateRenderRequest(request.Capture);

        BackgroundMapRenderRequest capture = request.Capture;
        BackgroundMapCoordinate clickedPoint = request.ClickedWorldPoint;
        if (!double.IsFinite(clickedPoint.X) || !double.IsFinite(clickedPoint.Y))
            throw new ArgumentOutOfRangeException(nameof(request), "The clicked world coordinate must be finite.");
        if (clickedPoint.X < capture.Viewport.MinX || clickedPoint.X > capture.Viewport.MaxX ||
            clickedPoint.Y < capture.Viewport.MinY || clickedPoint.Y > capture.Viewport.MaxY)
            throw new ArgumentOutOfRangeException(nameof(request), "The clicked point is outside the captured map viewport.");

        if (!capture.Settings.AreBackgroundMapsOn || !capture.Settings.IsOpenStreetMapOn)
            return new(capture, clickedPoint, "osm", "disabled",
                "OpenStreetMap background tiles are not enabled for this captured map view.", null, null, null);

        Projection projectProjection = ResolveProjectedCRS(capture.ProjectionType, capture.ProjectionValue);
        using ReprojectionTransformer transformer = new(projectProjection, WellKnownProjections.WebMercator);
        Memory<Point> point = new Point[] { new(clickedPoint.X, clickedPoint.Y) };
        transformer.Transform(point, point);
        Point webMercatorPoint = point.Span[0];
        if (!double.IsFinite(webMercatorPoint.X) || !double.IsFinite(webMercatorPoint.Y))
            throw new InvalidOperationException("The clicked point could not be projected into the OSM tile grid.");

        BackgroundMapTileAddress? address = GetOsmTileAddress(
            ResolveOsmZoom(capture.WorldUnitsPerPixel, projectProjection, capture.Settings.Density),
            webMercatorPoint.X, webMercatorPoint.Y);
        if (address is null)
            return new(capture, clickedPoint, "osm", "outsideCoverage",
                "The clicked point is outside OSM Web Mercator tile coverage.", null, null, null);

        cancellationToken.ThrowIfCancellationRequested();
        OsmCpuTileCoordinate tile = new(address.Zoom, address.Column, address.TmsRow);
        byte[] compressedTile = await GetOsmTileAsync(tile, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return DecodeTileJson(request, tile, compressedTile);
    }

    internal static int ResolveOsmZoom(double worldUnitsPerPixel, Projection projectProjection, int density)
    {
        double cellSizeInMeters = worldUnitsPerPixel;
        if (projectProjection.TryParseUnitSystem(out Utility.Units.UnitSystem units) &&
            units == Utility.Units.UnitSystem.USCustomary)
            cellSizeInMeters *= 0.3048;

        List<double> levels = new(20);
        double nextLevel = (HalfWorldWidth * 2.0) / OsmTileSize;
        for (int index = 0; index < 20; index++)
        {
            levels.Add(nextLevel);
            nextLevel /= 2.0;
        }
        int sourceZoom = levels.FindIndex(level => cellSizeInMeters > level);
        if (sourceZoom < 0) sourceZoom = levels.Count - 1;
        return Math.Clamp(sourceZoom + density, 0, 14);
    }

    internal static BackgroundMapTileAddress? GetOsmTileAddress(int zoom, double webMercatorX, double webMercatorY)
    {
        if (zoom is < 0 or > 14)
            throw new ArgumentOutOfRangeException(nameof(zoom));
        if (!double.IsFinite(webMercatorX))
            throw new ArgumentOutOfRangeException(nameof(webMercatorX));
        if (!double.IsFinite(webMercatorY))
            throw new ArgumentOutOfRangeException(nameof(webMercatorY));
        if (webMercatorX < -HalfWorldWidth || webMercatorX > HalfWorldWidth ||
            webMercatorY < -HalfWorldWidth || webMercatorY > HalfWorldWidth)
            return null;

        int count = 1 << zoom;
        return new BackgroundMapTileAddress(zoom,
            TileIndex(webMercatorX, count), TileIndex(webMercatorY, count));
    }

    internal static BackgroundMapTileJsonResult DecodeTileJson(BackgroundMapTileJsonRequest request,
        OsmCpuTileCoordinate tile, ReadOnlyMemory<byte> compressedTile)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Capture);
        ArgumentNullException.ThrowIfNull(request.ClickedWorldPoint);
        int tilesPerAxis = tile.Zoom is >= 0 and <= 14 ? 1 << tile.Zoom : 0;
        if (tilesPerAxis == 0 || tile.Column < 0 || tile.Column >= tilesPerAxis ||
            tile.TmsRow < 0 || tile.TmsRow >= tilesPerAxis)
            throw new ArgumentOutOfRangeException(nameof(tile));
        if (compressedTile.Length <= 2 || compressedTile.Span[0] != 0x1F || compressedTile.Span[1] != 0x8B)
            throw new InvalidDataException("The OSM tile service did not return a gzip-compressed vector tile.");

        TileParser parsedTile;
        using (MemoryStream compressed = new(compressedTile.ToArray(), writable: false))
        using (GZipStream decompressed = new(compressed, CompressionMode.Decompress, leaveOpen: false))
            parsedTile = TileParser.Parser.ParseFrom(decompressed);

        string rawJson = JsonFormatter.Default.Format(parsedTile);
        string revision = Convert.ToHexString(SHA256.HashData(compressedTile.Span));
        return new(request.Capture, request.ClickedWorldPoint, "osm", "available", null,
            new BackgroundMapTileAddress(tile.Zoom, tile.Column, tile.TmsRow), revision, rawJson);
    }
}
