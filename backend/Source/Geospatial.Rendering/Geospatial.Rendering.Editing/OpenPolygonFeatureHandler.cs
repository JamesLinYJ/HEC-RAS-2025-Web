using System;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class OpenPolygonFeatureHandler : OpenFeatureHandler
{
	private readonly Func<int, Polygon> _getPolygon;

	public OpenPolygonFeatureHandler(Func<HitTestArgs, int> hitClosest, Func<int, EditContext, IEditHandler> buildEditHandler, Func<int, Polygon> getPolygon)
		: base(hitClosest, buildEditHandler)
	{
		_getPolygon = getPolygon;
	}

	public override bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		int hitClosest = _hitClosest(hit);
		if (hitClosest < 0)
		{
			return false;
		}
		string channel = "openpolygon_highlight";
		ctx.Preview.TryGetVector(channel, out var id);
		ctx.VectorManager.AddOrUpdateVector(ref id, _getPolygon(hitClosest), DefaultEditSymbology.SelectedPolygonSymbology);
		ctx.Preview.RegisterVector(channel, id);
		return true;
	}
}
