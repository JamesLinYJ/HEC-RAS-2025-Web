using System;

namespace Geospatial.Rendering.Editing.Options;

public static class SnapTolerance
{
	public static readonly OptionKey<double> SearchRadiusPixels = OptionKey<double>.Define("Experimental.Snap.SearchRadiusPixels", "Search Radius (px)", "Pixel snap radius. Combined with world-units radius via Max().", 8.0);

	public static readonly OptionKey<double> SearchRadiusWorld = OptionKey<double>.Define("Experimental.Snap.SearchRadiusWorld", "Search Radius (world units)", "World-space snap radius (map units). Combined with pixel radius via Max(). 0 = pixel-only.", 50.0);

	public static double EffectiveRadius(EditContext ctx, double cellSize)
	{
		return Math.Max(ctx.Options.Get(SearchRadiusPixels) * cellSize, ctx.Options.Get(SearchRadiusWorld));
	}
}
