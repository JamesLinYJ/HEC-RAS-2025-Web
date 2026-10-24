using System;
using System.Collections.Generic;

namespace Geospatial.Rendering.Editing;

public sealed class EditHandlerRouter
{
	private readonly List<(IEditHandler handler, int priority)> _handlers = new List<(IEditHandler, int)>();

	public const bool EmitCaptureChangedOperationsSilently = true;

	private IEditHandler _captured;

	private Func<IEditHandler> _cachedGetCapturedHandler;

	public IEditHandler RedoHandler = new RedoHandler();

	public IEditHandler UndoHandler = new UndoHandler();

	public bool HasCapture => _captured != null;

	public EditHandlerRouter()
	{
		RegisterUndoRedo();
	}

	public void Register(IEditHandler handler, int priority)
	{
		_handlers.Add((handler, priority));
		_handlers.Sort(((IEditHandler handler, int priority) a, (IEditHandler handler, int priority) b) => b.priority.CompareTo(a.priority));
	}

	public void Clear()
	{
		_handlers.Clear();
		RegisterUndoRedo();
	}

	private void RegisterUndoRedo()
	{
		Register(UndoHandler, -1);
		Register(RedoHandler, -1);
	}

	public RouterResult Route(EditCommand command, EditContext context)
	{
		_ = HasCapture;
		if (HasCapture)
		{
			HandlerResult handlerResult = _captured.Handle(command, context);
			if (!handlerResult.Handled)
			{
				IEditHandler redoHandler = RedoHandler;
				if (redoHandler != null && redoHandler.CanHandle(command.Command, context))
				{
					HandlerResult handlerResult2 = RedoHandler.Handle(command, context);
					if (!handlerResult2.Handled)
					{
						throw new InvalidOperationException("RedoHandler must always return true to prevent chaining of redos up the stack.");
					}
					handlerResult = handlerResult2;
				}
				else
				{
					IEditHandler undoHandler = UndoHandler;
					if (undoHandler != null && undoHandler.CanHandle(command.Command, context))
					{
						HandlerResult handlerResult3 = UndoHandler.Handle(command, context);
						if (!handlerResult3.Handled)
						{
							throw new InvalidOperationException("UndoHandler must always return true to prevent chaining of undos up the stack.");
						}
						handlerResult = handlerResult3;
					}
				}
			}
			if (handlerResult.ReleaseCapture)
			{
				if (handlerResult.TrackCaptureStateChanged && _cachedGetCapturedHandler != null)
				{
					context.ExecuteEditOperation(new CaptureChangedOperation(_cachedGetCapturedHandler, CaptureChangedOperation.Mode.ReleaseCapture), !handlerResult.IsCaptureDiscreteStep);
					context.DebugGetEditStack().ClearRedoStack();
				}
				if (_captured is IRouteEditHandler routeEditHandler)
				{
					routeEditHandler.CleanUp();
				}
				_captured = null;
				_cachedGetCapturedHandler = null;
			}
			if (handlerResult.IsCaptured)
			{
				if (handlerResult.RequestParentReleaseCapture)
				{
					throw new InvalidOperationException("If a child wants its grandparent to release and re-capture, use RequestParentSwapCapture not RequestParentReleaseCapture.");
				}
				if (handlerResult.RequestParentSwapCapture)
				{
					return new RouterResult
					{
						Handled = true,
						ChildRequestedCaptureRelease = handlerResult.RequestParentReleaseCapture,
						ChildRequestedSwapCapture = handlerResult.GetCapture,
						ChildRequestedTrackCaptureStateChanged = handlerResult.RequestParentTrackCaptureStateChanged,
						ChildRequestedBeforeSwapCapture = handlerResult.BeforeAcquireCapture
					};
				}
				if (handlerResult.BeforeAcquireCapture != null)
				{
					handlerResult.BeforeAcquireCapture(context);
				}
				_captured = handlerResult.GetCapture();
				if (handlerResult.TrackCaptureStateChanged)
				{
					_cachedGetCapturedHandler = handlerResult.GetCapture;
					context.ExecuteEditOperation(new CaptureChangedOperation(_cachedGetCapturedHandler, CaptureChangedOperation.Mode.BeginCapture), !handlerResult.IsCaptureDiscreteStep);
					context.DebugGetEditStack().ClearRedoStack();
				}
			}
			if (handlerResult.Handled)
			{
				return new RouterResult
				{
					Handled = true,
					ChildRequestedCaptureRelease = handlerResult.RequestParentReleaseCapture,
					ChildRequestedTrackCaptureStateChanged = handlerResult.RequestParentTrackCaptureStateChanged
				};
			}
			return false;
		}
		foreach (var handler in _handlers)
		{
			IEditHandler handler2 = handler.handler;
			if (!handler2.CanHandle(command.Command, context))
			{
				continue;
			}
			HandlerResult handlerResult4 = handler2.Handle(command, context);
			if (handlerResult4.IsCaptured && !handlerResult4.RequestParentSwapCapture)
			{
				if (handlerResult4.BeforeAcquireCapture != null)
				{
					handlerResult4.BeforeAcquireCapture(context);
				}
				_captured = handlerResult4.GetCapture();
				_cachedGetCapturedHandler = handlerResult4.GetCapture;
				if (handlerResult4.TrackCaptureStateChanged)
				{
					context.ExecuteEditOperation(new CaptureChangedOperation(_cachedGetCapturedHandler, CaptureChangedOperation.Mode.BeginCapture), !handlerResult4.IsCaptureDiscreteStep);
					context.DebugGetEditStack().ClearRedoStack();
				}
				return true;
			}
			if (handlerResult4.Handled)
			{
				return new RouterResult
				{
					Handled = true,
					ChildRequestedCaptureRelease = handlerResult4.RequestParentReleaseCapture,
					ChildRequestedTrackCaptureStateChanged = handlerResult4.RequestParentTrackCaptureStateChanged,
					ChildRequestedSwapCapture = (handlerResult4.RequestParentSwapCapture ? handlerResult4.GetCapture : null),
					ChildRequestedBeforeSwapCapture = handlerResult4.BeforeAcquireCapture
				};
			}
		}
		return false;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext context)
	{
		if (HasCapture)
		{
			return _captured.Preview(hit, modifiers, context);
		}
		context.Preview?.BeginFrame();
		bool result = false;
		foreach (var handler in _handlers)
		{
			IEditHandler handler2 = handler.handler;
			if (handler2.Preview(hit, modifiers, context))
			{
				result = true;
				break;
			}
		}
		context.Preview?.EndFrame();
		return result;
	}
}
