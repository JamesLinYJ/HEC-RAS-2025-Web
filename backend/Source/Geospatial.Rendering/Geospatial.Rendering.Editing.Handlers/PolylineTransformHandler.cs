using Geospatial.Helpers;
using Geospatial.Rasters;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers;

public abstract class PolylineTransformHandler : IEditHandler
{
	private readonly IPolylineCollection _polylines;

	private readonly string _previewChannel;

	private readonly string _operationDescription;

	private VectorID _previewId;

	protected IPolylineCollection Polylines => _polylines;

	protected string PreviewChannel => _previewChannel;

	protected PolylineTransformHandler(IPolylineCollection polylines, string previewChannel, string operationDescription)
	{
		_polylines = polylines;
		_previewChannel = previewChannel;
		_operationDescription = operationDescription;
	}

	protected abstract Polyline TransformPolyline(Polyline src, EditContext ctx, RasterDefinition window);

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		IEditModifiers modifiers = cmd.Modifiers;
		if (modifiers != null && modifiers.IsRightClick())
		{
			return HandlerResult.NotHandled;
		}
		if (!TryHit(cmd.Location, out var fid))
		{
			return HandlerResult.NotHandled;
		}
		Polyline polyline = _polylines[fid];
		if (polyline == null)
		{
			return HandlerResult.NotHandled;
		}
		Polyline polyline2 = TransformPolyline(polyline, ctx, cmd.Location.Window);
		if (polyline2 == null)
		{
			return HandlerResult.NotHandled;
		}
		ReplacePolylinesOperation op = new ReplacePolylinesOperation(_polylines, new _003C_003Ez__ReadOnlySingleElementList<int>(fid), new _003C_003Ez__ReadOnlySingleElementList<Polyline>(polyline2), _operationDescription);
		ctx.ExecuteEditOperation(op);
		return HandlerResult.WasHandled;
	}

	public virtual bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		if (!TryHit(hit, out var fid))
		{
			return false;
		}
		Polyline polyline = _polylines[fid];
		if (polyline == null)
		{
			return false;
		}
		Polyline polyline2 = TransformPolyline(polyline, ctx, hit.Window);
		if (polyline2 == null)
		{
			return false;
		}
		ctx.Preview.TryGetVector(_previewChannel, out _previewId);
		ctx.VectorManager.AddOrUpdateVector(ref _previewId, polyline2, DefaultEditSymbology.AppendSegmentSymbology);
		ctx.Preview.RegisterVector(_previewChannel, _previewId);
		return true;
	}

	protected bool TryHit(HitTestArgs hit, out int fid)
	{
		if (hit == null || _polylines == null)
		{
			fid = -1;
			return false;
		}
		double hitDistance = 8.0 * hit.Window.CellSize;
		if (_polylines.TryHitTestClosestFeature(hit.QueryPoint, hitDistance, out fid))
		{
			return true;
		}
		fid = -1;
		return false;
	}
}
