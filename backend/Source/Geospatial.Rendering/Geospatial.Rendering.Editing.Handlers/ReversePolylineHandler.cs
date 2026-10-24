using Geospatial.Helpers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers;

public sealed class ReversePolylineHandler : IEditHandler
{
	private const string PreviewChannel = "polyline_reverse_preview";

	private const string ArrowsChannel = "polyline_reverse_preview_arrows";

	private readonly IPolylineCollection _polylines;

	private readonly ReverseDirectionPreviewCache _previewCache = new ReverseDirectionPreviewCache();

	private VectorID _previewId;

	private VectorID _arrowsId;

	public ReversePolylineHandler(IPolylineCollection polylines)
	{
		_polylines = polylines;
	}

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
		Polyline polyline = _polylines[fid].Clone();
		polyline.Reverse();
		ReplacePolylinesOperation op = new ReplacePolylinesOperation(_polylines, new _003C_003Ez__ReadOnlySingleElementList<int>(fid), new _003C_003Ez__ReadOnlySingleElementList<Polyline>(polyline), "Reverse polyline");
		ctx.ExecuteEditOperation(op);
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		if (!TryHit(hit, out var fid))
		{
			return false;
		}
		_previewCache.GetOrCompute(_polylines[fid], hit.Window, out var reversed, out var chevrons);
		ctx.Preview.TryGetVector("polyline_reverse_preview", out _previewId);
		ctx.VectorManager.AddOrUpdateVector(ref _previewId, reversed, DefaultEditSymbology.OpenSymbology);
		ctx.Preview.RegisterVector("polyline_reverse_preview", _previewId);
		ctx.Preview.TryGetVector("polyline_reverse_preview_arrows", out _arrowsId);
		ctx.VectorManager.AddOrUpdateVector(ref _arrowsId, chevrons, DefaultEditSymbology.SelectLineSymbology);
		ctx.Preview.RegisterVector("polyline_reverse_preview_arrows", _arrowsId);
		return true;
	}

	private bool TryHit(HitTestArgs hit, out int fid)
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
