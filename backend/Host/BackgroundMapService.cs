using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Geospatial.GDALAssist;
using Geospatial.IO.TileMapService;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Recolor;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Ras.Layers;

namespace Ras.Backend;

/// <summary>
/// The source RasMap background pipeline: the retained network pyramid tile
/// providers render satellite and USGS imagery, while the renamed OSM style
/// and TMS vector-tile routes feed the CPU tile decoder and native map canvas.
/// </summary>
public static partial class BackgroundMapService
{
    private const string OsmBaseUrl = "https://osm.hecdev.net";
    private const string OsmTileKey = "ras010_65fd3e26933b";
    private const string OsmAttribution = "Map data © OpenStreetMap contributors.";
    private const int MaximumRasterSide = 2048;
    private const long MaximumRasterPixels = 2_097_152;
    private const int MaximumOsmTilesPerView = 256;
    private const int MaximumConcurrentOsmRequests = 5;
    private const double HalfWorldWidth = 20037508.34;
    private const int OsmTileSize = 4096;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private static readonly SemaphoreSlim OsmRequestGate = new(MaximumConcurrentOsmRequests);
    private static readonly ConcurrentDictionary<string, OsmCpuStyleDocument> ParsedStyles = new(StringComparer.Ordinal);
    private static readonly BackgroundMapTileDiskCache OsmTileCache =
        new(Path.Combine(Path.GetTempPath(), "OSMTiles"));

    private static readonly TimeSpan StyleCatalogBudget = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SingleStyleBudget = TimeSpan.FromSeconds(60);
    private const int MaximumStyleCount = 32;
    private const int MaximumStyleCatalogBytes = 128 * 1024;
    private const int MaximumStylePreviewBytes = 1024 * 1024;
    private const long MaximumTotalStylePreviewBytes = 4L * 1024 * 1024;
    private const int MaximumStyleDescriptionBytes = 4 * 1024;
    private const int MaximumConcurrentStyleRequests = 4;

    public static Task<BackgroundMapStylesResult> GetStylesAsync(BackgroundMapStylesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetStylesAsync(request, Http, OsmBaseUrl,
            request.StyleID is null ? StyleCatalogBudget : SingleStyleBudget, cancellationToken);
    }

