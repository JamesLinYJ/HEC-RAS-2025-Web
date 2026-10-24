using Geospatial.Rendering.Systems.Vector;

namespace Geospatial.Rendering.Editing;

public abstract class RenderLayerPreviewHandlerBase : IEditHandler
{
	private readonly string _pointsName;

	private readonly string _segmentsName;

	private readonly string _polylinesName;

	private readonly string _polygonsName;

	private readonly string _complexPolygonsName;

	protected RenderLayerPreviewHandlerBase(string namePrefix)
	{
		_pointsName = namePrefix + "_Points";
		_segmentsName = namePrefix + "_Segments";
		_polylinesName = namePrefix + "_Polylines";
		_polygonsName = namePrefix + "_Polygons";
		_complexPolygonsName = namePrefix + "_ComplexPolygons";
	}

	public virtual bool CanHandle(CommandType type, EditContext context)
	{
		return false;
	}

	public virtual HandlerResult Handle(EditCommand command, EditContext context)
	{
		return HandlerResult.NotHandled;
	}

	public abstract bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext context);

	protected void GetVectorIds(EditContext ctx, out VectorID ptsId, out VectorID segsId, out VectorID polylinesId, out VectorID polygonsId, out VectorID complexPolygonsId)
	{
		ctx.Preview.TryGetVector(_pointsName, out ptsId);
		ctx.Preview.TryGetVector(_segmentsName, out segsId);
		ctx.Preview.TryGetVector(_polylinesName, out polylinesId);
		ctx.Preview.TryGetVector(_polygonsName, out polygonsId);
		ctx.Preview.TryGetVector(_complexPolygonsName, out complexPolygonsId);
	}

	protected void RegisterVectorIds(EditContext ctx, VectorID ptsId, VectorID segsId, VectorID polylinesId, VectorID polygonsId, VectorID complexPolygonsId)
	{
		ctx.Preview.TryRegisterVector(_pointsName, ptsId);
		ctx.Preview.TryRegisterVector(_segmentsName, segsId);
		ctx.Preview.TryRegisterVector(_polylinesName, polylinesId);
		ctx.Preview.TryRegisterVector(_polygonsName, polygonsId);
		ctx.Preview.TryRegisterVector(_complexPolygonsName, complexPolygonsId);
	}

	protected void TouchAllVectors(EditContext ctx)
	{
		ctx.Preview.Touch(_pointsName);
		ctx.Preview.Touch(_segmentsName);
		ctx.Preview.Touch(_polylinesName);
		ctx.Preview.Touch(_polygonsName);
		ctx.Preview.Touch(_complexPolygonsName);
	}
}
