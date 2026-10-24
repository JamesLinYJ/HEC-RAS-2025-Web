using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Utility.DataStructures;

namespace Visual.Mapping.Observables;

public interface IObservableFeatureSet<out TFeature> : IReadOnlyNotifyCollection<TFeature>, IReadOnlyList<TFeature>, IEnumerable<TFeature>, IEnumerable, IReadOnlyCollection<TFeature>, INotifyCollectionChanged where TFeature : ObservableTopologyFeature
{
	Type FeatureType { get; }

	bool IsLoaded { get; }

	int IndexOf(ObservableTopologyFeature feature);

	void AlertNew(int fid);

	void AlertInserted(int index);

	void AlertAtDeleted(int index);

	void AlertRangeAppended(int appendedCount);

	void AlertRangeInserted(IEnumerable<int> indices);

	void AlertRangeAtDeleted(IEnumerable<int> indices);

	void HandleCellValueChanged(int rowIdx, int colIdx, object localValue);

	void RebuildInPlace();

	void DisposeAllTags();
}
