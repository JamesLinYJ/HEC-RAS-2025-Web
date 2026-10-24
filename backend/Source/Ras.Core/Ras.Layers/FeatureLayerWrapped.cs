using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Geospatial;
using Geospatial.Vectors;

namespace Ras.Layers;

internal class FeatureLayerWrapped<F, V> : IVectorCollection<V>, IReadOnlyVectorCollection<V>, IEnumerable<V>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<V>, IReadOnlyCollection<V> where F : IFeatureRow, new()
{
	private readonly FeatureLayerBase<F> _lyr;

	protected readonly Func<F, V> _ftToVec;

	protected readonly Func<V, F> _vecToFt;

	private Freezer _freezer;

	public V this[int index]
	{
		get
		{
			return _ftToVec(_lyr[index]);
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	public bool IsReadOnly => _lyr.IsReadOnly;

	public ReaderWriterLock Lock => _lyr.Lock;

	public int Count => _lyr.Count;

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _lyr.VersionXY;
		}
	}

	public Extent Extent => _lyr.Extent;

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public FeatureLayerWrapped(FeatureLayerBase<F> lyr, Func<F, V> featureToVector, Func<V, F> vectorToFeature)
	{
		_lyr = lyr;
		_ftToVec = featureToVector;
		_vecToFt = vectorToFeature;
	}

	public void Add(V localValue)
	{
		F vecToFt = _vecToFt(localValue);
		_lyr.Add(vecToFt);
	}

	public void Insert(int index, V localValue)
	{
		F vecToFt = _vecToFt(localValue);
		_lyr.Insert(index, vecToFt);
	}

	public void RemoveAt(int index)
	{
		_lyr.RemoveAt(index);
	}

	public void Clear()
	{
		_lyr.Clear();
	}

	public IEnumerator<V> GetEnumerator()
	{
		return _lyr.Select((F f) => _ftToVec(f)).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable<V>)this).GetEnumerator();
	}
}
