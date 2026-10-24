using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial;
using Ras.Layers;
using Utility;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Hashing;

namespace Ras.Observables;

public abstract class ObservableFeatureLayer<GeomLyr, GeomFt, ObsFt> : ObservableObject, INotifyList<ObsFt>, IReadOnlyNotifyCollection<ObsFt>, IReadOnlyList<ObsFt>, IEnumerable<ObsFt>, IEnumerable, IReadOnlyCollection<ObsFt>, INotifyCollectionChanged, IList<ObsFt>, ICollection<ObsFt>, INotifyList, IList, ICollection, IObservableFeatureLayer where GeomLyr : FeatureLayerBase<GeomFt>, IDataHash where GeomFt : IFeatureRow, new() where ObsFt : ObservableObject, IObservableFeature<GeomFt>
{
	public delegate void ExplicitCollectionChangeEvent(object sender, ExplicitCollectionChangedEventArgs<ObsFt> args);

	private readonly GeomLyr _layer;

	private readonly object _loadLock = new object();

	private AdvancedCollection<ObsFt> _features;

	private readonly Func<GeomFt, ObsFt> _obsFeatureMaker;

	public GeomLyr Layer => _layer;

	public int Count => _layer.PeekCount();

	public IReadOnlyAdvancedCollection<ObsFt> Features => LazyThreadsafe<AdvancedCollection<ObsFt>>.AllocateOnce(ref _features, _loadLock, this, (ObservableFeatureLayer<GeomLyr, GeomFt, ObsFt> model) =>
	{
		AdvancedCollection<ObsFt> advancedCollection = new AdvancedCollection<ObsFt>();
		for (int i = 0; i < model._layer.Count; i++)
		{
			advancedCollection.Add(model._obsFeatureMaker(model._layer[i]));
		}
		model.OnFeaturesListBuilt(advancedCollection);
		return advancedCollection;
	});

	public ObsFt this[int index] => Features[index];

	public bool IsReadOnly => false;

	public bool IsFixedSize => false;

	public bool IsSynchronized => false;

	public object SyncRoot => this;

	object IList.this[int index]
	{
		get
		{
			return this[index];
		}
		set
		{
			throw new NotSupportedException("Can not overwrite values.");
		}
	}

	ObsFt IList<ObsFt>.this[int index]
	{
		get
		{
			return _features[index];
		}
		set
		{
			throw new NotSupportedException("Can not overwrite values.");
		}
	}

	public event ExplicitCollectionChangeEvent ExplicitCollectionChanged;

	public event ExplicitCollectionChangeEvent ExplicitCollectionChanging;

	public event NotifyCollectionChangedEventHandler CollectionChanged;

	public ObservableFeatureLayer(GeomLyr lyr, Func<GeomFt, ObsFt> makeFeatureObservable)
	{
		_layer = lyr;
		_obsFeatureMaker = makeFeatureObservable;
	}

	protected virtual void OnFeaturesListBuilt(INotifyList ftList)
	{
		ftList.CollectionChanged += BaseListCollectionChanged;
	}

