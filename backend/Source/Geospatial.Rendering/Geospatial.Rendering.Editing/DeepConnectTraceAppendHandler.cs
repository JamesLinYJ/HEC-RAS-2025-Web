using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class DeepConnectTraceAppendHandler<T, K> : IEditHandler where T : IPointCollection, IReadOnlyList<Point>, ISpatiallyIndexedPointCollection where K : GenericEditableLine<T>
{
	private readonly K _editable;

	private readonly SnapSelector _snap;

	public DeepConnectTraceAppendHandler(K editable, SnapSelector snap)
	{
		_editable = editable;
		_snap = snap;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (cmd.Command != CommandType.Click)
		{
			return HandlerResult.NotHandled;
		}
		if (!TryBuildTrace(out var points))
		{
			return HandlerResult.NotHandled;
		}
		_editable.TryAppendPoints(points);
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		if (!TryBuildTrace(out var points))
		{
			_editable.ClearSmartPathPreview();
			return false;
		}
		_editable.PreviewSmartPath(_editable.Vector[_editable.Vector.Count - 1], points);
		return true;
	}

	private bool TryBuildTrace(out List<Point> points)
	{
		points = null;
		if (_editable.Vector.Count < 1)
		{
			return false;
		}
		if (!_snap.TryGetCurrentAnchor(out var anchor, out var getLine))
		{
			return false;
		}
		SnapSelector.SnapAnchor committedAnchor = _snap.CommittedAnchor;
		if (!committedAnchor.IsValid || committedAnchor.SourceKey != anchor.SourceKey || committedAnchor.Fid != anchor.Fid)
		{
			return false;
		}
		Polyline polyline = getLine(anchor.Fid);
		if (polyline == null)
		{
			return false;
		}
		Polyline polyline2 = polyline.SubsectionDirected(committedAnchor.Position, anchor.Position);
		if (polyline2 == null || polyline2.Count == 0)
		{
			return false;
		}
		points = new List<Point>(polyline2.Count);
		for (int i = 0; i < polyline2.Count; i++)
		{
			points.Add(polyline2[i]);
		}
		Point other = _editable.Vector[_editable.Vector.Count - 1];
		if (points.Count > 0 && points[0].EqualXY(in other))
		{
			points.RemoveAt(0);
		}
		return points.Count > 0;
	}
}
