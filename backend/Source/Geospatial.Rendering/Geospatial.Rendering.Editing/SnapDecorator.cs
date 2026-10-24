using System;

namespace Geospatial.Rendering.Editing;

public sealed class SnapDecorator : IRouteEditHandler, IEditHandler
{
	private readonly IEditHandler _inner;

	private readonly SnapSelector _snap;

	public SnapDecorator(IEditHandler inner, SnapSelector snap)
	{
		_inner = inner;
		_snap = snap;
	}

	public bool CanHandle(CommandType type, EditContext context)
	{
		return _inner.CanHandle(type, context);
	}

	public HandlerResult Handle(EditCommand command, EditContext context)
	{
		HitTestArgs location = TrySnapHit(command.Location, context, render: false);
		EditCommand editCommand = command;
		editCommand.Location = location;
		EditCommand command2 = editCommand;
		HandlerResult result = _inner.Handle(command2, context);
		if (command.Command == CommandType.Click)
		{
			_snap.PromoteCommitted();
		}
		_snap.Clear();
		return WrapCapture(result);
	}

	public bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext context)
	{
		HitTestArgs hit2 = TrySnapHit(hit, context, render: true);
		return _inner.Preview(hit2, modifiers, context);
	}

	public void CleanUp()
	{
		_snap.ResetTrace();
		if (_inner is IRouteEditHandler routeEditHandler)
		{
			routeEditHandler.CleanUp();
		}
	}

	private HitTestArgs TrySnapHit(HitTestArgs hit, EditContext context, bool render)
	{
		if (hit == null)
		{
			_snap.Clear();
			return null;
		}
		if (!_snap.TrySnap(hit, context.Options.Get(DeepConnectOptions.SnapTolerancePixels)))
		{
			_snap.Clear();
			return hit;
		}
		if (render)
		{
			_snap.Render();
		}
		return new HitTestArgs(_snap.SnapPoint, hit.Window);
	}

	private HandlerResult WrapCapture(HandlerResult result)
	{
		if (result == null || result.GetCapture == null)
		{
			return result;
		}
		Func<IEditHandler> innerGetCapture = result.GetCapture;
		SnapSelector snap = _snap;
		return new HandlerResult
		{
			Handled = result.Handled,
			GetCapture = () => new SnapDecorator(innerGetCapture(), snap),
			IsCaptureDiscreteStep = result.IsCaptureDiscreteStep,
			BeforeAcquireCapture = result.BeforeAcquireCapture,
			TrackCaptureStateChanged = result.TrackCaptureStateChanged,
			ReleaseCapture = result.ReleaseCapture,
			RequestParentReleaseCapture = result.RequestParentReleaseCapture,
			RequestParentSwapCapture = result.RequestParentSwapCapture,
			RequestParentTrackCaptureStateChanged = result.RequestParentTrackCaptureStateChanged
		};
	}
}
