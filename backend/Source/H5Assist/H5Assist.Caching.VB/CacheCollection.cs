using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace H5Assist.Caching.VB;

public class CacheCollection
{
	private List<string> _filenames = new List<string>();

	private ConcurrentDictionary<string, Dictionary<Type, object>> _rowCaches = new ConcurrentDictionary<string, Dictionary<Type, object>>();

	private ConcurrentDictionary<string, Dictionary<Type, object>> _columnCaches = new ConcurrentDictionary<string, Dictionary<Type, object>>();

	private ConcurrentDictionary<string, Dictionary<Type, object>> _indexedCaches = new ConcurrentDictionary<string, Dictionary<Type, object>>();

	private List<IH5Cache> _cachesToClear = new List<IH5Cache>();

	public float TotalCacheSizeMB { get; set; } = 50f;

	private int EnsureAddFile(string fn)
	{
		int indexOf = _filenames.IndexOf(fn);
		if (indexOf == -1)
		{
			_filenames.Add(fn);
			indexOf = _filenames.IndexOf(fn);
			if (indexOf == -1)
			{
				throw new Exception("Could not add " + fn);
			}
		}
		return indexOf;
	}

	public H5RowCache<T> GetRowCache<T>(string fn) where T : struct
	{
		EnsureAddFile(fn);
		Dictionary<Type, object> localValue = null;
		lock (fn)
		{
			if (_rowCaches.TryGetValue(fn, out localValue))
			{
				Type typeFromHandle = typeof(T);
				object localValue2 = null;
				if (localValue.TryGetValue(typeFromHandle, out localValue2))
				{
					return (H5RowCache<T>)localValue2;
				}
				H5RowCache<T> h5RowCache = new H5RowCache<T>(fn);
				localValue.Add(typeFromHandle, h5RowCache);
				_cachesToClear.Add(h5RowCache);
				return h5RowCache;
			}
			localValue = new Dictionary<Type, object>();
			Type typeFromHandle2 = typeof(T);
			H5RowCache<T> h5RowCache2 = new H5RowCache<T>(fn);
			localValue.Add(typeFromHandle2, h5RowCache2);
			_cachesToClear.Add(h5RowCache2);
			_rowCaches.TryAdd(fn, localValue);
			return h5RowCache2;
		}
	}

	public H5ColumnCache<T> GetColumnCache<T>(string fn) where T : struct
	{
		EnsureAddFile(fn);
		Dictionary<Type, object> localValue = null;
		lock (fn)
		{
			if (_columnCaches.TryGetValue(fn, out localValue))
			{
				Type typeFromHandle = typeof(T);
				object localValue2 = null;
				if (localValue.TryGetValue(typeFromHandle, out localValue2))
				{
					return (H5ColumnCache<T>)localValue2;
				}
				H5ColumnCache<T> h5ColumnCache = new H5ColumnCache<T>(fn);
				_cachesToClear.Add(h5ColumnCache);
				localValue.Add(typeFromHandle, h5ColumnCache);
				return h5ColumnCache;
			}
			localValue = new Dictionary<Type, object>();
			Type typeFromHandle2 = typeof(T);
			H5ColumnCache<T> h5ColumnCache2 = new H5ColumnCache<T>(fn);
			localValue.Add(typeFromHandle2, h5ColumnCache2);
			_cachesToClear.Add(h5ColumnCache2);
			_columnCaches.TryAdd(fn, localValue);
			return h5ColumnCache2;
		}
	}

	public H5IndexedCache1D<T> GetIndexedCache<T>(string fn) where T : struct
	{
		EnsureAddFile(fn);
		Dictionary<Type, object> localValue = null;
		lock (fn)
		{
			if (_indexedCaches.TryGetValue(fn, out localValue))
			{
				Type typeFromHandle = typeof(T);
				object localValue2 = null;
				if (localValue.TryGetValue(typeFromHandle, out localValue2))
				{
					return (H5IndexedCache1D<T>)localValue2;
				}
				H5IndexedCache1D<T> h5IndexedCache1D = new H5IndexedCache1D<T>(fn);
				_cachesToClear.Add(h5IndexedCache1D);
				localValue.Add(typeFromHandle, h5IndexedCache1D);
				return h5IndexedCache1D;
			}
			localValue = new Dictionary<Type, object>();
			Type typeFromHandle2 = typeof(T);
			H5IndexedCache1D<T> h5IndexedCache1D2 = new H5IndexedCache1D<T>(fn);
			_cachesToClear.Add(h5IndexedCache1D2);
			localValue.Add(typeFromHandle2, h5IndexedCache1D2);
			_indexedCaches.TryAdd(fn, localValue);
			return h5IndexedCache1D2;
		}
	}

	public void Clear()
	{
		foreach (IH5Cache cachesToClear in _cachesToClear)
		{
			cachesToClear.Clear();
		}
	}

	public void VerifyFileDate()
	{
		foreach (IH5Cache cachesToClear in _cachesToClear)
		{
			cachesToClear.VerifyFileDate();
		}
	}
}
