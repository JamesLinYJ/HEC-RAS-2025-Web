using Utility.DataStructures;

namespace Visual.Mapping.Observables.Selection;

public interface IFeatureSelectionView<out T> : IFeatureSelection where T : class
{
	IReadOnlyNotifyCollection<T> SelectedFeatures { get; }

	INotifyList SelectedItems { get; }

	int Count { get; }

	T LastSelectedFeature { get; }
}
