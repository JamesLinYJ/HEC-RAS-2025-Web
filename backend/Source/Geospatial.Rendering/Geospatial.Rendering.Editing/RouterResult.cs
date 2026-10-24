using System;

namespace Geospatial.Rendering.Editing;

public struct RouterResult
{
	public bool Handled { get; init; }

	public bool ChildRequestedCaptureRelease { get; init; }

	public bool ChildRequestedTrackCaptureStateChanged { get; init; }

	public Func<IEditHandler> ChildRequestedSwapCapture { get; init; }

	public Action<EditContext> ChildRequestedBeforeSwapCapture { get; init; }

	public static implicit operator bool(RouterResult result)
	{
		return result.Handled;
	}

	public static implicit operator RouterResult(bool result)
	{
		return new RouterResult
		{
			Handled = result
		};
	}
}
