using System.Collections.Generic;
using Geospatial.Helpers;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers;

public sealed class MergePolylineByClickHandler : IEditHandler
{
	private const string PickedChannel = "polyline_merge_click_picked";

	private const string HoverChannel = "polyline_merge_click_hover";

	private readonly IPolylineCollection _polylines;

	private readonly SelectedSet _selectedSet;

	private int _pickedFid = -1;

	private VectorID _pickedPreviewId;

	private VectorID _hoverPreviewId;

	public MergePolylineByClickHandler(IPolylineCollection polylines, SelectedSet selectedSet)
	{
		_polylines = polylines;
		_selectedSet = selectedSet;
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
			ResetPick();
			return HandlerResult.WasHandled;
		}
		if (!TryHit(cmd.Location, out var fid))
		{
			ResetPick();
			return HandlerResult.NotHandled;
		}
		if (_pickedFid < 0)
		{
			_pickedFid = fid;
			return HandlerResult.WasHandled;
		}
		if (fid == _pickedFid)
		{
			return HandlerResult.WasHandled;
		}
		double tolerance = ctx.Options.Get(MergePolylineByLassoHandler.Options.ToleranceWorld);
		List<int> fids = new List<int>(2) { _pickedFid, fid };
		MergePolylineOperation op = new MergePolylineOperation(_polylines, fids, tolerance);
		ctx.ExecuteEditOperation(op);
		_selectedSet?.Clear();
		ResetPick();
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		bool result = false;
		if (_pickedFid >= 0 && _pickedFid < _polylines.Count)
		{
			ctx.Preview.TryGetVector("polyline_merge_click_picked", out _pickedPreviewId);
			ctx.VectorManager.AddOrUpdateVector(ref _pickedPreviewId, _polylines[_pickedFid], DefaultEditSymbology.OpenSymbology);
			ctx.Preview.RegisterVector("polyline_merge_click_picked", _pickedPreviewId);
			result = true;
		}
		if (hit != null && TryHit(hit, out var fid) && fid != _pickedFid)
		{
			ctx.Preview.TryGetVector("polyline_merge_click_hover", out _hoverPreviewId);
			ctx.VectorManager.AddOrUpdateVector(ref _hoverPreviewId, _polylines[fid], DefaultEditSymbology.SelectLineSymbology);
			ctx.Preview.RegisterVector("polyline_merge_click_hover", _hoverPreviewId);
			result = true;
		}
		return result;
	}

	private void ResetPick()
	{
		_pickedFid = -1;
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
