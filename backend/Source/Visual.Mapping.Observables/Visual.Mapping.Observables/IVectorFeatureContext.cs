using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public interface IVectorFeatureContext
{
	SelectedSet SelectedFeatureIDs { get; }

	EditingSession FeatureEditSession => (this as VisualContext)?.EditLayer;

	int FeatureCount { get; }

	bool IsFeatureDataLoaded => true;

	ObservableTable FeatureAttributeTable => null;

	IEnumerable<int> ValidSelectedFeatureIDs()
	{
		SelectedSet selectedFeatureIDs = SelectedFeatureIDs;
		if (selectedFeatureIDs == null)
		{
			return Enumerable.Empty<int>();
		}
		return selectedFeatureIDs.Where((int fid) => fid >= 0 && fid < FeatureCount);
	}

	bool TryRegisterSketchHandlers(ToolOrchestrator editLayer, SelectedSet selected)
	{
		return false;
	}

	IRenderLayer CreateDefaultRenderLayer(VectorSymbology symbology)
	{
		return null;
	}
}
