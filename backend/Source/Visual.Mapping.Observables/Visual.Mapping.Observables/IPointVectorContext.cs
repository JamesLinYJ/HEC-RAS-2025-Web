using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Visual.Mapping.Observables;

public interface IPointVectorContext : IVectorContext<Point>, IVectorFeatureContext
{
	IPointCollection Points { get; }

	IVectorCollection<Point> IVectorContext<Point>.Features => Points;

	int IVectorFeatureContext.FeatureCount => Points?.Count ?? 0;

	bool IVectorFeatureContext.TryRegisterSketchHandlers(ToolOrchestrator editLayer, SelectedSet selected)
	{
		if (Points == null)
		{
			return false;
		}
		editLayer.RegisterSketchPointHandlers(Points, selected);
		return true;
	}
}
