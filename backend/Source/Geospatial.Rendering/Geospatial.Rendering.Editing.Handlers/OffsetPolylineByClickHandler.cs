using System;
using System.Collections.Generic;
using Geospatial.Helpers;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers;

public sealed class OffsetPolylineByClickHandler : IEditHandler
{
	public static class Options
	{
		public static readonly OptionKey<double> OffsetDistance = OptionKey<double>.Define("Polyline.Offset.Distance", "Offset Distance", "Distance between the input polyline and each offset, in map units.", 10.0);

		public static readonly OptionKey<OffsetSides> Sides = OptionKey<OffsetSides>.Define("Polyline.Offset.Sides", "Banks", "Which bank(s) to emit, relative to the direction the input line is drawn in.", OffsetSides.Both);

		public static readonly OptionKey<bool> IncludeEndpoints = OptionKey<bool>.Define("Polyline.Offset.IncludeEndpoints", "Include Endpoints", "Also emit a line across each end, connecting the offsets.", defaultValue: false);

		public static readonly OptionKey<bool> KeepOriginal = OptionKey<bool>.Define("Polyline.Offset.KeepOriginal", "Keep Original", "Leave the clicked polyline in place instead of replacing it with its offsets.", defaultValue: false);
	}

	private static readonly string[] PreviewChannels = new string[4] { "polyline_offset_0", "polyline_offset_1", "polyline_offset_2", "polyline_offset_3" };

	private readonly IPolylineCollection _polylines;

	private readonly VectorID[] _previewIds = new VectorID[PreviewChannels.Length];

	public OffsetPolylineByClickHandler(IPolylineCollection polylines)
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
		List<Polyline> polylines = new List<Polyline>(PreviewChannels.Length);
		if (!TryComputeParts(_polylines[fid], ctx, polylines))
		{
			return HandlerResult.NotHandled;
		}
		bool optionsCtxTerm = ctx.Options.Get(Options.KeepOriginal);
		int[] originalFids = (optionsCtxTerm ? Array.Empty<int>() : new int[1] { fid });
		string description = (optionsCtxTerm ? "Offset polyline (keep original)" : "Offset polyline");
		ReplacePolylinesOperation op = new ReplacePolylinesOperation(_polylines, originalFids, polylines, description);
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
		List<Polyline> polylines = new List<Polyline>(PreviewChannels.Length);
		if (!TryComputeParts(_polylines[fid], ctx, polylines))
		{
			return false;
		}
		int count = Math.Min(polylines.Count, PreviewChannels.Length);
		for (int i = 0; i < count; i++)
		{
			ctx.Preview.TryGetVector(PreviewChannels[i], out _previewIds[i]);
			ctx.VectorManager.AddOrUpdateVector(ref _previewIds[i], polylines[i], DefaultEditSymbology.AppendSegmentSymbology);
			ctx.Preview.RegisterVector(PreviewChannels[i], _previewIds[i]);
		}
		return true;
	}

	private static bool TryComputeParts(Polyline source, EditContext ctx, List<Polyline> parts)
	{
		double width = ctx.Options.Get(Options.OffsetDistance);
		OffsetSides sides = ctx.Options.Get(Options.Sides);
		bool includeEndpoints = ctx.Options.Get(Options.IncludeEndpoints);
		return OffsetPolylineFactory.TryComputeOffsetParts(source, width, sides, includeEndpoints, parts);
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
