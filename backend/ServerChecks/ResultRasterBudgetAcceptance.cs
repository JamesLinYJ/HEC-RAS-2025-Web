namespace Ras.Backend.ServerChecks;

internal sealed record ResultRasterBudgetReport(string State, int ViewportChecks,
    int InvalidInputsRejected, long MaximumAggregatePixels);

internal static class ResultRasterBudgetAcceptance
{
    internal static ResultRasterBudgetReport Run()
    {
        int checks = 0;
        foreach ((int width, int height) in new[] { (1536, 1536), (1536, 1), (1, 1536), (1024, 1024), (929, 454) })
        {
            for (int layers = 1; layers <= 32; layers++)
            {
                var source = new RasterMapViewExtent(-37.5, 11, 104.25, 96, width, height);
                RasterMapViewExtent result = MapResultRenderService.ConstrainRasterViewport(source, layers);
                if (result.MinX != source.MinX || result.MinY != source.MinY ||
                    result.MaxX != source.MaxX || result.MaxY != source.MaxY ||
                    result.PixelWidth < 1 || result.PixelHeight < 1 ||
                    result.PixelWidth > width || result.PixelHeight > height ||
                    (long)result.PixelWidth * result.PixelHeight * layers > MapResultRenderService.MaximumAggregateRasterPixels)
                    throw new InvalidDataException("Result preview dimensions exceeded their aggregate budget or changed world bounds.");
                if ((long)width * height * layers <= MapResultRenderService.MaximumAggregateRasterPixels && result != source)
                    throw new InvalidDataException("A Result preview was downsampled despite fitting the full-resolution budget.");
                if (Math.Abs((double)result.PixelWidth / width - (double)result.PixelHeight / height) > Math.Max(1d / width, 1d / height))
                    throw new InvalidDataException("Result preview downsampling distorted its aspect ratio beyond pixel rounding.");
                checks++;
            }
        }
        int rejected = 0;
        foreach ((int width, int height, int layers) in new[] { (1024, 1024, 0), (1024, 1024, 33), (0, 1024, 2), (1537, 1024, 2) })
        {
            try { _ = MapResultRenderService.ConstrainRasterViewport(new(0, 0, 4, 4, width, height), layers); }
            catch (ArgumentException) { rejected++; continue; }
            throw new InvalidDataException("An invalid Result preview request escaped its bounds.");
        }
        return new("passed", checks, rejected, MapResultRenderService.MaximumAggregateRasterPixels);
    }
}
