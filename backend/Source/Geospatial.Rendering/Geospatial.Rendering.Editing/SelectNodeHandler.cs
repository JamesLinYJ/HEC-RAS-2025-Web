using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering.Editing;

public sealed class SelectNodeHandler : IEditHandler
{
	private readonly NodeArcEditContext _context;

	public SelectNodeHandler(NodeArcEditContext context)
	{
		_context = context;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (!NetworkRenderLayer.HitTestNode(_context.Network, cmd.Location, out var elementIndex))
		{
			if (!cmd.Modifiers.IsCtrlDown())
			{
				_context.NodeSelectedSet.Clear();
			}
			return HandlerResult.NotHandled;
		}
		if (!cmd.Modifiers.IsCtrlDown())
		{
			_context.ClearSelection();
			_context.NodeSelectedSet.Add(elementIndex);
			return HandlerResult.WasHandled;
		}
		if (_context.NodeSelectedSet.Contains(elementIndex))
		{
			_context.NodeSelectedSet.Remove(elementIndex);
		}
		else
		{
			_context.NodeSelectedSet.Add(elementIndex);
		}
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		return _context.EditableNetwork.TryPreviewSelectNode(hit);
	}
}
