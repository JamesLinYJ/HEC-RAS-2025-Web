using System.Collections.Generic;

namespace Geospatial.Rendering.Editing;

public class RedoHandler : UndoRedoHandlerBase
{
	public override bool CanHandle(CommandType type, EditContext context)
	{
		if (type == CommandType.Redo)
		{
			return context.DebugGetEditStack().RedoStackCount > 0;
		}
		return false;
	}

	protected override bool TryExecuteStackOperation(EditStack editStack, out IReadOnlyList<EditOperation> operations)
	{
		return editStack.TryRedo(out operations);
	}

	protected override CaptureChangedOperation.Mode GetCaptureModeForResult()
	{
		return CaptureChangedOperation.Mode.BeginCapture;
	}
}
