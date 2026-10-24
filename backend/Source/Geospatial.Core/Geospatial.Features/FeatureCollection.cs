using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Features;

[DebuggerDisplay("{DebuggerDisplayString()},nq")]
public class FeatureCollection<T> : IVectorCollection<T>, IReadOnlyVectorCollection<T>, IEnumerable<T>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<T>, IReadOnlyCollection<T>
{
	protected IVectorCollection<T> _featureCollection;

	protected readonly Table _attrTable;

	public IVectorCollection<T> Features => _featureCollection;

	public Table AttributeTable => _attrTable;

	public int Count => _featureCollection.Count;

	public bool IsReadOnly => false;

	public T this[int index]
	{
		get
		{
			return _featureCollection[index];
		}
		set
		{
			_featureCollection[index] = value;
		}
	}

	public long VersionXY => _featureCollection.VersionXY;

	public Extent Extent => _featureCollection.Extent;

	public ReaderWriterLock Lock => _featureCollection.Lock;

	public FeatureCollection(IVectorCollection<T> ftCollection)
	{
		_featureCollection = ftCollection;
		_attrTable = new Table();
		for (int i = 0; i < ftCollection.Count; i++)
		{
			_attrTable.AddNewRow();
		}
	}

	public FeatureCollection(IVectorCollection<T> ftCollection, IEnumerable<TableColumn> dataColumns)
	{
		_featureCollection = ftCollection;
		_attrTable = new Table();
		foreach (TableColumn dataColumn in dataColumns)
		{
			_attrTable.AddColumn(dataColumn);
		}
		for (int i = 0; i < ftCollection.Count; i++)
		{
			_attrTable.AddNewRow();
		}
	}

	public FeatureCollection(IVectorCollection<T> featureCollection, Table attrTable)
	{
		if (featureCollection.Count != attrTable.Rows.Count)
		{
			throw new ArgumentException();
		}
		_featureCollection = featureCollection;
		_attrTable = attrTable;
	}

	public TableRow Add(T feature)
	{
		_featureCollection.Add(feature);
		return _attrTable.AddNewRow();
	}

	public void AddRange(IList<T> features)
	{
		for (int i = 0; i < features.Count; i++)
		{
			_featureCollection.Add(features[i]);
			_attrTable.AddNewRow();
		}
	}

	protected T NullFeature()
	{
		return default;
	}

	public void SetSize(int newSize)
	{
		if (newSize < Count)
		{
			throw new Exception("Cannot set capacity lower than the current collection count.");
		}
		if (newSize != Count)
		{
			int count = newSize - Count;
			for (int i = 0; i < count; i++)
			{
				Add(NullFeature());
			}
		}
	}

	public TableRow Insert(int index, T localValue)
	{
		_featureCollection.Insert(index, localValue);
		return _attrTable.InsertRow(index);
	}

	public void RemoveAt(int index)
	{
		_featureCollection.RemoveAt(index);
		_attrTable.RemoveRow(index);
	}

	public void Clear()
	{
		_featureCollection.Clear();
		_attrTable.ClearRows();
	}

	void IVectorCollection<T>.Add(T localValue)
	{
		Add(localValue);
	}

	void IVectorCollection<T>.Insert(int index, T localValue)
	{
		Insert(index, localValue);
	}

	public void FreezeOne()
	{
		_featureCollection.FreezeOne();
	}

	public void ThawOne()
	{
		_featureCollection.ThawOne();
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _featureCollection.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return _featureCollection.GetEnumerator();
	}

	private string DebuggerDisplayString()
	{
		return $"{Count} Features with {AttributeTable.Columns.Count} Columns";
	}
}
