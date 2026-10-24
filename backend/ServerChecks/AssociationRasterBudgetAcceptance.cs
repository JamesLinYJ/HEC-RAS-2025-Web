namespace Ras.Backend.ServerChecks;

public sealed record AssociationRasterBudgetReport(string State, int ViewportChecks,
    int LayerCountsCovered, int InvalidInputsRejected, long MaximumAggregatePixels);

public static class AssociationRasterBudgetAcceptance
{
    private const long MaximumRequestPixels = 2_097_152;
    private const int MaximumRequestSide = 2_048;

    public static AssociationRasterBudgetReport Run()
    {
        (int Width, int Height)[] viewports =
        [
            (2048, 1024), (1024, 2048), (1448, 1448), (2048, 1023),
            (1536, 1365), (1920, 1080), (1600, 900), (1280, 720),
            (1024, 1024), (929, 454), (512, 512),
            (1, 2048), (2048, 1), (3, 2048), (2048, 3),
            (7, 2048), (2048, 7), (17, 2048), (2048, 17)
        ];

        int checks = 0;
        long largestAggregate = 0;
        long budget = MapResultRenderService.MaximumAggregateRasterPixels;
        foreach ((int width, int height) in viewports)
        {
            if (width is < 1 or > MaximumRequestSide || height is < 1 or > MaximumRequestSide ||
                (long)width * height > MaximumRequestPixels)
                throw new InvalidDataException("The associated-layer budget fixture contains an invalid request viewport.");

            for (int layers = 0; layers <= 256; layers++)
            {
                (int renderWidth, int renderHeight) = AssociatedMapRasterBudget.Constrain(width, height, layers);
                long aggregatePixels = (long)renderWidth * renderHeight * layers;
                largestAggregate = Math.Max(largestAggregate, aggregatePixels);

                if (renderWidth is < 1 || renderHeight is < 1 ||
                    renderWidth > width || renderHeight > height || aggregatePixels > budget)
                    throw new InvalidDataException("Associated-layer render dimensions exceeded the aggregate pixel budget.");

                long requestedAggregate = (long)width * height * layers;
                if (requestedAggregate <= budget)
                {
                    if (renderWidth != width || renderHeight != height)
                        throw new InvalidDataException("An associated-layer viewport was downsampled despite fitting the aggregate budget.");
                }
                else
                {
                    if (renderWidth == width && renderHeight == height)
                        throw new InvalidDataException("An over-budget associated-layer viewport was not downsampled.");

                    // Uniform scale with integer dimensions may differ by at
                    // most one source pixel in either axis.
                    double widthScale = (double)renderWidth / width;
                    double heightScale = (double)renderHeight / height;
                    double roundingTolerance = Math.Max(1d / width, 1d / height) + 1e-12;
                    if (Math.Abs(widthScale - heightScale) > roundingTolerance)
                        throw new InvalidDataException("Associated-layer budget reduction changed the viewport aspect ratio.");
                }
                checks++;
            }
        }

        int rejected = 0;
        foreach ((int width, int height, int layers) in new[]
                 {
                     (0, 1024, 1), (-1, 8, 1), (1024, 0, 1), (8, -1, 1),
                     (1, 1, -1), (1, 1, 257)
                 })
        {
            try
            {
                _ = AssociatedMapRasterBudget.Constrain(width, height, layers);
            }
            catch (ArgumentException)
            {
                rejected++;
                continue;
            }
            throw new InvalidDataException("An invalid associated-layer budget input escaped its bounds.");
        }

        return new("passed", checks, 257, rejected, largestAggregate);
    }
}
