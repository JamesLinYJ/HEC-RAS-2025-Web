using System;
using System.Collections.Generic;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class PathSelectionHandler : IEditHandler
{
	private readonly Action<Polyline, SelectMode> _select;

	private readonly Polyline _line = new Polyline(100);

	private readonly Polyline _drawLine = new Polyline(100);

	private readonly List<DateTime> _linePointTimes = new List<DateTime>(100);

	private VectorID _previewBackdropId;

	private VectorID _previewStrokeId;

	private bool _isActive;

	private static readonly TimeSpan TrailDuration = TimeSpan.FromMilliseconds(250L);

	public PathSelectionHandler(Action<Polyline, SelectMode> select)
	{
		_select = select ?? throw new ArgumentNullException("select");
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (type != CommandType.BeginDragging && type != CommandType.Dragging && type != CommandType.FinishDragging)
		{
			return type == CommandType.CancelDragging;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		return cmd.Command switch
		{
			CommandType.BeginDragging => HandleBegin(cmd), 
			CommandType.Dragging => HandleDrag(cmd, ctx), 
			CommandType.FinishDragging => HandleFinish(cmd, ctx), 
			CommandType.CancelDragging => HandleCancel(ctx), 
			CommandType.Close => HandleCancel(ctx), 
			_ => HandlerResult.NotHandled, 
		};
	}

	public bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext ctx)
	{
		TrimTrail();
		if (!_isActive)
		{
			ClearPreview(ctx);
			return false;
		}
		RenderPreview(ctx);
		return true;
	}

	private HandlerResult HandleBegin(EditCommand cmd)
	{
		IEditModifiers modifiers = cmd.Modifiers;
		if (modifiers != null && modifiers.IsRightClick())
		{
			return HandlerResult.NotHandled;
		}
		if (cmd.Location == null)
		{
			return HandlerResult.NotHandled;
		}
		IEditModifiers modifiers2 = cmd.Modifiers;
		if (modifiers2 == null || !modifiers2.IsCtrlDown())
		{
			_select(null, SelectMode.Remove);
		}
		ClearInternal();
		AddPoint(cmd.Location.QueryPoint);
		_isActive = true;
		return new HandlerResult
		{
			Handled = true,
			GetCapture = () => this
		};
	}

	private HandlerResult HandleDrag(EditCommand cmd, EditContext ctx)
	{
		if (!_isActive || cmd.Location == null)
		{
			return HandlerResult.NotHandled;
		}
		AddPoint(cmd.Location.QueryPoint);
		TrimTrail();
		RenderPreview(ctx);
		AppendSelection();
		return HandlerResult.WasHandled;
	}

	private HandlerResult HandleFinish(EditCommand cmd, EditContext ctx)
	{
		if (!_isActive)
		{
			return HandlerResult.NotHandled;
		}
		if (_line.Count > 0)
		{
			IEditModifiers modifiers = cmd.Modifiers;
			SelectMode replace = ((modifiers == null || !modifiers.IsCtrlDown()) ? SelectMode.Replace : SelectMode.Toggle);
			Polyline line = new Polyline(_line);
			_select(line, replace);
		}
		ClearInternal();
		ClearPreview(ctx);
		_isActive = false;
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	private HandlerResult HandleCancel(EditContext ctx)
	{
		if (!_isActive)
		{
			return HandlerResult.NotHandled;
		}
		ClearInternal();
		ClearPreview(ctx);
		_isActive = false;
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	private void AddPoint(Point worldPoint)
	{
		_line.Add(worldPoint);
		_drawLine.Add(worldPoint);
		_linePointTimes.Add(DateTime.UtcNow);
	}

	private void TrimTrail()
	{
		if (_drawLine.Count != _linePointTimes.Count)
		{
			throw new InvalidOperationException("Trail point bookkeeping mismatch.");
		}
		DateTime dateTime = DateTime.UtcNow - TrailDuration;
		while (_linePointTimes.Count > 0 && _linePointTimes[0] < dateTime)
		{
			_drawLine.RemoveAt(0);
			_linePointTimes.RemoveAt(0);
		}
	}

	private void RenderPreview(EditContext ctx)
	{
		if (_drawLine.Count != 0)
		{
			ctx.VectorManager.AddOrUpdateVector(ref _previewBackdropId, _drawLine, DefaultEditSymbology.MarqueeBackdropSymbology);
			ctx.VectorManager.AddOrUpdateVector(ref _previewStrokeId, _drawLine, DefaultEditSymbology.MarqueeStrokeSymbology);
		}
	}

	private void AppendSelection()
	{
		if (_line.Count != 0)
		{
			Polyline line = new Polyline(_line);
			_select(line, SelectMode.Append);
		}
	}

	private void ClearInternal()
	{
		_line.Clear();
		_drawLine.Clear();
		_linePointTimes.Clear();
	}

	private void ClearPreview(EditContext ctx)
	{
		ctx.VectorManager.TryDeleteVector(ref _previewBackdropId);
		ctx.VectorManager.TryDeleteVector(ref _previewStrokeId);
	}
}
