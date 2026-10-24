using System;

namespace Geospatial.Rendering.Editing;

public class CaptureChangedOperation : EditOperation
{
	public enum Mode
	{
		BeginCapture,
		ReleaseCapture
	}

	private Func<IEditHandler> _makeCapturedHandler;

	public override bool IsNoOp => true;

	public override string Description => "";

	public Func<IEditHandler> MakeCapturedHandler => _makeCapturedHandler;

	public Mode CaptureMode { get; }

	public CaptureChangedOperation(Func<IEditHandler> makeCapturedHandler, Mode mode)
		: base(null)
	{
		_makeCapturedHandler = makeCapturedHandler;
		CaptureMode = mode;
	}

	protected override bool ApplyInternal()
	{
		return true;
	}

	protected override bool UndoInternal()
	{
		return true;
	}

	public override string ToString()
	{
		return $"CaptureChangedOp - {CaptureMode}";
	}
}
