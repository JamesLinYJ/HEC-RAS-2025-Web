using System;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class SnapArcByClickHandler : ArcPolylineTransformHandler
{
	private readonly Func<IResample<float>> _getRaster;

	public SnapArcByClickHandler(TopologyEditContext context, Func<IResample<float>> getRaster)
		: base(context, "arc_snap_preview", "Snap arc")
	{
		_getRaster = getRaster;
	}

	protected override Polyline TransformArcPolyline(Polyline arcLine, EditContext ctx, RasterDefinition window)
	{
		IResample<float> resample = _getRaster?.Invoke();
		if (resample == null)
		{
			return null;
		}
		PolylineSnapOptions options = SnapPolylineByClickHandler.ReadOptions(ctx, window.CellSize);
		return PolylineSnapHelpers.SnapPolyline(arcLine, resample, options);
	}

	public override bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		return false;
	}
}
