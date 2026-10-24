using Geospatial.Rasters;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers;

public sealed class FilterPolylineHandler : PolylineTransformHandler
{
	public static class Options
	{
		public static readonly OptionKey<double> Epsilon = OptionKey<double>.Define("LineFeature.Filter.Epsilon", "Tolerance (map units)", "Douglas-Peucker tolerance; larger = more aggressive simplification.", 1.0);
	}

	public FilterPolylineHandler(IPolylineCollection polylines)
		: base(polylines, "polyline_filter_preview", "Filter polyline")
	{
	}

	protected override Polyline TransformPolyline(Polyline src, EditContext ctx, RasterDefinition window)
	{
		return new Polyline(src.DouglasPeuckerReduction(ctx.Options.Get(Options.Epsilon)));
	}
}
