using System;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class MarqueeSelectionHandler : IEditHandler
{
	public enum Shape
	{
		Extent,
		Path,
		Lasso
	}

	public static class Options
	{
		public static readonly OptionKey<Shape> SelectionShape = OptionKey<Shape>.Define("Marquee.SelectionShape", "Selection Shape", "Whether selection uses a rubber-band rectangle or a freeform lasso.", Shape.Extent);
	}

	private readonly ExtentSelectionHandler _extent;

	private readonly PathSelectionHandler _path;

	private readonly LassoSelectionHandler _lasso;

	private IEditHandler _captured;

	public MarqueeSelectionHandler(Action<Extent, SelectMode> selectExtent, Action<Polyline, SelectMode> selectPath, Action<Polygon, MarqueeHitMode, SelectMode> selectLasso)
	{
		ArgumentNullException.ThrowIfNull(selectExtent, "selectExtent");
		ArgumentNullException.ThrowIfNull(selectPath, "selectPath");
		ArgumentNullException.ThrowIfNull(selectLasso, "selectLasso");
		_extent = new ExtentSelectionHandler(selectExtent);
		_path = new PathSelectionHandler(selectPath);
		_lasso = new LassoSelectionHandler(selectLasso);
	}

	private IEditHandler Pick(EditContext ctx)
	{
		return ctx.Options.Get(Options.SelectionShape) switch
		{
			Shape.Path => (IEditHandler)_path, 
			Shape.Lasso => _lasso, 
			_ => _extent, 
		};
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return (_captured ?? Pick(ctx)).CanHandle(type, ctx);
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		IEditHandler editHandler = _captured ?? Pick(ctx);
		HandlerResult handlerResult = editHandler.Handle(cmd, ctx);
		if (handlerResult.GetCapture != null)
		{
			_captured = editHandler;
			return new HandlerResult
			{
				Handled = handlerResult.Handled,
				GetCapture = () => this,
				ReleaseCapture = handlerResult.ReleaseCapture,
				TrackCaptureStateChanged = handlerResult.TrackCaptureStateChanged,
				IsCaptureDiscreteStep = handlerResult.IsCaptureDiscreteStep,
				BeforeAcquireCapture = handlerResult.BeforeAcquireCapture
			};
		}
		if (handlerResult.ReleaseCapture)
		{
			_captured = null;
		}
		return handlerResult;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		return (_captured ?? Pick(ctx)).Preview(hit, mods, ctx);
	}
}
