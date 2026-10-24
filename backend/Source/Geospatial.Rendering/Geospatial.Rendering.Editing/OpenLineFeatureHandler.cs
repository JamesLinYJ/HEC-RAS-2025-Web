using System;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class OpenLineFeatureHandler : OpenFeatureHandler
{
	private Func<int, Polyline> _getLineForFid;

	public OpenLineFeatureHandler(Func<HitTestArgs, int> hitClosest, Func<int, EditContext, IEditHandler> buildEditHandler, Func<int, Polyline> getLineForFid)
		: base(hitClosest, buildEditHandler)
	{
		_getLineForFid = getLineForFid;
	}

	public override bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		int hitClosest = _hitClosest(hit);
		if (hitClosest >= 0)
		{
			string channel = "selectline_highlight";
			ctx.Preview.TryGetVector(channel, out var id);
			ctx.VectorManager.AddOrUpdateVector(ref id, _getLineForFid(hitClosest), DefaultEditSymbology.SelectLineSymbology);
			ctx.Preview.RegisterVector(channel, id);
			return true;
		}
		return false;
	}
}
