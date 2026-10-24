using System;

namespace Geospatial.Rendering.Editing;

public class CompleteableEditArcFeatureHandler : EditLineFeatureHandler<ArcPolylineWrapper, EditableArc>
{
	private NodeArcEditContext _context;

	public CompleteableEditArcFeatureHandler(NodeArcEditContext context, EditableArc editable, Action<EditHandlerRouter> registerChildren)
		: base(editable, editable.ArcFID, registerChildren)
	{
		_context = context;
	}

	protected override HandlerResult HandleClose(EditContext ctx, HitTestArgs hit)
	{
		if (_editable.Arc.InternalPointCount == 0)
		{
			if (ctx.DebugGetEditStack().TryPeekUndoStack(out var op) && op is DeleteControlPointsOperation)
			{
				ctx.DebugGetEditStack().TryUndo();
			}
			ctx.DebugGetEditStack().DeleteTopOfUndoStack();
			ctx.DebugGetEditStack().TryUndo();
			ctx.DebugGetEditStack().ClearRedoStack();
			return new HandlerResult
			{
				Handled = true,
				ReleaseCapture = true
			};
		}
		CompleteArcResultPackage completeArcResultPackage = CompleteArcHelpers.TryCompleteArc(_context, _editable.ArcFID, ctx, hit, CompleteArcMode.All);
		if (completeArcResultPackage.Result == CompleteArcResult.Rejection)
		{
			return HandlerResult.WasHandled;
		}
		if (completeArcResultPackage.Result == CompleteArcResult.Failure)
		{
			return HandlerResult.NotHandled;
		}
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true,
			TrackCaptureStateChanged = true
		};
	}
}
