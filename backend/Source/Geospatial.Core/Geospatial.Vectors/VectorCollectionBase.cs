using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Utility;

namespace Geospatial.Vectors;

public abstract class VectorCollectionBase<T> : IVectorCollection<T>, IReadOnlyVectorCollection<T>, IEnumerable<T>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<T>, IReadOnlyCollection<T> where T : IExtent
{
	private CollectionVersionTracker _tracker;

	protected readonly List<T> _elements;

	private readonly Versioned<Extent> _extCache = new Versioned<Extent>();

	private static readonly bool IsSpatial = typeof(T).IsAssignableTo(typeof(ISpatialVersion));

	protected Freezer _freezer;

	private readonly ReaderWriterLock _lock = new ReaderWriterLock();

	public int Count => _elements.Count;

	public int Capacity
	{
		get
		{
			return _elements.Capacity;
		}
		set
		{
			_elements.Capacity = value;
		}
	}

	private IReadOnlyList<ISpatialVersion> _elementsAsISpatial => _elements as IReadOnlyList<ISpatialVersion>;

	public T this[int index]
	{
		get
		{
			return _elements[index];
		}
		set
		{
			if (IsReadOnly)
			{
				throw new NotSupportedException();
			}
			OnDeletedInternal(index);
			_elements[index] = value;
		}
	}

	public virtual long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			if (!IsSpatial)
			{
				return _tracker.GetVersion();
			}
			return _tracker.GetVersion(_elementsAsISpatial);
		}
	}

	public bool IsReadOnly => false;

	public Extent Extent => _extCache.Get(VersionXY, ComputeExtent);

	public ReaderWriterLock Lock => _lock;

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public VectorCollectionBase()
		: this(0)
	{
	}

	public VectorCollectionBase(int capacity)
	{
		_elements = new List<T>(capacity);
		Init();
	}

	public VectorCollectionBase(T ele)
		: this((IEnumerable<T>)new T[1] { ele })
	{
	}

	public VectorCollectionBase(IEnumerable<T> elements)
	{
		_elements = elements.ToList();
		Init();
	}

	protected virtual void Init()
	{
	}

	public void AddRange(IEnumerable<T> elements)
	{
		Capacity = Math.Max(elements.Count(), Capacity);
		foreach (T element in elements)
		{
			Add(element);
		}
	}

	public virtual void Add(T localValue)
	{
		_elements.Add(localValue);
	}

	public virtual void Clear()
	{
		if (IsSpatial)
		{
			_tracker.OnDeleted<ISpatialVersion>(_elements as IEnumerable<ISpatialVersion>);
		}
		else
		{
			_tracker.OnDeleted(_elements.Count);
		}
		_elements.Clear();
	}

	public virtual void RemoveAt(int index)
	{
		OnDeletedInternal(index);
		_elements.RemoveAt(index);
	}

	protected virtual void OnDeletedInternal(int idx)
	{
		if (IsSpatial)
		{
			_tracker.OnDeleted(_elements[idx] as ISpatialVersion);
		}
		else
		{
			_tracker.OnDeleted();
		}
	}

	public virtual void Insert(int index, T localValue)
	{
		_elements.Insert(index, localValue);
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _elements.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return _elements.GetEnumerator();
	}

	private Extent ComputeExtent()
	{
		return Extent.Union(_elements);
	}
}
