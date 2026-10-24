using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class SelectPointFeatureHandler : IEditHandler
{
	private readonly Func<HitTestArgs, int> _hitTest;

	private readonly SelectedSet _selected;

	private readonly Func<int, Point> _getPointForFid;

	public SelectPointFeatureHandler(Func<HitTestArgs, int> hitTest, SelectedSet selected, Func<int, Point> getPointForFid)
	{
		_hitTest = hitTest;
		_selected = selected;
		_getPointForFid = getPointForFid;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		int hitTest = _hitTest(cmd.Location);
		if (hitTest >= 0)
		{
			if (cmd.Modifiers.IsCtrlDown())
			{
				if (_selected.Contains(hitTest))
				{
					_selected.Remove(hitTest);
				}
				else
				{
					_selected.Add(hitTest);
				}
			}
			else
			{
				_selected.Clear();
				_selected.Add(hitTest);
			}
			return HandlerResult.WasHandled;
		}
		if (!cmd.Modifiers.IsCtrlDown())
		{
			_selected.Clear();
			return HandlerResult.NotHandled;
		}
		return HandlerResult.NotHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		int hitTest = _hitTest(hit);
		if (hitTest >= 0)
		{
			string channel = "selectpoint_highlight";
			ctx.Preview.TryGetVector(channel, out var id);
			ctx.VectorManager.AddOrUpdateVector(ref id, _getPointForFid(hitTest), DefaultEditSymbology.SelectedPointSymbology);
			ctx.Preview.RegisterVector(channel, id);
			return true;
		}
		return false;
	}
}
