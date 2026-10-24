using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class MovePointFeatureHandler : IEditHandler
{
	private readonly Func<HitTestArgs, int> _hitTest;

	private readonly SelectedSet _selected;

	private readonly Func<int, Point> _getPointForFid;

	private readonly Func<EditContext, EditablePointCollection> _editableFactory;

	private EditablePointCollection _editable;

	private Point _interactionStartPt;

	private bool _isDragging;

	public MovePointFeatureHandler(Func<HitTestArgs, int> hitTest, SelectedSet selected, Func<int, Point> getPointForFid, Func<EditContext, EditablePointCollection> editableFactory)
	{
		_hitTest = hitTest;
		_selected = selected;
		_getPointForFid = getPointForFid;
		_editableFactory = editableFactory;
	}

	private EditablePointCollection GetEditable(EditContext ctx)
	{
		return _editable ?? (_editable = _editableFactory(ctx));
	}

	internal void ResetEditable()
	{
		_editable = null;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.BeginDragging;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (cmd.Location == null)
		{
			return HandlerResult.NotHandled;
		}
		if (cmd.Command == CommandType.BeginDragging)
		{
			GetEditable(ctx);
			int hitTest = _hitTest(cmd.Location);
			if (hitTest < 0)
			{
				return HandlerResult.NotHandled;
			}
			_interactionStartPt = cmd.Location.QueryPoint;
			if (!_selected.Contains(hitTest))
			{
				_selected.Clear();
				_selected.Add(hitTest);
			}
			_isDragging = true;
			return new HandlerResult
			{
				Handled = true,
				GetCapture = () => this
			};
		}
		if (cmd.Command == CommandType.FinishDragging)
		{
			EditablePointCollection editable = GetEditable(ctx);
			_isDragging = false;
			editable.TryMoveSelectedPoints(deltaOffset: cmd.Location.QueryPoint - _interactionStartPt, location: cmd.Location);
			editable.ClearAllVectors();
			ResetEditable();
			return new HandlerResult
			{
				Handled = true,
				ReleaseCapture = true
			};
		}
		return HandlerResult.NotHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		if (_isDragging)
		{
			GetEditable(ctx).PreviewMoveSelectedPoints(hit, _interactionStartPt);
			return true;
		}
		int hitTest = _hitTest(hit);
		string channel = "movepoint_preview";
		if (hitTest >= 0)
		{
			ctx.Preview.TryGetVector(channel, out var id);
			ctx.VectorManager.AddOrUpdateVector(ref id, _getPointForFid(hitTest), DefaultEditSymbology.SelectPointSymbology);
			ctx.Preview.RegisterVector(channel, id);
			return true;
		}
		return false;
	}
}
