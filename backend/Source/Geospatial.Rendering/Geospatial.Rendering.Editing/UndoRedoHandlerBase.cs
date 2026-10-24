using System;
using System.Collections.Generic;

namespace Geospatial.Rendering.Editing;

public abstract class UndoRedoHandlerBase : IEditHandler
{
	protected abstract bool TryExecuteStackOperation(EditStack editStack, out IReadOnlyList<EditOperation> operations);

	protected abstract CaptureChangedOperation.Mode GetCaptureModeForResult();

	public abstract bool CanHandle(CommandType type, EditContext context);

	public HandlerResult Handle(EditCommand command, EditContext context)
	{
		EditStack getEditStack = context.DebugGetEditStack();
		if (!TryExecuteStackOperation(getEditStack, out var operations))
		{
			return HandlerResult.NotHandled;
		}
		HandlerResult handlerResult = null;
		foreach (EditOperation operation in operations)
		{
			if (operation.IsNoOp && operation is CaptureChangedOperation captureChangedOperation)
			{
				if (handlerResult != null)
				{
					throw new InvalidOperationException("Can not currently handle multiple CaptureChangedPlaceholderOperation in a single undo.");
				}
				handlerResult = ((captureChangedOperation.CaptureMode != GetCaptureModeForResult()) ? new HandlerResult
				{
					Handled = true,
					ReleaseCapture = true,
					RequestParentReleaseCapture = true,
					TrackCaptureStateChanged = false
				} : new HandlerResult
				{
					Handled = true,
					GetCapture = captureChangedOperation.MakeCapturedHandler,
					TrackCaptureStateChanged = false
				});
			}
		}
		if (handlerResult == null)
		{
			handlerResult = HandlerResult.WasHandled;
		}
		return handlerResult;
	}

	public virtual bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext context)
	{
		return false;
	}
}
