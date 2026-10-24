using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Geospatial;
using Geospatial.Features;
using Geospatial.Vectors;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class ObservableFeatureCollection<T> : IVectorCollection<T>, IReadOnlyVectorCollection<T>, IEnumerable<T>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<T>, IReadOnlyCollection<T>
{
	private readonly FeatureCollection<T> _featureCollection;

	public FeatureCollection<T> FeatureCollection => _featureCollection;

	public ObservableTable ObservableTable { get; }

	private IVectorCollection<T> Features => _featureCollection.Features;

	public T this[int index]
	{
		get
		{
			return Features[index];
		}
		set
		{
			Features[index] = value;
		}
	}

	public int Count => Features.Count;

	public bool IsReadOnly => false;

	public long VersionXY => Features.VersionXY;

	public Extent Extent => Features.Extent;

	public ReaderWriterLock Lock => Features.Lock;

	public ObservableFeatureCollection(FeatureCollection<T> featureCollection, ObservableTable observableTable)
	{
		_featureCollection = featureCollection;
		ObservableTable = observableTable;
	}

	public void Add(T localValue)
	{
		Features.Add(localValue);
		ObservableTable.AppendRow();
	}

	public void Insert(int index, T localValue)
	{
		Features.Insert(index, localValue);
		ObservableTable.InsertRow(index);
	}

	public void RemoveAt(int index)
	{
		Features.RemoveAt(index);
		ObservableTable.RemoveRow(index);
	}

	public void Clear()
	{
		Features.Clear();
		ObservableTable.ClearRows();
	}

	public void FreezeOne()
	{
		Features.FreezeOne();
	}

	public void ThawOne()
	{
		Features.ThawOne();
	}

	public IEnumerator<T> GetEnumerator()
	{
		return Features.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return Features.GetEnumerator();
	}
}
