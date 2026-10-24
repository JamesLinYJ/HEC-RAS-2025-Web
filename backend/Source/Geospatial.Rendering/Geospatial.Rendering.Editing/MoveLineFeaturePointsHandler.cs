using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class MoveLineFeaturePointsHandler<T, K> : MovePointsHandler where T : IPointCollection, IReadOnlyList<Point>, ISpatiallyIndexedPointCollection where K : GenericEditableLine<T>
{
	protected readonly K _editable;

	public MoveLineFeaturePointsHandler(K editable)
		: base(editable.SelectedPoints)
	{
		_editable = editable;
	}

	public override bool HitTestPoint(HitTestArgs hit, out int pointIndex)
	{
		if (!_editable.HitTestClosestPart(hit, out var el, out pointIndex))
		{
			return false;
		}
		if (el == PointOrSegment.Segment)
		{
			return false;
		}
		return true;
	}

	public override HandlerResult MoveSelectedPoints(EditContext ctx, HitTestArgs hit, Vector delta)
	{
		if (!_editable.TryMoveSelectedPoints(hit, delta))
		{
			return HandlerResult.NotHandled;
		}
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	public override bool TryPreviewMoveSelectedPoints(HitTestArgs hit, Vector delta)
	{
		_editable.PreviewMovePoints(_editable.SelectedPoints.ToList(), delta);
		return true;
	}

	public override bool TryPreviewToggleSelectedPoints(HitTestArgs hit)
	{
		return _editable.TryPreviewToggleSelectedPoints(hit);
	}

	public override bool IsPointInteractionDisabled(int pointIndex)
	{
		return _editable.IsPointInteractionDisabled(pointIndex);
	}

	public static HandlerResult CreateAndBeginDrag(EditCommand cmd, EditContext ctx, K editable)
	{
		HandlerResult handlerResult = new MoveLineFeaturePointsHandler<T, K>(editable).Handle(cmd, ctx);
		if (!handlerResult.Handled || !handlerResult.IsCaptured)
		{
			throw new EditOperationInvalidStateException("MoveLineFeaturePointsHandler should've handled and captured");
		}
		return handlerResult;
	}
}
