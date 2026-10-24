using System;
using Geospatial.Rasters;
using Geospatial.Rendering.TiledRenderingSystem;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class PanInteractionHandler : IEditHandler
{
	private readonly PyramidedTileManager _map;

	private readonly bool _handleLeftButton;

	private readonly bool _handleRightButton;

	private bool _isPanning;

	private Point _previousScreenPoint;

	private RasterDefinition _dragStartRaster;

	private double _xOffset;

	private double _yOffset;

	public PanInteractionHandler(PyramidedTileManager map, bool handleLeftButton = true, bool handleRightButton = true)
	{
		_map = map ?? throw new ArgumentNullException("map");
		_handleLeftButton = handleLeftButton;
		_handleRightButton = handleRightButton;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (type != CommandType.BeginDragging)
		{
			return type == CommandType.DoubleClick;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		return cmd.Command switch
		{
			CommandType.BeginDragging => HandleBegin(cmd), 
			CommandType.Dragging => HandleDrag(cmd), 
			CommandType.FinishDragging => HandleFinish(cmd), 
			CommandType.CancelDragging => HandleCancel(), 
			CommandType.Close => HandleCancel(), 
			CommandType.DoubleClick => HandleDoubleClick(cmd), 
			_ => HandlerResult.NotHandled, 
		};
	}

	public bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext ctx)
	{
		return false;
	}

	private HandlerResult HandleBegin(EditCommand cmd)
	{
		if (!ShouldHandle(cmd.Modifiers) || cmd.Location == null)
		{
			return HandlerResult.NotHandled;
		}
		_isPanning = true;
		_dragStartRaster = cmd.Location.Window;
		_previousScreenPoint = ConvertWorldToScreen(cmd.Location.QueryPoint, _dragStartRaster);
		_map.TileCanvas.Transform.ExtractTranslation(out _xOffset, out _yOffset);
		return new HandlerResult
		{
			Handled = true,
			GetCapture = () => this
		};
	}

	private HandlerResult HandleDrag(EditCommand cmd)
	{
		if (!_isPanning || cmd.Location == null)
		{
			return HandlerResult.NotHandled;
		}
		ApplyPan(cmd.Location);
		return HandlerResult.WasHandled;
	}

	private HandlerResult HandleFinish(EditCommand cmd)
	{
		if (!_isPanning)
		{
			return HandlerResult.NotHandled;
		}
		_isPanning = false;
		FinishPan();
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	private HandlerResult HandleCancel()
	{
		if (!_isPanning)
		{
			return HandlerResult.NotHandled;
		}
		_isPanning = false;
		FinishPan();
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	private HandlerResult HandleDoubleClick(EditCommand cmd)
	{
		if (ShouldHandle(cmd.Modifiers, requireDown: false))
		{
			IEditModifiers modifiers = cmd.Modifiers;
			if (modifiers == null || !modifiers.IsRightClick())
			{
				if (cmd.Location == null)
				{
					return HandlerResult.NotHandled;
				}
				if (_map == null)
				{
					return HandlerResult.NotHandled;
				}
				RasterDefinition liveScreenRaster = _map.LiveScreenRaster;
				if (!liveScreenRaster.Extent.IsFiniteAndValid())
				{
					return HandlerResult.NotHandled;
				}
				_map.SetWindow_Animated(liveScreenRaster.Extent.Ratio(0.25, cmd.Location.QueryPoint), render: true, 0.3, frameToVisibleArea: false);
				return HandlerResult.WasHandled;
			}
		}
		return HandlerResult.NotHandled;
	}

	private void ApplyPan(HitTestArgs hit)
	{
		if (hit != null && _dragStartRaster.IsValid() && !(Math.Abs(_dragStartRaster.CellSize) < double.Epsilon))
		{
			Point point = ConvertWorldToScreen(hit.QueryPoint, hit.Window);
			double pointPreviousScreenPointTerm = point.X - _previousScreenPoint.X;
			double pointPreviousScreenPointTerm2 = point.Y - _previousScreenPoint.Y;
			if (!(Math.Abs(pointPreviousScreenPointTerm) < double.Epsilon) || !(Math.Abs(pointPreviousScreenPointTerm2) < double.Epsilon))
			{
				_map.PartialPan(pointPreviousScreenPointTerm, pointPreviousScreenPointTerm2);
				_map.QueueRenderTiles();
			}
		}
	}

	private void FinishPan()
	{
		_map.FinishPan();
		_map.QueueRenderTiles();
	}

	private Point ConvertWorldToScreen(Point worldPoint, RasterDefinition raster)
	{
		if (!raster.IsValid())
		{
			return Point.NullPoint;
		}
		double x = raster.FractionalCol(worldPoint.X);
		double y = raster.FractionalRow(worldPoint.Y);
		return new Point(x, y);
	}

	private bool ShouldHandle(IEditModifiers modifiers, bool requireDown = true)
	{
		bool modifiersIsRightClickTerm = modifiers?.IsRightClick() ?? false;
		if (modifiersIsRightClickTerm && !_handleRightButton)
		{
			return false;
		}
		if (!modifiersIsRightClickTerm && !_handleLeftButton)
		{
			return false;
		}
		if (requireDown && !_handleLeftButton && !_handleRightButton)
		{
			return false;
		}
		return true;
	}
}
