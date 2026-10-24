using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial;
using Geospatial.Vectors;
using Ras.Layers;
using Utility.Hashing;

namespace Ras.Observables;

public class ObservableFeatureLayerWrapped<GeometryLayer, Feature, ObservableFeature, Vector> : IVectorCollection<Vector>, IReadOnlyVectorCollection<Vector>, IEnumerable<Vector>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Vector>, IReadOnlyCollection<Vector> where GeometryLayer : FeatureLayerBase<Feature>, IDataHash where Feature : class, IFeatureRow, new() where ObservableFeature : ObservableObject, IObservableFeature<Feature> where Vector : class
{
	private readonly ObservableFeatureLayer<GeometryLayer, Feature, ObservableFeature> _lyr;

	protected readonly Func<Feature, Vector> _ftToVec;

	protected readonly Func<Vector, Feature> _vecToFt;

	private readonly ConditionalWeakTable<Vector, Feature> _featureCache = new ConditionalWeakTable<Vector, Feature>();

	private Freezer _freezer;

	public Vector this[int index]
	{
		get
		{
			Feature layer = _lyr.Layer[index];
			Vector ftToVec = _ftToVec(layer);
			if (ftToVec != null)
			{
				_featureCache.AddOrUpdate(ftToVec, layer);
			}
			return ftToVec;
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	public bool IsReadOnly => false;

	public ReaderWriterLock Lock => _lyr.Layer.Lock;

	public int Count => _lyr.Count;

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _lyr.Layer.VersionXY;
		}
	}

	public Extent Extent => _lyr.Layer.Extent;

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public ObservableFeatureLayerWrapped(ObservableFeatureLayer<GeometryLayer, Feature, ObservableFeature> lyr, Func<Feature, Vector> featureToVector, Func<Vector, Feature> vectorToFeature)
	{
		_lyr = lyr;
		_ftToVec = featureToVector;
		_vecToFt = vectorToFeature;
	}

	public void Add(Vector vector)
	{
		if (vector != null && _featureCache.TryGetValue(vector, out var localValue))
		{
			_lyr.AddFeature(localValue);
			return;
		}
		Feature feature = _vecToFt(vector);
		_lyr.AddFeature(feature);
	}

	public void Insert(int index, Vector vector)
	{
		if (vector != null && _featureCache.TryGetValue(vector, out var localValue))
		{
			_lyr.InsertFeature(index, localValue);
			return;
		}
		Feature feature = _vecToFt(vector);
		_lyr.InsertFeature(index, feature);
	}

	public void RemoveAt(int index)
	{
		_lyr.DeleteFeatureAt(index);
	}

	public void Clear()
	{
		_lyr.Clear();
	}

	public IEnumerator<Vector> GetEnumerator()
	{
		return _lyr.Layer.Select((Feature f) => _ftToVec(f)).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable<Vector>)this).GetEnumerator();
	}
}
