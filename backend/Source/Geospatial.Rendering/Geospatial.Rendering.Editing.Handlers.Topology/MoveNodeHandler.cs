using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class MoveNodeHandler : MovePointsHandler
{
	private NodeArcEditContext _context;

	public MoveNodeHandler(NodeArcEditContext context)
		: base(context.NodeSelectedSet)
	{
		_context = context;
	}

	public override bool HitTestPoint(HitTestArgs hit, out int pointIndex)
	{
		return NetworkRenderLayer.HitTestNode(_context.Network, hit, out pointIndex);
	}

	public override HandlerResult MoveSelectedPoints(EditContext ctx, HitTestArgs hit, Vector delta)
	{
		if (NodeArcEditGeometry.CanMoveNodes(_context.Network, delta, _context.NodeSelectedSet, hit.Window.CellSize))
		{
			ctx.ExecuteEditOperation(_context.CreateTranslateSelectedNodesOperation(hit, delta));
		}
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	public override bool TryPreviewMoveSelectedPoints(HitTestArgs hit, Vector delta)
	{
		if (_selected.IsEmpty)
		{
			return false;
		}
		return _context.EditableNetwork.TryPreviewMoveSelectedNodes(hit, delta, _selected);
	}

	public override bool TryPreviewToggleSelectedPoints(HitTestArgs hit)
	{
		return _context.EditableNetwork.TryPreviewSelectNode(hit);
	}
}
