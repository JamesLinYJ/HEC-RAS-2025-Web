using Geospatial.Helpers;
using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering.Editing;

public sealed class SelectArcHandler : IEditHandler
{
	private readonly NodeArcEditContext _context;

	public SelectArcHandler(NodeArcEditContext context)
	{
		_context = context;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (!NetworkRenderLayer.HitTestArc(_context.Network, cmd.Location, HitTestSortOrder.Distance, out var _, out var elementIndex, out var _))
		{
			if (!cmd.Modifiers.IsCtrlDown())
			{
				_context.ArcSelectedSet.Clear();
			}
			return HandlerResult.NotHandled;
		}
		if (!cmd.Modifiers.IsCtrlDown())
		{
			_context.ClearSelection();
			_context.ArcSelectedSet.Add(elementIndex);
			return HandlerResult.WasHandled;
		}
		if (_context.ArcSelectedSet.Contains(elementIndex))
		{
			_context.ArcSelectedSet.Remove(elementIndex);
		}
		else
		{
			_context.ArcSelectedSet.Add(elementIndex);
		}
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		return _context.EditableNetwork.TryPreviewSelectArc(hit);
	}
}
