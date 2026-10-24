using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Visual.Mapping.Observables;

public interface IPolygonVectorContext : IVectorContext<Polygon>, IVectorFeatureContext
{
	IPolygonCollection Polygons { get; }

	IVectorCollection<Polygon> IVectorContext<Polygon>.Features => Polygons;

	int IVectorFeatureContext.FeatureCount => Polygons?.Count ?? 0;

	bool IVectorFeatureContext.TryRegisterSketchHandlers(ToolOrchestrator editLayer, SelectedSet selected)
	{
		if (Polygons == null)
		{
			return false;
		}
		editLayer.RegisterSketchPolygonHandlers(Polygons, selected);
		return true;
	}

	IRenderLayer IVectorFeatureContext.CreateDefaultRenderLayer(VectorSymbology symbology)
	{
		if (Polygons == null)
		{
			return null;
		}
		return new PolygonRenderLayer(Polygons)
		{
			VectorSymbology = symbology,
			VertexSymbology = symbology
		};
	}
}
