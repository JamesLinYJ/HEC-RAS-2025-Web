using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class StampPolygonPlacementHandler : IEditHandler
{
	private readonly IPolygonCollection _polygons;

	private readonly SelectedSet _selected;

	private readonly Func<Polygon> _templateFactory;

	private Func<HitTestArgs, int> _hitClosest;

	private readonly string _previewChannel = "stamp_polygon_preview";

	private readonly string _previewPointChannel = "stamp_polygon_point";

	public StampPolygonPlacementHandler(IPolygonCollection polygons, SelectedSet selected, Func<Polygon> templateFactory, Func<HitTestArgs, int> hitClosest)
	{
		_polygons = polygons;
		_selected = selected;
		_templateFactory = templateFactory ?? throw new ArgumentNullException("templateFactory");
		_hitClosest = hitClosest;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		int hitClosest = _hitClosest(cmd.Location);
		if (hitClosest >= 0 && !_polygons[hitClosest].Contains(cmd.Location.QueryPoint))
		{
			return HandlerResult.NotHandled;
		}
		Polygon polygon = CreateStamp(cmd.Location.QueryPoint);
		if (polygon == null || polygon.Count == 0)
		{
			return HandlerResult.NotHandled;
		}
		Polygon stampClone = new Polygon(polygon);
		NewFeatureOperation<Polygon> op = new NewFeatureOperation<Polygon>(_polygons, _selected, () => new Polygon(stampClone));
		if (!ctx.ExecuteEditOperation(op))
		{
			return HandlerResult.NotHandled;
		}
		_selected.Clear();
		_selected.Add(_polygons.Count - 1);
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		Polygon polygon = CreateStamp(hit.QueryPoint);
		if (polygon == null || polygon.Count == 0)
		{
			return false;
		}
		ctx.Preview.TryGetVector(_previewChannel, out var id);
		ctx.VectorManager.AddOrUpdateVector(ref id, polygon, DefaultEditSymbology.SelectedPolygonSymbology);
		ctx.Preview.RegisterVector(_previewChannel, id);
		ctx.Preview.TryGetVector(_previewPointChannel, out var id2);
		ctx.VectorManager.AddOrUpdateVector(ref id2, hit.QueryPoint, DefaultEditSymbology.AppendPointSymbology);
		ctx.Preview.RegisterVector(_previewPointChannel, id2);
		return true;
	}

	private Polygon CreateStamp(Point anchor)
	{
		Polygon polygon = _templateFactory?.Invoke();
		if (polygon == null)
		{
			return null;
		}
		Polygon polygon2 = new Polygon(polygon);
		polygon2.Translate(anchor.X, anchor.Y);
		return polygon2;
	}
}
