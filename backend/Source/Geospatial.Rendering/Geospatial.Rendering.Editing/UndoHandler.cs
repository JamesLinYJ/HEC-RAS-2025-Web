using System.Collections.Generic;

namespace Geospatial.Rendering.Editing;

public class UndoHandler : UndoRedoHandlerBase
{
	public override bool CanHandle(CommandType type, EditContext context)
	{
		if (type == CommandType.Undo)
		{
			return context.DebugGetEditStack().UndoStackCount > 0;
		}
		return false;
	}

	protected override bool TryExecuteStackOperation(EditStack editStack, out IReadOnlyList<EditOperation> operations)
	{
		return editStack.TryUndo(out operations);
	}

	protected override CaptureChangedOperation.Mode GetCaptureModeForResult()
	{
		return CaptureChangedOperation.Mode.ReleaseCapture;
	}
}
