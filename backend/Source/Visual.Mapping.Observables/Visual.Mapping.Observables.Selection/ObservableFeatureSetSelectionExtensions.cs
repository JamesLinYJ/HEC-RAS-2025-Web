using Geospatial.Rendering.Layers;
using Utility;

namespace Visual.Mapping.Observables.Selection;

public static class ObservableFeatureSetSelectionExtensions
{
	public static LinkedFeatureSelection<TFeature> ToLinkedSelection<TFeature>(this IObservableFeatureSet<TFeature> features, SelectedSet selectedSet, ISynchronizationContext uiThreadSyncContext) where TFeature : ObservableTopologyFeature
	{
		return new LinkedFeatureSelection<TFeature>(selectedSet, features.IndexOf, (int index) => features[index], uiThreadSyncContext);
	}
}
