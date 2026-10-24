using Geospatial.Rasters;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers;

public sealed class SmoothPolylineHandler : PolylineTransformHandler
{
	public static class Options
	{
		public static readonly OptionKey<int> WindowSize = OptionKey<int>.Define("LineFeature.Smooth.WindowSize", "Window Size", "Number of neighboring points averaged together. Larger values produce smoother results.", 3);

		public static readonly OptionKey<int> Iterations = OptionKey<int>.Define("LineFeature.Smooth.Iterations", "Passes", "How many times to repeat the moving average.", 2);
	}

	public SmoothPolylineHandler(IPolylineCollection polylines)
		: base(polylines, "polyline_smooth_preview", "Smooth polyline")
	{
	}

	protected override Polyline TransformPolyline(Polyline src, EditContext ctx, RasterDefinition window)
	{
		return src.CloneWithMovingAverageSmoothing(ctx.Options.Get(Options.WindowSize), ctx.Options.Get(Options.Iterations));
	}
}
