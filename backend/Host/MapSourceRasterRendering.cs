using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Transformers;
using Geospatial.Vectors;

namespace Ras.Backend;

/// <summary>
/// Pixels and labels produced by a retained source RenderLayer for one exact
/// project-coordinate viewport. RGBA bytes are premultiplied and row-major.
/// </summary>
public sealed record MapSourceRasterLabel(string Text, double X, double Y);

public sealed record MapSourceRasterFrame(
    RasterMapViewExtent Viewport,
    int Width,
    int Height,
    byte[] Rgba,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    MapSourceRasterLabel[] Labels);

/// <summary>
/// Adapts the retained source renderers to the native map's exact viewport.
/// The renderer still sees coordinates in its source CRS; output samples and
/// labels are warped back to the requested project CRS through the supplied
/// project-to-source transform.
/// </summary>
internal static class MapSourceRasterRendering
{
    private const long MaximumPixelCount = 2_097_152;

    private sealed record RenderedSource(RasterDefinition MapRaster, int[] Pixels,
        MapSourceRasterLabel[] Labels, RasterMapViewExtent Viewport);

    public static MapSourceRasterFrame Render(IRenderLayer renderer, RasterMapViewExtent viewport,
        ISpatialTransformer? projectToSource = null, double renderScale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        RenderedSource rendered = RenderSource(renderer, viewport, projectToSource, renderScale);
        return Frame(rendered, rendered.Pixels, rendered.Labels);
    }

    public static MapSourceRasterFrame Difference(IRenderLayer baseline, IRenderLayer enabled,
        RasterMapViewExtent viewport, ISpatialTransformer? projectToSource = null,
        double renderScale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(enabled);
        RenderedSource before = RenderSource(baseline, viewport, projectToSource, renderScale);
        RenderedSource after = RenderSource(enabled, viewport, projectToSource, renderScale);
        if (!before.MapRaster.Equals(after.MapRaster) || before.Pixels.Length != after.Pixels.Length)
            throw new InvalidDataException("The source renderer did not return a stable viewport.");

        int[] overlay = new int[after.Pixels.Length];
        for (int index = 0; index < overlay.Length; index++)
            if (before.Pixels[index] != after.Pixels[index]) overlay[index] = after.Pixels[index];

        var originalLabels = new HashSet<MapSourceRasterLabel>(before.Labels);
        MapSourceRasterLabel[] labels = after.Labels.Where(label => !originalLabels.Contains(label)).ToArray();
        return Frame(after, overlay, labels);
    }

