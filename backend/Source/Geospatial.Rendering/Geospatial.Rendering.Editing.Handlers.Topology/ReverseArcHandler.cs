using Geospatial.Helpers;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class ReverseArcHandler : IEditHandler
{
	private const string PreviewChannel = "arc_reverse_preview";

	private const string ArrowsChannel = "arc_reverse_preview_arrows";

	private readonly TopologyEditContext _context;

	private readonly ReverseDirectionPreviewCache _previewCache = new ReverseDirectionPreviewCache();

	private VectorID _previewId;

	private VectorID _arrowsId;

	public ReverseArcHandler(TopologyEditContext context)
	{
		_context = context;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		IEditModifiers modifiers = cmd.Modifiers;
		if (modifiers != null && modifiers.IsRightClick())
		{
			return HandlerResult.NotHandled;
		}
		if (!NetworkRenderLayer.HitTestArc(_context.Topology, cmd.Location, HitTestSortOrder.Distance, out var _, out var elementIndex, out var _))
		{
			return HandlerResult.NotHandled;
		}
		ReverseArcDirectionOperation op = new ReverseArcDirectionOperation(_context, elementIndex);
		ctx.ExecuteEditOperation(op);
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		if (!NetworkRenderLayer.HitTestArc(_context.Topology, hit, HitTestSortOrder.Distance, out var _, out var elementIndex, out var _))
		{
			return false;
		}
		Polyline arcPolyline = _context.Topology.GetArcPolyline(elementIndex);
		_previewCache.GetOrCompute(arcPolyline, hit.Window, out var reversed, out var chevrons);
		ctx.Preview.TryGetVector("arc_reverse_preview", out _previewId);
		ctx.VectorManager.AddOrUpdateVector(ref _previewId, reversed, DefaultEditSymbology.OpenSymbology);
		ctx.Preview.RegisterVector("arc_reverse_preview", _previewId);
		ctx.Preview.TryGetVector("arc_reverse_preview_arrows", out _arrowsId);
		ctx.VectorManager.AddOrUpdateVector(ref _arrowsId, chevrons, DefaultEditSymbology.SelectLineSymbology);
		ctx.Preview.RegisterVector("arc_reverse_preview_arrows", _arrowsId);
		return true;
	}
}
