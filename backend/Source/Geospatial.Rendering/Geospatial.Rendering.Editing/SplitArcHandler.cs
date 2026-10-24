using System;
using Geospatial.Rendering.Editing.Handlers;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class SplitArcHandler : IEditHandler
{
	public static class Options
	{
		public static readonly OptionKey<SplitMode> Mode = OptionKey<SplitMode>.Define("SplitArc.Mode", "Split Mode", "Where the split occurs on an arc. Alt swaps to the non-default mode.", SplitMode.Anywhere);
	}

	private readonly NodeArcEditContext _context;

	private readonly OptionKey<SplitMode> _modeKey;

	private readonly Func<int, EditContext, Func<IEditHandler>> _getNewArcEditCapture;

	private readonly bool _requireSKey;

	private readonly bool _startNewArcOnSplit;

	public SplitArcHandler(NodeArcEditContext context, OptionKey<SplitMode> modeKey, Func<int, EditContext, Func<IEditHandler>> getNewArcEditCapture, bool requireSKey = true, bool startNewArcOnSplit = false)
	{
		_context = context;
		_modeKey = modeKey;
		_getNewArcEditCapture = getNewArcEditCapture;
		_requireSKey = requireSKey;
		_startNewArcOnSplit = startNewArcOnSplit;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (_startNewArcOnSplit)
		{
			if (type != CommandType.Open)
			{
				return type == CommandType.DoubleClick;
			}
			return true;
		}
		if (type != CommandType.Click && type != CommandType.Open)
		{
			return type == CommandType.DoubleClick;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (_requireSKey && !cmd.Modifiers.IsAdditionalKeyDown(TopologyEditingDefaults.DefaultSplitKey))
		{
			return HandlerResult.NotHandled;
		}
		SplitMode splitMode = ResolveMode(cmd.Modifiers, ctx);
		if (!NetworkRenderLayer.HitTest(_context.Network, cmd.Location, out var element, out var _, out var _))
		{
			return HandlerResult.NotHandled;
		}
		if (element != NetworkElementType.ArcSegment && element != NetworkElementType.ArcInternalPoint)
		{
			return HandlerResult.NotHandled;
		}
		if (splitMode == SplitMode.AtVertex)
		{
			if (!TryGetVertexHitTestArgs(cmd.Location, out var vertexHit))
			{
				return HandlerResult.WasHandled;
			}
			ctx.ExecuteEditOperation(new SplitArcOperation(_context, vertexHit));
		}
		else
		{
			ctx.ExecuteEditOperation(new SplitArcOperation(_context, cmd.Location));
		}
		int nodeFid = _context.Network.NodeCount - 1;
		if (cmd.Command == CommandType.Click)
		{
			return HandlerResult.WasHandled;
		}
		int arcCount = _context.Network.ArcCount;
		if (cmd.Command == CommandType.Open || cmd.Command == CommandType.DoubleClick)
		{
			return new HandlerResult
			{
				Handled = true,
				BeforeAcquireCapture = (EditContext ectx) =>
				{
					_context.OpenFeatureSelectedSetCache.Clear();
					ectx.ExecuteEditOperation(new NewArcOperation(_context, cmd.Location, nodeFid));
				},
				GetCapture = _getNewArcEditCapture(arcCount, ctx),
				TrackCaptureStateChanged = true
			};
		}
		throw new NotImplementedException();
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		if (_requireSKey && (mods == null || !mods.IsAdditionalKeyDown(TopologyEditingDefaults.DefaultSplitKey)))
		{
			return false;
		}
		if (ResolveMode(mods, ctx) == SplitMode.AtVertex)
		{
			return _context.EditableNetwork.TryPreviewSplitArcsAtVertex(hit);
		}
		return _context.EditableNetwork.TryPreviewSplitArcs(hit);
	}

	private SplitMode ResolveMode(IEditModifiers mods, EditContext ctx)
	{
		SplitMode splitMode = ctx.Options.Get(_modeKey);
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

	private bool TryGetVertexHitTestArgs(HitTestArgs hit, out HitTestArgs vertexHit)
	{
		if (!NetworkRenderLayer.HitTest(_context.Network, hit, out var _, out var elementIndex, out var _))
		{
			vertexHit = null;
			return false;
		}
		Polyline arcPolyline = _context.Network.GetArcPolyline(elementIndex);
		if (arcPolyline.Count < 3)
		{
			vertexHit = null;
			return false;
		}
		int nearestPoint = arcPolyline.NearestPoint(hit.QueryPoint);
		if (nearestPoint == 0)
		{
			nearestPoint = 1;
		}
		else if (nearestPoint == arcPolyline.Count - 1)
		{
			nearestPoint = arcPolyline.Count - 2;
		}
		Point pt = arcPolyline[nearestPoint];
		vertexHit = new HitTestArgs(pt, hit.Window);
		return true;
	}
}