    private static RenderedSource RenderSource(IRenderLayer renderer, RasterMapViewExtent viewport,
        ISpatialTransformer? projectToSource, double renderScale)
    {
        ValidateViewport(viewport);
        if (!double.IsFinite(renderScale) || renderScale <= 0 || renderScale > 64)
            throw new ArgumentOutOfRangeException(nameof(renderScale));
        if ((long)viewport.PixelWidth * viewport.PixelHeight > MaximumPixelCount)
            throw new ArgumentException("The source map render viewport exceeds the supported pixel count.");

        var requestedExtent = new Extent(viewport.MinX, viewport.MaxX, viewport.MinY, viewport.MaxY);
        var mapRaster = new RasterDefinition(requestedExtent, viewport.PixelHeight, viewport.PixelWidth);
        if (!mapRaster.IsValid() || mapRaster.CellCount <= 0 || mapRaster.CellCount > MaximumPixelCount)
            throw new ArgumentException("The source map render viewport could not be represented as a raster.");

        Extent sourceExtent = projectToSource is null ? mapRaster.Extent : mapRaster.Extent.Transform(projectToSource);
        if (!sourceExtent.IsFiniteAndValid())
            throw new InvalidDataException("The source renderer viewport could not be transformed into its native projection.");
        var sourceRaster = new RasterDefinition(sourceExtent, viewport.PixelHeight, viewport.PixelWidth);
        if (!sourceRaster.IsValid() || sourceRaster.CellCount <= 0 || sourceRaster.CellCount > MaximumPixelCount)
            throw new InvalidDataException("The source renderer viewport could not be represented in its native projection.");

        PlotRequest plot;
        double previousRenderScale = VectorManagerOptions.RenderScaling;
        try
        {
            // Raster dimensions determine sampling density. RenderScaling
            // preserves the source pen/marker size when a high-DPI canvas is
            // downsampled to the bounded response dimensions.
            VectorManagerOptions.RenderScaling = renderScale;
            plot = renderer.Render(sourceRaster);
        }
        finally
        {
            VectorManagerOptions.RenderScaling = previousRenderScale;
        }
        using (plot)
        {
        if (plot.ColorBufferARGB.Length != sourceRaster.CellCount)
            throw new InvalidDataException("The source map renderer returned an incomplete pixel buffer.");
        int[] sourcePixels = plot.ColorBufferARGB.ToArray();
        int[] mapPixels = new int[mapRaster.CellCount];
        Point[] mapRow = new Point[mapRaster.Cols];
        Point[] sourceRow = new Point[mapRaster.Cols];
        for (int row = 0; row < mapRaster.Rows; row++)
        {
            for (int col = 0; col < mapRaster.Cols; col++)
                mapRow[col] = mapRaster.CellCenter(row, col);
            if (projectToSource is null)
                Array.Copy(mapRow, sourceRow, mapRow.Length);
            else
                projectToSource.Transform(mapRow, sourceRow);

            int mapOffset = row * mapRaster.Cols;
            for (int col = 0; col < mapRaster.Cols; col++)
            {
                Point point = sourceRow[col];
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) continue;
                int sourceRowIndex = sourceRaster.Row(point.Y);
                int sourceColumnIndex = sourceRaster.Col(point.X);
                if ((uint)sourceRowIndex >= (uint)sourceRaster.Rows ||
                    (uint)sourceColumnIndex >= (uint)sourceRaster.Cols) continue;
                mapPixels[mapOffset + col] = sourcePixels[sourceRowIndex * sourceRaster.Cols + sourceColumnIndex];
            }
        }

        MapSourceRasterLabel[] labels = (plot.Labels ?? []).Select(label =>
        {
            Point point = label.PointLocation;
            if (projectToSource is not null)
            {
                Point[] sourcePoint = [point];
                Point[] mapPoint = new Point[1];
                projectToSource.ReverseTransform(sourcePoint, mapPoint);
                point = mapPoint[0];
            }
            return new MapSourceRasterLabel(label.Text ?? string.Empty, point.X, point.Y);
        }).Where(label => !string.IsNullOrEmpty(label.Text) && double.IsFinite(label.X) && double.IsFinite(label.Y))
          .ToArray();

        return new RenderedSource(mapRaster, mapPixels, labels, viewport);
        }
    }

    private static MapSourceRasterFrame Frame(RenderedSource rendered, int[] pixels,
        MapSourceRasterLabel[] labels)
    {
        byte[] rgba = new byte[checked(pixels.Length * 4)];
        for (int index = 0; index < pixels.Length; index++)
        {
            uint argb = unchecked((uint)pixels[index]);
            byte alpha = (byte)(argb >> 24);
            rgba[index * 4] = (byte)((((argb >> 16) & 0xff) * alpha + 127) / 255);
            rgba[index * 4 + 1] = (byte)((((argb >> 8) & 0xff) * alpha + 127) / 255);
            rgba[index * 4 + 2] = (byte)(((argb & 0xff) * alpha + 127) / 255);
            rgba[index * 4 + 3] = alpha;
        }
        RasterDefinition raster = rendered.MapRaster;
        return new MapSourceRasterFrame(rendered.Viewport, raster.Cols, raster.Rows, rgba,
            raster.MinX, raster.MinY, raster.MaxX, raster.MaxY, labels);
    }

    private static void ValidateViewport(RasterMapViewExtent viewport)
    {
        if (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY) ||
            !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY) ||
            viewport.MinX >= viewport.MaxX || viewport.MinY >= viewport.MaxY ||
            viewport.PixelWidth is < 1 or > 2048 || viewport.PixelHeight is < 1 or > 2048)
            throw new ArgumentException("The source map render viewport is invalid.");
    }
}
