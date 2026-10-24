using System;

namespace Geospatial.Rendering.Editing;

public class EditFeatureHandler : IRouteEditHandler, IEditHandler
{
	protected readonly int _fid;

	protected readonly EditHandlerRouter _child;

	public EditFeatureHandler(int fid, Action<EditHandlerRouter> registerChildren)
	{
		_fid = fid;
		_child = new EditHandlerRouter();
		registerChildren?.Invoke(_child);
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return true;
	}

	public virtual HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		bool notHandledAtAll;
		return HandleInternal(cmd, ctx, out notHandledAtAll);
	}

	protected HandlerResult HandleInternal(EditCommand cmd, EditContext ctx, out bool notHandledAtAll)
	{
		notHandledAtAll = false;
		RouterResult routerResult = _child.Route(cmd, ctx);
		if ((bool)routerResult)
		{
			return new HandlerResult
			{
				Handled = true,
				ReleaseCapture = (routerResult.ChildRequestedSwapCapture != null || routerResult.ChildRequestedCaptureRelease),
				TrackCaptureStateChanged = routerResult.ChildRequestedTrackCaptureStateChanged,
				GetCapture = routerResult.ChildRequestedSwapCapture,
				BeforeAcquireCapture = routerResult.ChildRequestedBeforeSwapCapture
			};
		}
		if (cmd.Command == CommandType.Close || cmd.Command == CommandType.DoubleClick)
		{
			return HandleClose(ctx, cmd.Location);
		}
		notHandledAtAll = true;
		return HandlerResult.NotHandled;
	}

	protected virtual HandlerResult HandleClose(EditContext ctx, HitTestArgs hit)
	{
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true,
			TrackCaptureStateChanged = true,
			IsCaptureDiscreteStep = true
		};
	}

	public virtual bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		return _child.Preview(hit, mods, ctx);
	}

	public virtual void CleanUp()
	{
	}
}
