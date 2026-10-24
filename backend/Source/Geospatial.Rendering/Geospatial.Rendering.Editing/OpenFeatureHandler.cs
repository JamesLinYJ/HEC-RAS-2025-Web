using System;

namespace Geospatial.Rendering.Editing;

public class OpenFeatureHandler : IEditHandler
{
	protected readonly Func<HitTestArgs, int> _hitClosest;

	protected readonly Func<int, EditContext, IEditHandler> _buildEditHandler;

	public OpenFeatureHandler(Func<HitTestArgs, int> hitClosest, Func<int, EditContext, IEditHandler> buildEditHandler)
	{
		_hitClosest = hitClosest;
		_buildEditHandler = buildEditHandler;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (type != CommandType.DoubleClick)
		{
			return type == CommandType.Open;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (cmd.Command != CommandType.DoubleClick && cmd.Command != CommandType.Open)
		{
			return HandlerResult.NotHandled;
		}
		int fid = _hitClosest(cmd.Location);
		if (fid == -1)
		{
			return HandlerResult.NotHandled;
		}
		return new HandlerResult
		{
			Handled = true,
			GetCapture = () => _buildEditHandler(fid, ctx),
			BeforeAcquireCapture = OnOpening,
			IsCaptureDiscreteStep = true,
			TrackCaptureStateChanged = true
		};
	}

	protected virtual void OnOpening(EditContext ctx)
	{
	}

	public virtual bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		return false;
	}
}
