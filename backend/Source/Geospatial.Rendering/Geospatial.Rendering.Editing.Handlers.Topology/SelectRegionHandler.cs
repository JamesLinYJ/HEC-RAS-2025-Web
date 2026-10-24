using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class SelectRegionHandler : IEditHandler
{
	private readonly TopologyEditContext _context;

	public SelectRegionHandler(TopologyEditContext editLayer)
	{
		_context = editLayer;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (!NetworkRenderLayer.HitTest(_context.Topology, cmd.Location, out var element, out var elementIndex, out var _))
		{
			if (!cmd.Modifiers.IsCtrlDown())
			{
				_context.ControlPointSelectedSet.Clear();
			}
			return HandlerResult.NotHandled;
		}
		if (element != NetworkElementType.Region)
		{
			return HandlerResult.NotHandled;
		}
		Region region = _context.Topology.CompletedRegions[elementIndex];
		if (region.ControlPointIndex < 0)
		{
			return HandlerResult.NotHandled;
		}
		if (cmd.Modifiers.IsRightClick())
		{
			if (!_context.ControlPointSelectedSet.Contains(region.ControlPointIndex))
			{
				_context.ClearSelection();
				_context.ControlPointSelectedSet.Add(region.ControlPointIndex);
			}
			return HandlerResult.WasHandled;
		}
		if (!cmd.Modifiers.IsCtrlDown())
		{
			_context.ClearSelection();
			_context.ControlPointSelectedSet.Add(region.ControlPointIndex);
			return HandlerResult.WasHandled;
		}
		if (_context.ControlPointSelectedSet.Contains(region.ControlPointIndex))
		{
			_context.ControlPointSelectedSet.Remove(region.ControlPointIndex);
		}
		else
		{
			_context.ControlPointSelectedSet.Add(region.ControlPointIndex);
		}
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		return _context.EditableTopology.TryPreviewSelectRegion(hit);
	}
}
