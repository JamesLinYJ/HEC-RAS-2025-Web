using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Utility;
using Utility.DataStructures;

namespace Visual.Mapping.Observables;

public sealed class ObservableFeatureSet<TFeature> : IObservableFeatureSet<TFeature>, IReadOnlyNotifyCollection<TFeature>, IReadOnlyList<TFeature>, IEnumerable<TFeature>, IEnumerable, IReadOnlyCollection<TFeature>, INotifyCollectionChanged where TFeature : ObservableTopologyFeature
{
	private readonly object _loadLock = new object();

	private readonly Func<int> _modelCount;

	private readonly Func<int, TFeature> _build;

	private AdvancedCollection<TFeature> _features;

	private AdvancedCollection<TFeature> Loaded => LazyThreadsafe<AdvancedCollection<TFeature>>.AllocateOnce(ref _features, _loadLock, this, Build);

	public int Count => Loaded.Count;

	public TFeature this[int index] => Loaded[index];

	public Type FeatureType => typeof(TFeature);

	public bool IsLoaded => _features != null;

	public event NotifyCollectionChangedEventHandler CollectionChanged
	{
		add
		{
			Loaded.CollectionChanged += value;
		}
		remove
		{
			Loaded.CollectionChanged -= value;
		}
	}

	public IEnumerator<TFeature> GetEnumerator()
	{
		return Loaded.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public ObservableFeatureSet(Func<int> modelCount, Func<int, TFeature> build)
	{
		_modelCount = modelCount;
		_build = build;
	}

	private static AdvancedCollection<TFeature> Build(ObservableFeatureSet<TFeature> set)
	{
		AdvancedCollection<TFeature> advancedCollection = new AdvancedCollection<TFeature>();
		int modelCount = set._modelCount();
		for (int i = 0; i < modelCount; i++)
		{
			advancedCollection.Add(set._build(i));
		}
		return advancedCollection;
	}

	private bool EnsureLoaded()
	{
		if (_features != null)
		{
			return false;
		}
		_ = Loaded;
		return true;
	}

	public int IndexOf(ObservableTopologyFeature feature)
	{
		if (!(feature is TFeature tFeature))
		{
			return -1;
		}
		return Loaded.IndexOf(tFeature);
	}

	public void AlertNew(int fid)
	{
		if (!EnsureLoaded())
		{
			_features.Add(_build(fid));
		}
	}

	public void AlertInserted(int index)
	{
		if (!EnsureLoaded())
		{
			TFeature build = _build(index);
			ShiftFidsFrom(index, atOrAfter: true, 1);
			_features.Insert(index, build);
		}
	}

	public void AlertDeleted(TFeature feature)
	{
		if (!EnsureLoaded())
		{
			feature?.DisposeTags();
			_features.Remove(feature);
			ShiftFidsFrom(feature.Fid, atOrAfter: false, -1);
		}
	}

	public void AlertAtDeleted(int index)
	{
		if (!EnsureLoaded())
		{
			_features[index]?.DisposeTags();
			_features.RemoveAt(index);
			ShiftFidsFrom(index, atOrAfter: false, -1);
		}
	}

	public void AlertRangeAppended(int appendedCount)
	{
		if (!EnsureLoaded())
		{
			int count = _features.Count;
			List<TFeature> tFeatures = new List<TFeature>();
			for (int i = 0; i < appendedCount; i++)
			{
				tFeatures.Add(_build(count + i));
			}
			_features.AddAll(tFeatures);
		}
	}

	public void AlertRangeInserted(IEnumerable<int> indices)
	{
		if (EnsureLoaded())
		{
			return;
		}
		foreach (int order in indices.Order())
		{
			AlertInserted(order);
		}
	}

	public void AlertRangeAtDeleted(IEnumerable<int> indices)
	{
		if (EnsureLoaded())
		{
			return;
		}
		foreach (int index in indices)
		{
			if (index >= 0 && index < _features.Count)
			{
				_features[index]?.DisposeTags();
			}
		}
		_features.RemoveAllAt(indices);
		foreach (int orderDescending in indices.OrderDescending())
		{
			ShiftFidsFrom(orderDescending, atOrAfter: false, -1);
		}
	}

	public void HandleCellValueChanged(int rowIdx, int colIdx, object localValue)
	{
		if (_features != null && rowIdx >= 0 && rowIdx < _features.Count)
		{
			_features[rowIdx]?.HandleCellValueChanged(colIdx, localValue);
		}
	}

	public void RebuildInPlace()
	{
		if (_features != null)
		{
			_features.SetAll(Build(this), fullRefresh: true);
		}
	}

	public void DisposeAllTags()
	{
		if (_features == null)
		{
			return;
		}
		foreach (TFeature feature in _features)
		{
			feature?.DisposeTags();
		}
	}

	private void ShiftFidsFrom(int fid, bool atOrAfter, int delta)
	{
		foreach (TFeature feature in _features)
		{
			if (atOrAfter ? (feature.Fid >= fid) : (feature.Fid > fid))
			{
				feature.UpdateFid(feature.Fid + delta);
			}
		}
	}
}