	private void BaseListCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		CollectionChanged?.Invoke(this, e);
	}

	private bool CheckLoad()
	{
		if (_features == null)
		{
			_ = Features.Count;
			return true;
		}
		return false;
	}

	public void AlertNewFeature(ObsFt feature)
	{
		if (!CheckLoad())
		{
			_features.Add(feature);
		}
	}

	public void AlertFeatureDeleted(ObsFt feature)
	{
		if (!CheckLoad())
		{
			_features.Remove(feature);
		}
	}

	public void AlertFeatureAtDeleted(int index)
	{
		if (!CheckLoad())
		{
			_features.RemoveAt(index);
		}
	}

	public int AddFeature(GeomFt feature)
	{
		CheckLoad();
		ObsFt obsFt = _obsFeatureMaker(feature);
		return AddFeature(obsFt);
	}

	public int AddFeature(ObsFt obsFt)
	{
		CheckLoad();
		List<ObsFt> newItems = new List<ObsFt> { obsFt };
		InvokeExplicitCollectionChanging(newItems, null);
		_features.Add(obsFt);
		Layer.Add(obsFt.BaseFeature);
		OnPropertyChanged("Count");
		InvokeExplicitCollectionChanged(newItems, null);
		return _features.Count - 1;
	}

	public void InsertFeature(int index, GeomFt feature)
	{
		ObsFt obsFt = _obsFeatureMaker(feature);
		InsertFeature(index, obsFt);
	}

	public void InsertFeature(int index, ObsFt obsFt)
	{
		CheckLoad();
		List<ObsFt> newItems = new List<ObsFt> { obsFt };
		InvokeExplicitCollectionChanging(newItems, null);
		_features.Insert(index, obsFt);
		Layer.Insert(index, obsFt.BaseFeature);
		OnPropertyChanged("Count");
		InvokeExplicitCollectionChanged(newItems, null);
	}

	public void DeleteFeatureAt(int featureIdx)
	{
		CheckLoad();
		List<ObsFt> oldItems = new List<ObsFt> { _features[featureIdx] };
		InvokeExplicitCollectionChanging(null, oldItems);
		_features.RemoveAt(featureIdx);
		Layer.RemoveAt(featureIdx);
		OnPropertyChanged("Count");
		InvokeExplicitCollectionChanged(null, oldItems);
	}

	public bool DeleteFeature(ObsFt ft)
	{
		CheckLoad();
		List<ObsFt> oldItems = new List<ObsFt> { ft };
		InvokeExplicitCollectionChanging(null, oldItems);
		bool result = _features.Remove(ft);
		Layer.RemoveAt(Layer.IndexOf(ft.BaseFeature));
		OnPropertyChanged("Count");
		InvokeExplicitCollectionChanged(null, oldItems);
		return result;
	}

	public void MoveFeatureAt(int fromIndex, int toIndex)
	{
		CheckLoad();
		_features.Move(fromIndex, toIndex);
		Layer.Move(fromIndex, toIndex);
	}

	public void Clear()
	{
		CheckLoad();
		List<ObsFt> oldItems = new List<ObsFt>(_features);
		InvokeExplicitCollectionChanging(null, oldItems);
		_features.Clear();
		Layer.Clear();
		OnPropertyChanged("Count");
		InvokeExplicitCollectionChanged(null, oldItems);
	}

	protected void InvokeExplicitCollectionChanged(List<ObsFt> newItems, List<ObsFt> oldItems)
	{
		ExplicitCollectionChanged?.Invoke(this, new ExplicitCollectionChangedEventArgs<ObsFt>(newItems, oldItems));
	}

	protected void InvokeExplicitCollectionChanging(List<ObsFt> newItems, List<ObsFt> oldItems)
	{
		ExplicitCollectionChanging?.Invoke(this, new ExplicitCollectionChangedEventArgs<ObsFt>(newItems, oldItems));
	}

	public IEnumerator<ObsFt> GetEnumerator()
	{
		return Features.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return Features.GetEnumerator();
	}

	void IList<ObsFt>.Insert(int index, ObsFt obsFt)
	{
		InsertFeature(index, obsFt);
	}

	void IList<ObsFt>.RemoveAt(int index)
	{
		DeleteFeatureAt(index);
	}

	void ICollection<ObsFt>.Add(ObsFt obsFt)
	{
		AddFeature(obsFt);
	}

	bool ICollection<ObsFt>.Remove(ObsFt obsFt)
	{
		return DeleteFeature(obsFt);
	}

	public int IndexOf(ObsFt obsFt)
	{
		return _features.IndexOf(obsFt);
	}

	public bool Contains(ObsFt obsFt)
	{
		return Features.Contains(obsFt);
	}

	public void CopyTo(ObsFt[] features, int arrayIndex)
	{
		_features.CopyTo(features, arrayIndex);
	}

	public bool AddAll(IEnumerable<ObsFt> data, bool fullRefresh = false)
	{
		throw new NotImplementedException();
	}

	public bool RemoveAll(IEnumerable<ObsFt> data, bool fullRefresh = false)
	{
		throw new NotImplementedException();
	}

	public int Add(object localValue)
	{
		return AddFeature(localValue as ObsFt);
	}

	public bool Contains(object localValue)
	{
		return Contains(localValue as ObsFt);
	}

	public int IndexOf(object localValue)
	{
		return IndexOf(localValue as ObsFt);
	}

	public void Insert(int index, object localValue)
	{
		InsertFeature(index, localValue as ObsFt);
	}

	public void Remove(object localValue)
	{
		DeleteFeature(localValue as ObsFt);
	}

	public void RemoveAt(int index)
	{
		DeleteFeatureAt(index);
	}

	public void CopyTo(Array values, int index)
	{
		throw new NotImplementedException();
	}
}
