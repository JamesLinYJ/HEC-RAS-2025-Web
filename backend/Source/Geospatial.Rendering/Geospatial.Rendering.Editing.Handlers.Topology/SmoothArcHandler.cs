using Geospatial.Rasters;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class SmoothArcHandler : ArcPolylineTransformHandler
{
	public static class Options
	{
		public static readonly OptionKey<int> WindowSize = OptionKey<int>.Define("Arc.Smooth.WindowSize", "Window Size", "Number of neighboring points averaged together. Larger values produce smoother results.", 3);

		public static readonly OptionKey<int> Iterations = OptionKey<int>.Define("Arc.Smooth.Iterations", "Passes", "How many times to repeat the moving average over the arc's internal points.", 2);
	}

	public SmoothArcHandler(TopologyEditContext context)
		: base(context, "arc_smooth_preview", "Smooth arc")
	{
	}

	protected override Polyline TransformArcPolyline(Polyline arcLine, EditContext ctx, RasterDefinition window)
	{
		return arcLine.CloneWithMovingAverageSmoothing(ctx.Options.Get(Options.WindowSize), ctx.Options.Get(Options.Iterations));
	}
}
