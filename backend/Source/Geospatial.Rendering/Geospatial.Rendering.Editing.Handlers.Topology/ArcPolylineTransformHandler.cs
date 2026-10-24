using Geospatial.Helpers;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public abstract class ArcPolylineTransformHandler : IEditHandler
{
	private readonly TopologyEditContext _context;

	private readonly string _previewChannel;

	private readonly string _operationDescription;

	private VectorID _previewId;

	protected TopologyEditContext Context => _context;

	protected ArcPolylineTransformHandler(TopologyEditContext context, string previewChannel, string operationDescription)
	{
		_context = context;
		_previewChannel = previewChannel;
		_operationDescription = operationDescription;
	}

	protected abstract Polyline TransformArcPolyline(Polyline arcLine, EditContext ctx, RasterDefinition window);

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
		if (!TryBuildTransformed(elementIndex, ctx, cmd.Location.Window, out var transformed))
		{
			return HandlerResult.NotHandled;
		}
		PointCollection newInternalPoints = StripBookends(transformed);
		ReplaceArcInternalPointsOperation op = new ReplaceArcInternalPointsOperation(_context, elementIndex, newInternalPoints, _operationDescription);
		ctx.ExecuteEditOperation(op);
		return HandlerResult.WasHandled;
	}

	public virtual bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		if (!NetworkRenderLayer.HitTestArc(_context.Topology, hit, HitTestSortOrder.Distance, out var _, out var elementIndex, out var _))
		{
			return false;
		}
		if (!TryBuildTransformed(elementIndex, ctx, hit.Window, out var transformed))
		{
			return false;
		}
		ctx.Preview.TryGetVector(_previewChannel, out _previewId);
		ctx.VectorManager.AddOrUpdateVector(ref _previewId, transformed, DefaultEditSymbology.AppendSegmentSymbology);
		ctx.Preview.RegisterVector(_previewChannel, _previewId);
		return true;
	}

	private bool TryBuildTransformed(int arcFid, EditContext ctx, RasterDefinition window, out Polyline transformed)
	{
		Arc arc = _context.Topology.Arcs[arcFid];
		if (arc == null || arc.InternalPointCount == 0)
		{
			transformed = null;
			return false;
		}
		Polyline arcPolyline = _context.Topology.GetArcPolyline(arcFid);
		transformed = TransformArcPolyline(arcPolyline, ctx, window);
		return transformed != null;
	}

	private static PointCollection StripBookends(Polyline full)
	{
		PointCollection pointCollection = new PointCollection(full.Count - 2);
		for (int i = 1; i < full.Count - 1; i++)
		{
			pointCollection.Add(full[i]);
		}
		return pointCollection;
	}
}
