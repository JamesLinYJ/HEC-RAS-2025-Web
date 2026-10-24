using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Threading;

namespace Utility.Memory;

public class PrefetchArrayCacheManager<TKey, TValue> : IArrayCacheManager<TKey, TValue>, IPausablePrefetch, IDisposable where TValue : unmanaged
{
	private static readonly ILogger _logger = LogHelper.GetLogger("PrefetchArrayCacheManager");

	private readonly IArrayCacheManager<TKey, TValue> _inner;

	private readonly int _itemSize;

	private readonly Func<TKey, List<TKey>> _getWindow;

	private readonly Func<TKey, Memory<TValue>, bool> _populate;

	private readonly Action<TKey, Memory<TValue>, bool> _operate;

	private readonly object _gate = new object();

	private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

	private readonly Thread _worker;

	private volatile bool _disposed;

	private int _started;

	private TKey _lastKey;

	private bool _hasLastKey;

	private int _generation;

	private int _warmingPauses;

	public int MaxCacheSizeBytes
	{
		get
		{
			return _inner.MaxCacheSizeBytes;
		}
		set
		{
			_inner.MaxCacheSizeBytes = value;
		}
	}

	public string LogIdentifier
	{
		get
		{
			return _inner.LogIdentifier;
		}
		set
		{
			_inner.LogIdentifier = value;
		}
	}

	public PrefetchArrayCacheManager(IArrayCacheManager<TKey, TValue> inner, int itemSize, Func<TKey, List<TKey>> getWindow, Func<TKey, Memory<TValue>, bool> populate, Action<TKey, Memory<TValue>, bool> operate = null)
	{
		_inner = inner ?? throw new ArgumentNullException("inner");
		_itemSize = itemSize;
		_getWindow = getWindow ?? throw new ArgumentNullException("getWindow");
		_populate = populate ?? throw new ArgumentNullException("populate");
		_operate = operate;
		_worker = new Thread(WorkerLoop)
		{
			IsBackground = true,
			Name = "PrefetchArrayCacheManager Prefetcher"
		};
	}

	public void BuildOrUseExisting(TKey key, int size, Action<Memory<TValue>> populate, Action<Memory<TValue>> operate)
	{
		_inner.BuildOrUseExisting(key, size, populate, operate);
		NotifyAccessed(key);
	}

	public void BuildOrUseExisting<TModel>(TKey key, int size, TModel model, Action<TModel, Memory<TValue>> populate, Action<TModel, Memory<TValue>> operate)
	{
		_inner.BuildOrUseExisting(key, size, model, populate, operate);
		NotifyAccessed(key);
	}

	public void BuildOrUseExisting(TKey key, int size, Func<Memory<TValue>, bool> populate, Action<Memory<TValue>, bool> operate)
	{
		_inner.BuildOrUseExisting(key, size, populate, operate);
		NotifyAccessed(key);
	}

	public bool TryUseExistingOrBuildPinned<TModel>(TKey key, int size, TModel model, Action<TModel, Memory<TValue>> populate, out ReadOnlyMemory<TValue> data)
	{
		bool result = _inner.TryUseExistingOrBuildPinned(key, size, model, populate, out data);
		NotifyAccessed(key);
		return result;
	}

	public void Unpin(TKey key)
	{
		_inner.Unpin(key);
	}

	public void ForceEvictAll()
	{
		_inner.ForceEvictAll();
	}

	public IArrayCacheManager<TKeyTransform, TValue> GetView<TKeyTransform>(Func<TKeyTransform, TKey> keyTransformer)
	{
		return new ArrayCacheView<TKey, TKeyTransform, TValue>(this, keyTransformer);
	}

	private void NotifyAccessed(TKey key)
	{
		if (_disposed)
		{
			return;
		}
		lock (_gate)
		{
			if (_warmingPauses > 0 || (_hasLastKey && EqualityComparer<TKey>.Default.Equals(key, _lastKey)))
			{
				return;
			}
			_lastKey = key;
			_hasLastKey = true;
			_generation++;
		}
		if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
		{
			_worker.Start();
		}
		_signal.Release();
	}

	public IDisposable PauseWarming()
	{
		lock (_gate)
		{
			_warmingPauses++;
			_generation++;
		}
		return new DisposableScope(() =>
		{
			lock (_gate)
			{
				_warmingPauses--;
			}
		});
	}

	private void WorkerLoop()
	{
		while (true)
		{
			_signal.Wait();
			if (_disposed)
			{
				break;
			}
			while (_signal.Wait(0))
			{
			}
			TKey lastKey;
			int generation;
			lock (_gate)
			{
				if (!_hasLastKey)
				{
					continue;
				}
				lastKey = _lastKey;
				generation = _generation;
				goto IL_0059;
			}
			IL_0059:
			WarmWindow(lastKey, generation);
		}
	}

	private void WarmWindow(TKey key, int generation)
	{
		List<TKey> getWindow;
		try
		{
			getWindow = _getWindow(key);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Prefetch window lookup failed for key {Key}", key);
			return;
		}
		if (getWindow == null)
		{
			return;
		}
		foreach (TKey windowKey in getWindow)
		{
			if (Superseded(generation))
			{
				break;
			}
			try
			{
				_inner.BuildOrUseExisting(windowKey, _itemSize, (Memory<TValue> buf) => _populate(windowKey, buf), (Memory<TValue> buf, bool ok) =>
				{
					_operate?.Invoke(windowKey, buf, ok);
				});
			}
			catch (Exception exception2)
			{
				_logger.LogDebug(exception2, "Prefetch warm failed for key {Key}", windowKey);
			}
		}
	}

	private bool Superseded(int generation)
	{
		if (_disposed)
		{
			return true;
		}
		lock (_gate)
		{
			return _generation != generation;
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
		{
			_signal.Dispose();
		}
		else
		{
			_signal.Release();
			if (_worker.Join(TimeSpan.FromSeconds(2L)))
			{
				_signal.Dispose();
			}
			else
			{
				_logger.LogDebug("Prefetch worker did not stop promptly; leaving it to drain.");
			}
		}
		(_inner as IDisposable)?.Dispose();
	}
}
