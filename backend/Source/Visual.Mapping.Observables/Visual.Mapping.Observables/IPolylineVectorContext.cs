using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Visual.Mapping.Observables;

public interface IPolylineVectorContext : IVectorContext<Polyline>, IVectorFeatureContext
{
	IPolylineCollection Polylines { get; }

	IVectorCollection<Polyline> IVectorContext<Polyline>.Features => Polylines;

	int IVectorFeatureContext.FeatureCount => Polylines?.Count ?? 0;

	bool IVectorFeatureContext.TryRegisterSketchHandlers(ToolOrchestrator editLayer, SelectedSet selected)
	{
		if (Polylines == null)
		{
			return false;
		}
		editLayer.RegisterSketchPolylineHandlers(Polylines, selected);
		return true;
	}

	IRenderLayer IVectorFeatureContext.CreateDefaultRenderLayer(VectorSymbology symbology)
	{
		if (Polylines == null)
		{
			return null;
		}
		return new PolylineRenderLayer(Polylines)
		{
			VectorSymbology = symbology,
			VertexSymbology = symbology
		};
	}
}
