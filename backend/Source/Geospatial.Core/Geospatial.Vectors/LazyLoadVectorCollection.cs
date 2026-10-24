using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Utility;

namespace Geospatial.Vectors;

public abstract class LazyLoadVectorCollection<T> : IVectorCollection<T>, IReadOnlyVectorCollection<T>, IEnumerable<T>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<T>, IReadOnlyCollection<T>
{
	protected readonly Func<IVectorCollection<T>> _loadElements;

	private object _baseCollectionLock = new object();

	private IVectorCollection<T> _baseCollection;

	protected virtual IVectorCollection<T> BaseCollection => LazyThreadsafe<IVectorCollection<T>>.AllocateOnce(ref _baseCollection, _baseCollectionLock, _loadElements);

	public bool IsReadOnly => BaseCollection.IsReadOnly;

	public ReaderWriterLock Lock => BaseCollection.Lock;

	public int Count => BaseCollection.Count;

	public long VersionXY => BaseCollection.VersionXY;

	public Extent Extent => BaseCollection.Extent;

	public T this[int index]
	{
		get
		{
			return BaseCollection[index];
		}
		set
		{
			BaseCollection[index] = value;
		}
	}

	public LazyLoadVectorCollection(Func<IVectorCollection<T>> loadElements)
	{
		_loadElements = loadElements;
	}

	public void Add(T localValue)
	{
		BaseCollection.Add(localValue);
	}

	public void Insert(int index, T localValue)
	{
		BaseCollection.Insert(index, localValue);
	}

	public void RemoveAt(int index)
	{
		BaseCollection.RemoveAt(index);
	}

	public void Clear()
	{
		BaseCollection.Clear();
	}

	public IEnumerator<T> GetEnumerator()
	{
		return BaseCollection.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return BaseCollection.GetEnumerator();
	}

	public void FreezeOne()
	{
		BaseCollection.FreezeOne();
	}

	public void ThawOne()
	{
		BaseCollection.ThawOne();
	}
}
