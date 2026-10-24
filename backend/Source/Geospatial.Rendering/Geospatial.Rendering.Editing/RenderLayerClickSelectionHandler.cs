using System;
using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering.Editing;

public sealed class RenderLayerClickSelectionHandler : IEditHandler
{
	private readonly Func<IVectorRenderLayer> _getActiveLayer;

	private readonly bool _handleDoubleClick;

	public RenderLayerClickSelectionHandler(Func<IVectorRenderLayer> getActiveLayer, bool handleDoubleClick = true)
	{
		_getActiveLayer = getActiveLayer ?? throw new ArgumentNullException("getActiveLayer");
		_handleDoubleClick = handleDoubleClick;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (type != CommandType.Click)
		{
			if (_handleDoubleClick)
			{
				return type == CommandType.DoubleClick;
			}
			return false;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		IVectorRenderLayer selectedLayer = GetSelectedLayer();
		if (selectedLayer == null || cmd.Location == null)
		{
			return HandlerResult.NotHandled;
		}
		switch (cmd.Command)
		{
		case CommandType.Click:
			selectedLayer.Select(cmd.Location, ResolveClickMode(cmd.Modifiers));
			return HandlerResult.WasHandled;
		case CommandType.DoubleClick:
		{
			if (!_handleDoubleClick)
			{
				return HandlerResult.NotHandled;
			}
			IEditModifiers modifiers = cmd.Modifiers;
			if (modifiers != null && modifiers.IsRightClick())
			{
				return HandlerResult.NotHandled;
			}
			selectedLayer.Select(cmd.Location, SelectMode.Toggle);
			return HandlerResult.WasHandled;
		}
		default:
			return HandlerResult.NotHandled;
		}
	}

	public bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext ctx)
	{
		return false;
	}

	private IVectorRenderLayer GetSelectedLayer()
	{
		return _getActiveLayer();
	}

	private static SelectMode ResolveClickMode(IEditModifiers modifiers)
	{
		if (modifiers != null && modifiers.IsRightClick())
		{
			return SelectMode.EnsureIncluded;
		}
		if (modifiers != null && modifiers.IsCtrlDown())
		{
			return SelectMode.Toggle;
		}
		return SelectMode.Replace;
	}
}
