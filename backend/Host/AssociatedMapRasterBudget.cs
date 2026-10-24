namespace Ras.Backend;

/// <summary>
/// Constrains one shared associated-layer viewport so the RGBA snapshots for
/// all resolved rows remain within the same aggregate pixel budget used by
/// Result rendering.
/// </summary>
internal static class AssociatedMapRasterBudget
{
    private const int MaximumResolvedLayers = 256;

    /// <summary>
    /// Returns a render size shared by every resolved association row. Hidden,
    /// unavailable and ultimately empty rows are included conservatively,
    /// because empty output is only known after its source has been sampled.
    /// A zero-layer request keeps its validated size unchanged.
    /// </summary>
    internal static (int Width, int Height) Constrain(int width, int height,
        int resolvedLayerCount)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
        if (resolvedLayerCount is < 0 or > MaximumResolvedLayers)
            throw new ArgumentOutOfRangeException(nameof(resolvedLayerCount));
        if (resolvedLayerCount == 0) return (width, height);

        long requestedPixels = (long)width * height;
        long perLayerBudget = MapResultRenderService.MaximumAggregateRasterPixels / resolvedLayerCount;
        if (requestedPixels <= perLayerBudget) return (width, height);

        double scale = Math.Sqrt((double)perLayerBudget / requestedPixels);
        int renderWidth = Math.Max(1, (int)Math.Floor(width * scale));
        int renderHeight = Math.Max(1, (int)Math.Floor(height * scale));

        // Flooring both scaled dimensions is sufficient mathematically. Keep
        // the hard aggregate bound explicit against floating point rounding.
        while ((long)renderWidth * renderHeight > perLayerBudget)
        {
            double widthScale = (double)renderWidth / width;
            double heightScale = (double)renderHeight / height;
            if (widthScale >= heightScale && renderWidth > 1)
                renderWidth--;
            else if (renderHeight > 1)
                renderHeight--;
            else if (renderWidth > 1)
                renderWidth--;
            else
                throw new InvalidOperationException("The associated-layer raster budget cannot fit one pixel.");
        }

        return (renderWidth, renderHeight);
    }
}
