namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class AppendArcPointHandler : IEditHandler
{
	private readonly EditableArc _editable;

	private readonly NodeArcEditContext _context;

	public AppendArcPointHandler(EditableArc editable, NodeArcEditContext context)
	{
		_editable = editable;
		_context = context;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (type != CommandType.Click)
		{
			return type == CommandType.BeginDragging;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		HandlerResult completeIntoNewArcHandler;
		switch (TryCompleteAndBeginNewArc(_context, ctx, cmd.Location, _editable, out completeIntoNewArcHandler))
		{
		case CompleteArcResult.Success:
			return completeIntoNewArcHandler;
		case CompleteArcResult.Rejection:
			return HandlerResult.WasHandled;
		default:
			if (!_editable.TryAppendPoint(cmd.Location))
			{
				return HandlerResult.NotHandled;
			}
			_editable.ClearSelectedPoints();
			_editable.SelectPoints(_editable.Count - 1);
			if (cmd.Command == CommandType.Click)
			{
				return HandlerResult.WasHandled;
			}
			if (cmd.Command == CommandType.BeginDragging)
			{
				return new HandlerResult
				{
					Handled = true,
					GetCapture = () =>
					{
						MoveArcPointsHandler moveArcPointsHandler = new MoveArcPointsHandler(_context, _editable);
						moveArcPointsHandler.Handle(cmd, ctx);
						return moveArcPointsHandler;
					},
					TrackCaptureStateChanged = false
				};
			}
			return HandlerResult.NotHandled;
		}
	}

	public static CompleteArcResult TryCompleteAndBeginNewArc(NodeArcEditContext context, EditContext ctx, HitTestArgs hit, EditableArc editable, out HandlerResult completeIntoNewArcHandler)
	{
		if (editable.Mode != EditableArc.EditingMode.Adding)
		{
			completeIntoNewArcHandler = HandlerResult.NotHandled;
			return CompleteArcResult.Failure;
		}
		CompleteArcResultPackage completeArcResultPackage = CompleteArcHelpers.TryCompleteArc(context, editable.ArcFID, ctx, hit, CompleteArcMode.SplitArcOrExistingNode);
		if (completeArcResultPackage.Result == CompleteArcResult.Success)
		{
			int arcCount = context.Network.ArcCount;
			int endNodeFid = editable.Arc.EndNode;
			completeIntoNewArcHandler = new HandlerResult
			{
				Handled = true,
				RequestParentSwapCapture = true,
				RequestParentTrackCaptureStateChanged = true,
				BeforeAcquireCapture = (EditContext ectx) =>
				{
					ectx.ExecuteEditOperation(new NewArcOperation(context, hit, endNodeFid));
				},
				GetCapture = AddNewArcHandler.GetNewArcEditCapture(arcCount, context, ctx)
			};
			return CompleteArcResult.Success;
		}
		completeIntoNewArcHandler = HandlerResult.NotHandled;
		return completeArcResultPackage.Result;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		if (_editable.TryPreviewSnapToNode(hit))
		{
			return true;
		}
		if (_editable.TryPreviewSnapToArc(hit))
		{
			return true;
		}
		_editable.PreviewAppendPoint(hit.QueryPoint);
		return true;
	}
}
