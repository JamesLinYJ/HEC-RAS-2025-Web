using System.Linq;
using Geospatial.Rendering.Editing.Handlers.Topology;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class MoveArcPointsHandler : MoveLineFeaturePointsHandler<ArcPolylineWrapper, EditableArc>
{
	private NodeArcEditContext _context;

	public MoveArcPointsHandler(NodeArcEditContext context, EditableArc editable)
		: base(editable)
	{
		_context = context;
	}

	public override HandlerResult MoveSelectedPoints(EditContext ctx, HitTestArgs hit, Vector delta)
	{
		if (!base.MoveSelectedPoints(ctx, hit, delta).Handled)
		{
			return new HandlerResult
			{
				Handled = true,
				ReleaseCapture = true
			};
		}
		if (AppendArcPointHandler.TryCompleteAndBeginNewArc(_context, ctx, hit, _editable, out var completeIntoNewArcHandler) == CompleteArcResult.Success)
		{
			completeIntoNewArcHandler.ReleaseCapture = true;
			return completeIntoNewArcHandler;
		}
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	public override bool TryPreviewMoveSelectedPoints(HitTestArgs hit, Vector delta)
	{
		if (_editable.Mode == EditableArc.EditingMode.Adding)
		{
			if (_editable.TryPreviewSnapToNode(hit, EditableArc.SnapMode.MoveSnap))
			{
				return true;
			}
			if (_editable.TryPreviewSnapToArc(hit, EditableArc.SnapMode.MoveSnap))
			{
				return true;
			}
		}
		_editable.PreviewMovePoints(_editable.SelectedPoints.ToList(), delta);
		return true;
	}
}
