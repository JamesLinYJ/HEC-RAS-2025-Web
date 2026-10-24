using System;

namespace Geospatial.Rendering.Editing;

public sealed class HandlerResult
{
	public bool Handled { get; init; }

	public Func<IEditHandler> GetCapture { get; init; }

	public bool IsCaptured => GetCapture != null;

	public bool IsCaptureDiscreteStep { get; init; }

	public Action<EditContext> BeforeAcquireCapture { get; init; }

	public bool TrackCaptureStateChanged { get; init; }

	public bool ReleaseCapture { get; set; }

	public bool RequestParentReleaseCapture { get; init; }

	public bool RequestParentSwapCapture { get; init; }

	public bool RequestParentTrackCaptureStateChanged { get; init; }

	public static HandlerResult NotHandled => new HandlerResult
	{
		Handled = false
	};

	public static HandlerResult WasHandled => new HandlerResult
	{
		Handled = true
	};
}
