using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.Rendering.Systems;

public class PoolProvider<T> : IDisposable where T : class
{
	private static readonly ILogger _logger = LogHelper.GetLogger("PoolProvider");

	private readonly Pool<T> _pool;

	private readonly Dictionary<T, PoolHandle<T>> _dic;

	private bool _isDisposed;

	public PoolProvider(Func<T> generator, int initCapacity = 0)
	{
		_dic = new Dictionary<T, PoolHandle<T>>(initCapacity);
		_pool = new Pool<T>(generator);
		_pool.MaxResidentAllowed = 650;
		_pool.OnEvicted = (T el) =>
		{
			if (el is IDisposable disposable)
			{
				disposable.Dispose();
			}
		};
	}

	public PoolProvider(Func<T> generator, int maximumResidentsAllowed, int initCapacity = 0)
	{
		_dic = new Dictionary<T, PoolHandle<T>>(initCapacity);
		_pool = new Pool<T>(generator);
		_pool.MaxResidentAllowed = maximumResidentsAllowed;
		_pool.OnEvicted = (T el) =>
		{
			if (el is IDisposable disposable)
			{
				disposable.Dispose();
			}
		};
	}

	public T Acquire()
	{
		PoolHandle<T> poolHandle = _pool.Acquire();
		lock (_dic)
		{
			_dic[poolHandle.Data] = poolHandle;
		}
		return poolHandle.Data;
	}

	public T Acquire_NotThreadsafe()
	{
		PoolHandle<T> poolHandle = _pool.Acquire_NotThreadsafe();
		_dic[poolHandle.Data] = poolHandle;
		return poolHandle.Data;
	}

	public void Return(T bmp)
	{
		lock (_dic)
		{
			if (_dic.ContainsKey(bmp))
			{
				_dic[bmp].Dispose();
				_dic.Remove(bmp);
			}
		}
	}

	public void Return_NotThreadsafe(T bmp)
	{
		if (_dic.ContainsKey(bmp))
		{
			_dic[bmp].Dispose_NotThreafsafe();
			_dic.Remove(bmp);
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				_pool.ClearAll();
			}
			_isDisposed = true;
		}
	}

	~PoolProvider()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
