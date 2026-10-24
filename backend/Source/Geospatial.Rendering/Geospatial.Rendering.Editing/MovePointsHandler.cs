using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Rendering.Editing;

public abstract class MovePointsHandler : IEditHandler
{
	private static readonly ILogger _logger = LogHelper.GetLogger("MovePointsHandler");

	protected Point _dragStartPoint;

	protected bool _isDragging;

	protected SelectedSet _selected;

	public MovePointsHandler(SelectedSet selected)
	{
		_selected = selected;
	}

	public abstract bool HitTestPoint(HitTestArgs hit, out int pointIndex);

	public abstract HandlerResult MoveSelectedPoints(EditContext ctx, HitTestArgs hit, Vector delta);

	public abstract bool TryPreviewMoveSelectedPoints(HitTestArgs hit, Vector delta);

	public abstract bool TryPreviewToggleSelectedPoints(HitTestArgs hit);

	public virtual bool IsPointInteractionDisabled(int pointIndex)
	{
		return false;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (!_isDragging)
		{
			return type == CommandType.BeginDragging;
		}
		return false;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (cmd.Command == CommandType.BeginDragging)
		{
			if (!HitTestPoint(cmd.Location, out var pointIndex))
			{
				return HandlerResult.NotHandled;
			}
			if (!_selected.Contains(pointIndex))
			{
				if (IsPointInteractionDisabled(pointIndex))
				{
					return HandlerResult.NotHandled;
				}
				_selected.Clear();
				_selected.Add(pointIndex);
			}
			_dragStartPoint = cmd.Location.QueryPoint;
			_isDragging = true;
			return new HandlerResult
			{
				Handled = true,
				GetCapture = () => this
			};
		}
		if (cmd.Command == CommandType.Click)
		{
			if (_isDragging)
			{
				return new HandlerResult
				{
					Handled = true,
					ReleaseCapture = true
				};
			}
		}
		else if (cmd.Command == CommandType.FinishDragging)
		{
			if (!_isDragging)
			{
				throw new EditOperationInvalidStateException("Finishing interaction, but _isDragging is false?..");
			}
			HandlerResult result = MoveSelectedPoints(delta: new Vector(cmd.Location.QueryPoint.X - _dragStartPoint.X, cmd.Location.QueryPoint.Y - _dragStartPoint.Y), ctx: ctx, hit: cmd.Location);
			_isDragging = false;
			return result;
		}
		return HandlerResult.NotHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return _isDragging;
		}
		if (_isDragging)
		{
			Vector delta = new Vector(hit.QueryPoint.X - _dragStartPoint.X, hit.QueryPoint.Y - _dragStartPoint.Y);
			TryPreviewMoveSelectedPoints(hit, delta);
			return true;
		}
		try
		{
			return TryPreviewToggleSelectedPoints(hit);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Previewing toggle selected points.");
			_selected.Clear();
		}
		return true;
	}
}
