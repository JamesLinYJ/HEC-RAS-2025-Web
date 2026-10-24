using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class EditLineFeatureHandler<T, K> : EditFeatureHandler where T : IPointCollection, IReadOnlyList<Point>, ISpatiallyIndexedPointCollection where K : GenericEditableLine<T>
{
	protected readonly K _editable;

	public K Debug_Editable => _editable;

	public EditLineFeatureHandler(K editable, int fid, Action<EditHandlerRouter> registerChildren)
		: base(fid, registerChildren)
	{
		_editable = editable;
	}

	public override HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		bool notHandledAtAll;
		return HandleInternal(cmd, ctx, out notHandledAtAll);
	}

	protected override HandlerResult HandleClose(EditContext ctx, HitTestArgs hit)
	{
		_editable.ClearAllVectors();
		_editable.SelectedPoints.Clear();
		if (_editable.Count <= 1)
		{
			if (ctx.DebugGetEditStack().TryPeekUndoStack(out var op, allowSilentOperations: false, allowNoOps: true) && op is CaptureChangedOperation)
			{
				ctx.DebugGetEditStack().DeleteTopOfUndoStack();
			}
			if (ctx.DebugGetEditStack().TryPeekUndoStack(out var op2) && op2 is NewFeatureOperation<T>)
			{
				ctx.DebugGetEditStack().TryUndo();
				ctx.DebugGetEditStack().ClearRedoStack();
			}
			return new HandlerResult
			{
				Handled = true,
				ReleaseCapture = true
			};
		}
		return base.HandleClose(ctx, hit);
	}

	public override bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		_editable.Redraw();
		return base.Preview(hit, mods, ctx);
	}

	public override void CleanUp()
	{
		_editable.ClearAllVectors();
	}
}
