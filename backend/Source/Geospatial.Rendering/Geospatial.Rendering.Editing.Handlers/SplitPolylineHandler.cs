using System.Collections.Generic;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Vectors;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing.Handlers;

public sealed class SplitPolylineHandler : IEditHandler
{
	public static class Options
	{
		public static readonly OptionKey<SplitMode> Mode = OptionKey<SplitMode>.Define("Polyline.Split.Mode", "Split Mode", "Where the split occurs on a polyline. Alt swaps to the non-default mode.", SplitMode.Anywhere);
	}

	private readonly IPolylineCollection _polylines;

	private readonly bool _requireSKey;

	public SplitPolylineHandler(IPolylineCollection polylines, bool requireSKey = true)
	{
		_polylines = polylines;
		_requireSKey = requireSKey;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (_requireSKey && !cmd.Modifiers.IsAdditionalKeyDown(PolylineSplitHelpers.SplitKey))
		{
			return HandlerResult.NotHandled;
		}
		CompactList<SplitLineResult> splits;
		if (ResolveMode(cmd.Modifiers, ctx) == SplitMode.AtVertex)
		{
			if (!TryGetVertexSplit(cmd.Location, out var splitRes))
			{
				return HandlerResult.NotHandled;
			}
			splits = new CompactList<SplitLineResult>(splitRes);
		}
		else
		{
			SnapResult snapResult = TryGetSplitResult(cmd.Location);
			if (!snapResult.Success)
			{
				return HandlerResult.NotHandled;
			}
			splits = snapResult.SplitLines;
		}
		SplitPolylineOperation op = new SplitPolylineOperation(_polylines, splits);
		ctx.ExecuteEditOperation(op);
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		if (_requireSKey && (mods == null || !mods.IsAdditionalKeyDown(PolylineSplitHelpers.SplitKey)))
		{
			return false;
		}
		if (ResolveMode(mods, ctx) == SplitMode.AtVertex)
		{
			if (!TryGetVertexSplit(hit, out var splitRes))
			{
				return false;
			}
			EditFunctions.RenderSplitPreview(ctx.VectorManager, ctx.Preview, splitRes, (int fid) => _polylines[fid], SplitPreviewSymbology.Polyline, "polyline_split");
			return true;
		}
		SnapResult snapResult = TryGetSplitResult(hit);
		if (!snapResult.Success)
		{
			return false;
		}
		EditFunctions.RenderSplitsPreview(ctx.VectorManager, ctx.Preview, snapResult.SplitLines, (int fid) => _polylines[fid], snapResult.SnapPoint, SplitPreviewSymbology.Polyline, "polyline_split");
		return true;
	}

	private static SplitMode ResolveMode(IEditModifiers mods, EditContext ctx)
	{
		SplitMode splitMode = ctx.Options.Get(Options.Mode);
		if (mods == null || !mods.IsAltDown())
		{
			return splitMode;
		}
		if (splitMode != SplitMode.Anywhere)
		{
			return SplitMode.Anywhere;
		}
		return SplitMode.AtVertex;
	}

	private SnapResult TryGetSplitResult(HitTestArgs hit)
	{
		double distance = hit.Window.CellSize * 100.0;
		List<FeatureSegmentSubset> elements = ((ISpatiallyIndexedSegmentCollection)_polylines).GetSegmentSpatialIndex().GetElements(hit.QueryPoint, distance);
		if (elements.Count == 0)
		{
			return SnapResult.NoSnap;
		}
		return SnapHelpers.TrySnap(hit, elements, (int fid) => _polylines[fid]);
	}

	private bool TryGetVertexSplit(HitTestArgs hit, out SplitLineResult splitRes)
	{
		double distance = hit.Window.CellSize * 100.0;
		List<FeatureSegmentSubset> elements = ((ISpatiallyIndexedSegmentCollection)_polylines).GetSegmentSpatialIndex().GetElements(hit.QueryPoint, distance);
		if (elements.Count == 0)
		{
			splitRes = SplitLineResult.NullResult;
			return false;
		}
		return SnapHelpers.TrySnapToVertexOnly(hit, elements, (int fid) => _polylines[fid], out splitRes) != SnapMode.NoSnap;
	}
}