    // Injectable transport and budget also allow deterministic protocol checks without a live service.
    internal static async Task<BackgroundMapStylesResult> GetStylesAsync(BackgroundMapStylesRequest request,
        HttpClient http, string baseUrl, TimeSpan budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RequestID))
            throw new ArgumentException("A style-list request identity is required.", nameof(request));
        if (request.StyleID is not null && (!IsSafeStyleID(request.StyleID) || request.CatalogOnly))
            throw new ArgumentException("A selected style ID must be valid and cannot be combined with catalog-only mode.", nameof(request));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget);
        CancellationToken token = deadline.Token;
        string[] styleIDs;
        string? warning = null;
        try
        {
            using HttpResponseMessage response = await http.GetAsync(baseUrl + "/styles",
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            byte[] body = await ReadStyleBodyAsync(response.Content, MaximumStyleCatalogBytes, token).ConfigureAwait(false);
            styleIDs = JsonSerializer.Deserialize<string[]>(body) ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException or OperationCanceledException)
        {
            return new(request.RequestID, [], DescribeHttpFailure(exception));
        }

        if (request.StyleID is not null)
        {
            if (!styleIDs.Contains(request.StyleID, StringComparer.Ordinal))
                return new(request.RequestID, [], "The selected style was not returned by the OSM service.");
            styleIDs = [request.StyleID];
        }
        else if (styleIDs.Length > MaximumStyleCount)
        {
            styleIDs = styleIDs[..MaximumStyleCount];
            warning = "The OSM service returned too many style identifiers; the list was truncated.";
        }
        string[] safeIDs = styleIDs.Where(IsSafeStyleID).ToArray();
        if (safeIDs.Length != styleIDs.Length)
            warning ??= "The OSM service returned an invalid style identifier.";
        cancellationToken.ThrowIfCancellationRequested();
        if (request.CatalogOnly)
            return new(request.RequestID, safeIDs.Select(id => new BackgroundMapStyle(id, HumanizeStyleID(id),
                string.Empty, null, null, null)).ToArray(), warning);
        using var gate = new SemaphoreSlim(MaximumConcurrentStyleRequests);
        long totalPreviewBytes = 0;
        Task<BackgroundMapStyle>[] requests = safeIDs.Select(async styleID =>
        {
            byte[]? preview = null;
            string? description = null;
            string? styleWarning = null;
            try
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    string path = baseUrl + "/styles/" + Uri.EscapeDataString(styleID);
                    using HttpResponseMessage image = await http.GetAsync(path + "/image",
                        HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (image.StatusCode != HttpStatusCode.NoContent)
                    {
                        image.EnsureSuccessStatusCode();
                        preview = await ReadStyleBodyAsync(image.Content, MaximumStylePreviewBytes, token).ConfigureAwait(false);
                        if (Interlocked.Add(ref totalPreviewBytes, preview.Length) > MaximumTotalStylePreviewBytes)
                        {
                            Interlocked.Add(ref totalPreviewBytes, -preview.Length);
                            preview = null;
                            throw new IOException("The OSM preview total exceeds the size limit.");
                        }
                    }
                    using HttpResponseMessage text = await http.GetAsync(path + "/description",
                        HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    text.EnsureSuccessStatusCode();
                    description = System.Text.Encoding.UTF8.GetString(
                        await ReadStyleBodyAsync(text.Content, MaximumStyleDescriptionBytes, token).ConfigureAwait(false));
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
            {
                styleWarning = DescribeHttpFailure(exception);
            }
            return new BackgroundMapStyle(styleID, HumanizeStyleID(styleID), description ?? string.Empty,
                preview is { Length: > 0 } ? "image/*" : null, preview, styleWarning);
        }).ToArray();
        BackgroundMapStyle[] styles = await Task.WhenAll(requests).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        warning ??= styles.Select(style => style.Warning).FirstOrDefault(value => value is not null);
        return new(request.RequestID, styles, warning);
    }

    private static async Task<byte[]> ReadStyleBodyAsync(HttpContent content, int maximumBytes, CancellationToken token)
    {
        if (content.Headers.ContentLength is long length && length > maximumBytes)
            throw new IOException("The OSM response exceeds the size limit.");
        await using Stream input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > maximumBytes) throw new IOException("The OSM response exceeds the size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public static BackgroundMapCacheResult Cache(BackgroundMapCacheRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        (long bytes, bool canClear, bool cleared) = OsmTileCache.InspectOrClear(request.Clear);
        return new(OsmTileCache.DirectoryPath, bytes, canClear, cleared);
    }

    public static async Task<BackgroundMapRenderResult> RenderAsync(BackgroundMapRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRenderRequest(request);

        Projection projectProjection = ResolveProjectedCRS(request.ProjectionType, request.ProjectionValue);
        Extent viewportExtent = new(request.Viewport.MinX, request.Viewport.MaxX,
            request.Viewport.MinY, request.Viewport.MaxY);
        List<BackgroundMapRaster> rasters = [];
        List<BackgroundMapFeature> features = [];
        List<BackgroundMapLabel> labels = [];
        List<BackgroundMapSourceStatus> statuses = [];
        string? osmBackgroundColor = null;

        if (!request.Settings.AreBackgroundMapsOn)
        {
            return new(request.RequestID, request.MapID, request.ProjectSessionID, request.Viewport,
                [], [], [], [
                    new("osm", "disabled", null, OsmAttribution),
                    new("satellite", "disabled", null, null),
                    new("shadedRelief", "disabled", null, null)
                ]);
        }

        if (request.Settings.IsShadedReliefOn)
        {
            try
            {
                BackgroundMapRaster? raster = RenderNetworkRaster("shadedRelief", "USGS Shaded Relief",
                    NetworkTileProvider.ShadedReliefInstance, projectProjection, request.Viewport, 1.0, true);
                if (raster is null)
                    statuses.Add(new("shadedRelief", "outsideCoverage", "The current map extent is outside the USGS tile coverage.", "USGS Shaded Relief"));
                else
                {
                    rasters.Add(raster);
                    statuses.Add(new("shadedRelief", "available", null, "USGS Shaded Relief"));
                }
            }
            catch (Exception exception) when (IsRenderFailure(exception))
            {
                statuses.Add(new("shadedRelief", "failed", DescribeSourceFailure(exception), "USGS Shaded Relief"));
            }
        }
        else statuses.Add(new("shadedRelief", "disabled", null, null));

        if (request.Settings.IsSatelliteImageryOn)
        {
            try
            {
                BackgroundMapRaster? raster = RenderNetworkRaster("satellite", "Google Satellite",
                    NetworkTileProvider.SatelliteInstance, projectProjection, request.Viewport,
                    request.Settings.SatelliteOpacity, false);
                if (raster is null)
                    statuses.Add(new("satellite", "outsideCoverage", "The current map extent is outside satellite tile coverage.", "Google Satellite"));
                else
                {
                    rasters.Add(raster);
                    statuses.Add(new("satellite", "available", null, "Google Satellite"));
                }
            }
            catch (Exception exception) when (IsRenderFailure(exception))
            {
                statuses.Add(new("satellite", "failed", DescribeSourceFailure(exception), "Google Satellite"));
            }
        }
        else statuses.Add(new("satellite", "disabled", null, null));

        if (request.Settings.IsOpenStreetMapOn)
        {
            try
            {
                OsmCpuStyleDocument style = await GetParsedStyleAsync(request.Settings.OsmStyle, cancellationToken)
                    .ConfigureAwait(false);
                osmBackgroundColor = EncodeColor(style.BackgroundColor);
                OsmCpuTileCoordinate[] tileCoordinates = GetVisibleOsmTiles(
                    viewportExtent, request.WorldUnitsPerPixel, projectProjection, request.Settings.Density);
                if (tileCoordinates.Length == 0)
                {
                    statuses.Add(new("osm", "outsideCoverage", "The current map extent is outside the OSM Web Mercator tile coverage.", OsmAttribution));
                    return new(request.RequestID, request.MapID, request.ProjectSessionID, request.Viewport,
                        rasters.ToArray(), features.ToArray(), labels.ToArray(), statuses.ToArray(), osmBackgroundColor);
                }

                OsmCpuTileFrame[] frames = await LoadOsmFramesAsync(tileCoordinates, style,
                    projectProjection, request.Settings, cancellationToken).ConfigureAwait(false);
                foreach (OsmCpuTileFrame frame in frames)
                {
                    BackgroundMapCoordinate[] clip = frame.ClipPolygon
                        .Select(point => new BackgroundMapCoordinate(point.X, point.Y)).ToArray();
                    foreach (OsmCpuFeaturePath feature in frame.Features)
                    {
                        OsmCpuAppearance appearance = feature.Appearance;
                        string identity = string.Join(":", frame.Tile.Zoom, frame.Tile.Column,
                            frame.Tile.TmsRow, feature.SourceLayer,
                            feature.SourceFeatureId?.ToString(CultureInfo.InvariantCulture) ?? feature.FeatureIndex.ToString(CultureInfo.InvariantCulture),
                            feature.StyleLayerId, feature.FeatureIndex);
                        features.Add(new(identity, feature.SourceLayer, feature.StyleLayerId,
                            feature.GeometryType.ToString(), feature.Parts.Select(part => new BackgroundMapPathPart(
                                part.Points.Select(point => new BackgroundMapCoordinate(point.X, point.Y)).ToArray(),
                                part.IsClosed)).ToArray(), EncodeColor(appearance.StrokeColor),
                            EncodeColor(appearance.FillColor), 1.0, 1.0, appearance.StrokeWidth,
                            appearance.LineWidth, appearance.LineGapWidth, appearance.IsDashed,
                            appearance.DashArray.Select(value => (double)value).ToArray(), feature.ZIndex,
                            clip, appearance.IsTranslated, appearance.Translation.X, appearance.Translation.Y));
                    }

                    foreach (OsmCpuLabel label in frame.Labels)
                    {
                        string identity = string.Join(":", frame.Tile.Zoom, frame.Tile.Column,
                            frame.Tile.TmsRow, label.SourceLayer, label.StyleLayerId,
                            label.Text, labels.Count.ToString(CultureInfo.InvariantCulture));
                        labels.Add(new(identity, label.Text,
                            label.Anchor is { } anchor ? new BackgroundMapCoordinate(anchor.X, anchor.Y) : null,
                            label.FollowPath?.Select(point => new BackgroundMapCoordinate(point.X, point.Y)).ToArray() ?? [],
                            label.FontFamily, label.FontSize, 0, EncodeColor(label.Color),
                            EncodeColor(label.HaloColor), label.HaloWidth, label.ZIndex,
                            label.Bold, label.Italic, label.MaximumWidth));
                    }
                }
                statuses.Add(new("osm", "available", null, OsmAttribution));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRenderFailure(exception))
            {
                osmBackgroundColor = null;
                statuses.Add(new("osm", "failed", DescribeSourceFailure(exception), OsmAttribution));
            }
        }
        else statuses.Add(new("osm", "disabled", null, OsmAttribution));

        return new(request.RequestID, request.MapID, request.ProjectSessionID, request.Viewport,
            rasters.ToArray(), features.ToArray(), labels.ToArray(), statuses.ToArray(), osmBackgroundColor);
    }

    private static BackgroundMapRaster? RenderNetworkRaster(string id, string source,
        NetworkTileProvider provider, Projection projectProjection, RasterMapViewExtent viewport,
        double opacity, bool recolor)
    {
        Extent requested = new(viewport.MinX, viewport.MaxX, viewport.MinY, viewport.MaxY);
        using ReprojectionTransformer transformer = new(WellKnownProjections.WebMercator, projectProjection);
        IMultiBandRaster<byte> raster = provider.AsRaster(fallbackToLowerResOnReadFailure: true);
        ImageRenderLayer layer = raster.WithTransformer(transformer).AsRenderLayer();
        if (recolor) layer.Recolorer = new ToBlackOpacityRecolorer();
        layer.Opacity = checked((byte)Math.Round(opacity * byte.MaxValue, MidpointRounding.AwayFromZero));
        if (!layer.ValidExtent.Overlaps(requested))
            return null;

        RasterDefinition definition = new(requested, viewport.PixelHeight, viewport.PixelWidth);
        if (!definition.IsValid() || definition.CellCount <= 0)
            throw new InvalidOperationException("The map view cannot be rendered at the requested raster dimensions.");
        using PlotRequest plot = new(definition);
        if (!layer.Render(plot))
            throw new IOException("The source network tile provider could not render this map extent.");
        ReadOnlySpan<int> argb = plot.ColorBufferARGB.Span;
        byte[] rgba = new byte[checked(argb.Length * 4)];
        for (int index = 0; index < argb.Length; index++)
        {
            uint pixel = unchecked((uint)argb[index]);
            int target = index * 4;
            rgba[target] = (byte)(pixel >> 16);
            rgba[target + 1] = (byte)(pixel >> 8);
            rgba[target + 2] = (byte)pixel;
            rgba[target + 3] = (byte)(pixel >> 24);
        }
        return new(id, source, definition.Cols, definition.Rows, rgba,
            definition.MinX, definition.MinY, definition.MaxX, definition.MaxY, 1.0);
    }

    private static async Task<OsmCpuStyleDocument> GetParsedStyleAsync(string styleID,
        CancellationToken cancellationToken)
    {
        string safeStyleID = string.IsNullOrWhiteSpace(styleID) ? "basic" : styleID.Trim();
        if (!IsSafeStyleID(safeStyleID))
            throw new ArgumentException("Choose a valid OpenStreetMap style before rendering.", nameof(styleID));
        if (ParsedStyles.TryGetValue(safeStyleID, out OsmCpuStyleDocument? cached))
            return cached;

        string styleURL = safeStyleID.Equals("darkmode", StringComparison.Ordinal)
            ? "https://basemaps.cartocdn.com/gl/voyager-gl-style/style.json"
            : OsmBaseUrl + "/styles/" + Uri.EscapeDataString(safeStyleID) + "/json";
        using HttpResponseMessage response = await Http.GetAsync(styleURL,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string styleJSON = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        OsmCpuStyleDocument parsed = OsmBackgroundCpuRenderer.ParseStyle(styleJSON);
        return ParsedStyles.GetOrAdd(safeStyleID, parsed);
    }

    private static async Task<OsmCpuTileFrame[]> LoadOsmFramesAsync(OsmCpuTileCoordinate[] tiles,
        OsmCpuStyleDocument style, Projection projectProjection, BackgroundMapSettings settings,
        CancellationToken cancellationToken)
    {
        using ReprojectionTransformer projector = new(WellKnownProjections.WebMercator, projectProjection);
        (double X, double Y) Transform(double x, double y)
        {
            Memory<Point> point = new Point[] { new(x, y) };
            projector.Transform(point, point);
            Point projected = point.Span[0];
            if (!double.IsFinite(projected.X) || !double.IsFinite(projected.Y))
                throw new InvalidOperationException("The current project CRS cannot transform the OSM tile coordinates.");
            return (projected.X, projected.Y);
        }

        Task<OsmCpuTileFrame>[] tasks = tiles.Select(async tile =>
        {
            byte[] bytes = await GetOsmTileAsync(tile, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return OsmBackgroundCpuRenderer.DecodeTile(bytes, style, tile,
                new OsmCpuRenderOptions { Density = settings.Density, ShowLabels = settings.ShowLabels }, Transform);
        }).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static OsmCpuTileCoordinate[] GetVisibleOsmTiles(Extent projectExtent,
        double worldUnitsPerPixel, Projection projectProjection, int density)
    {
        int zoom = ResolveOsmZoom(worldUnitsPerPixel, projectProjection, density);

        using ReprojectionTransformer transformer = new(WellKnownProjections.WebMercator, projectProjection);
        Extent mercatorExtent = Projector.GetBoundingBox(projectExtent, transformer,
            edgeDivisor: 6, ptsScratchSpace: new Point[24], isTransformerReversed: true);
        if (!mercatorExtent.IsFiniteAndValid())
            throw new InvalidOperationException("The map viewport could not be transformed into the OSM Web Mercator tile grid.");
        if (mercatorExtent.MaxX < -HalfWorldWidth || mercatorExtent.MinX > HalfWorldWidth ||
            mercatorExtent.MaxY < -HalfWorldWidth || mercatorExtent.MinY > HalfWorldWidth)
            return [];

        int count = 1 << zoom;
        int minimumColumn = TileIndex(mercatorExtent.MinX, count);
        int maximumColumn = TileIndex(mercatorExtent.MaxX, count);
        int minimumRow = TileIndex(mercatorExtent.MinY, count);
        int maximumRow = TileIndex(mercatorExtent.MaxY, count);
        if (maximumColumn < minimumColumn || maximumRow < minimumRow)
            return [];

        long tileCount = (long)(maximumColumn - minimumColumn + 1) * (maximumRow - minimumRow + 1);
        if (tileCount > MaximumOsmTilesPerView)
            throw new InvalidOperationException("The current map view covers more OSM tiles than the renderer allows at once.");
        List<OsmCpuTileCoordinate> tiles = new(checked((int)tileCount));
        for (int column = minimumColumn; column <= maximumColumn; column++)
        for (int row = minimumRow; row <= maximumRow; row++)
            tiles.Add(new OsmCpuTileCoordinate(zoom, column, row));
        return tiles.ToArray();
    }

    private static int TileIndex(double coordinate, int tileCount)
    {
        double raw = tileCount * ((coordinate + HalfWorldWidth) / (HalfWorldWidth * 2.0));
        if (!double.IsFinite(raw))
            throw new InvalidOperationException("The transformed viewport is outside the OSM tile coordinate range.");
        return Math.Clamp((int)Math.Floor(raw), 0, tileCount - 1);
    }

    private static async Task<byte[]> GetOsmTileAsync(OsmCpuTileCoordinate tile,
        CancellationToken cancellationToken)
    {
        (byte[]? cached, long capturedCacheGeneration) = await OsmTileCache.ReadAsync(tile, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null) return cached;

        await OsmRequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(
                $"{OsmBaseUrl}/{tile.Zoom}/{tile.Column}/{tile.TmsRow}?key={OsmTileKey}",
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length <= 2 || bytes[0] != 0x1F || bytes[1] != 0x8B)
                throw new InvalidDataException("The OSM tile service did not return a gzip-compressed vector tile.");

            await OsmTileCache.WriteIfCurrentAsync(tile, bytes, capturedCacheGeneration, cancellationToken)
                .ConfigureAwait(false);
            return bytes;
        }
        finally
        {
            OsmRequestGate.Release();
        }
    }

    private static Projection ResolveProjectedCRS(string projectionType, string projectionValue)
    {
        ProjectionType type = ProjectionDefinition.TryParseProjectionType(projectionType);
        if (type == ProjectionType.Unknown || type == ProjectionType.Local)
            throw new NotSupportedException("Background map tiles require a projected project CRS.");
        ProjectionDefinition definition = new(type, projectionValue);
        if (!definition.Evaluate(out Projection projection, out _).Result || projection is null ||
            !projection.IsProjected() || projection.IsValid() != SRSValidation.Valid)
            throw new ArgumentException("The saved project CRS is not a valid projected coordinate system.");
        return projection;
    }

    private static void ValidateRenderRequest(BackgroundMapRenderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestID) || string.IsNullOrWhiteSpace(request.MapID) ||
            string.IsNullOrWhiteSpace(request.ProjectSessionID))
            throw new ArgumentException("Background map rendering requires map, project-session and request identities.");
        if (request.Viewport is null || request.Settings is null)
            throw new ArgumentException("Background map rendering requires a viewport and source settings.");
        RasterMapViewExtent viewport = request.Viewport;
        if (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY) ||
            !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY) ||
            viewport.MaxX <= viewport.MinX || viewport.MaxY <= viewport.MinY ||
            viewport.PixelWidth is < 1 or > MaximumRasterSide || viewport.PixelHeight is < 1 or > MaximumRasterSide ||
            (long)viewport.PixelWidth * viewport.PixelHeight > MaximumRasterPixels)
            throw new ArgumentOutOfRangeException(nameof(request), "The background map viewport dimensions or extent are invalid.");
        if (!double.IsFinite(request.WorldUnitsPerPixel) || request.WorldUnitsPerPixel <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "The current map scale must be a finite positive value.");
        if (request.Settings.Density is < -1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(request), "OSM information density must be between -1 and 5.");
        if (!double.IsFinite(request.Settings.SatelliteOpacity) || request.Settings.SatelliteOpacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(request), "Satellite opacity must be between 0 and 1.");
    }

    private static bool IsSafeStyleID(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string HumanizeStyleID(string value) =>
        string.Join(' ', value.Replace('-', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string EncodeColor(OsmCpuColor color) =>
        $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private static bool IsRenderFailure(Exception exception) =>
        exception is HttpRequestException or IOException or InvalidOperationException or
            ArgumentException or NotSupportedException or JsonException or OverflowException or OperationCanceledException;

    private static string DescribeHttpFailure(Exception exception) => exception switch
    {
        HttpRequestException => "The OSM service returned an HTTP error.",
        OperationCanceledException => "The OSM request timed out.",
        IOException io when io.Message.Contains("exceeds the size limit", StringComparison.Ordinal) =>
            "The OSM response exceeds the size limit.",
        _ => "The OSM service could not be reached or returned invalid data."
    };

    private static string DescribeSourceFailure(Exception exception) => exception is HttpRequestException
        ? "The background imagery service did not return usable map tiles. Check the network connection and try again."
        : exception is NotSupportedException
            ? exception.Message
        : exception is OperationCanceledException
            ? "Background imagery timed out while loading the current view."
            : "Background imagery could not render this view. Check the network connection and project CRS.";
}
