using Geospatial.Rasters;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class FilterArcHandler : ArcPolylineTransformHandler
{
	public static class Options
	{
		public static readonly OptionKey<double> Epsilon = OptionKey<double>.Define("Arc.Filter.Epsilon", "Tolerance (map units)", "Douglas-Peucker tolerance applied to arc internal points; larger = more aggressive simplification.", 1.0);
	}

	public FilterArcHandler(TopologyEditContext context)
		: base(context, "arc_filter_preview", "Filter arc")
	{
	}

	protected override Polyline TransformArcPolyline(Polyline arcLine, EditContext ctx, RasterDefinition window)
	{
		return new Polyline(arcLine.DouglasPeuckerReduction(ctx.Options.Get(Options.Epsilon)));
	}
}
