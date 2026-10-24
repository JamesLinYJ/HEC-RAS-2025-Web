using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Memory;

public class CacheManager<TKey, TValue> : CacheManagerBase, ICacheManager<TKey, TValue> where TValue : class
{
	public delegate void ItemEvictionEventHandler(TKey key, TValue tValue2);

	protected enum CacheAction
	{
		Generate,
		Use,
		Set,
		Flush
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("CacheManager");

	private static readonly bool LogIndividualActions = false;

	private const int AggregateLogFrequency = 1000;

	private readonly object _logTileLock = new object();

	private int _recentCacheMisses;

	private int _recentRequests;

	private readonly Dictionary<TKey, object> _keyLocks;

	private int _itemsInCache;

	private readonly object _cacheLock = new object();

	private readonly Func<TValue, int> _getSizeInBytes;

	private readonly Dictionary<TKey, CacheItem<TValue>> _cache;

	private bool _allowsNullCacheEntries;

	private List<KeyValuePair<TKey, CacheItem<TValue>>> _sortScratchSpace;

	public string LogIdentifier { get; set; } = "Default";

	public int ItemsInCache => _itemsInCache;

	public event ItemEvictionEventHandler OnItemEvicted;

	internal override object GetLock()
	{
		return _cacheLock;
	}

	public CacheManager(Func<TValue, int> getSizeInBytes, IEqualityComparer<TKey> comparer = null, bool allowsNullCacheEntries = true)
	{
		_allowsNullCacheEntries = allowsNullCacheEntries;
		_getSizeInBytes = getSizeInBytes;
		if (comparer == null)
		{
			_keyLocks = new Dictionary<TKey, object>();
			_cache = new Dictionary<TKey, CacheItem<TValue>>();
		}
		else
		{
			_keyLocks = new Dictionary<TKey, object>(comparer);
			_cache = new Dictionary<TKey, CacheItem<TValue>>(comparer);
		}
	}

	public bool TryGet(TKey key, out TValue tValue)
	{
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var localValue))
			{
				Log(CacheAction.Use, key);
				localValue.LastAccessTime = DateTime.Now.Ticks;
				tValue = localValue.Item;
				return true;
			}
			tValue = null;
			return false;
		}
	}

	/// <summary>Transfers a cached item to its caller without invoking eviction disposal.</summary>
	public bool TryTake(TKey key, out TValue value)
	{
		lock (_cacheLock)
		{
			if (_cache.Remove(key, out var cached))
			{
				_itemsInCache--;
				value = cached.Item;
				return true;
			}
			value = null;
			return false;
		}
	}

	public void Make(TKey key, TValue madeValue)
	{
		CacheItem<TValue> cacheItem = new CacheItem<TValue>(madeValue);
		Log(CacheAction.Generate, key);
		if (!_allowsNullCacheEntries && cacheItem.Item == null)
		{
			throw new InvalidOperationException("Cannot add null value.");
		}
		lock (_cacheLock)
		{
			_cache[key] = cacheItem;
			_itemsInCache++;
		}
		CheckEvictions();
	}

	public TValue GetOrMake(TKey key, Func<TValue> generator)
	{
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var localValue))
			{
				Log(CacheAction.Use, key);
				localValue.LastAccessTime = DateTime.Now.Ticks;
				return localValue.Item;
			}
		}
		CacheItem<TValue> cacheItem = new CacheItem<TValue>(generator());
		Log(CacheAction.Generate, key);
		if (!_allowsNullCacheEntries && cacheItem.Item == null)
		{
			return null;
		}
		lock (_cacheLock)
		{
			_cache[key] = cacheItem;
			_itemsInCache++;
		}
		CheckEvictions();
		return cacheItem.Item;
	}

	public TValue GetOrMake<K>(TKey key, K model, Func<K, TValue> generator)
	{
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var localValue))
			{
				Log(CacheAction.Use, key);
				localValue.LastAccessTime = DateTime.Now.Ticks;
				return localValue.Item;
			}
		}
		CacheItem<TValue> cacheItem = new CacheItem<TValue>(generator(model));
		Log(CacheAction.Generate, key);
		if (!_allowsNullCacheEntries && cacheItem.Item == null)
		{
			return null;
		}
		lock (_cacheLock)
		{
			_cache[key] = cacheItem;
			_itemsInCache++;
		}
		CheckEvictions();
		return cacheItem.Item;
	}

	public async Task<TValue> GetOrMakeAsync(TKey key, Func<Task<TValue>> generator)
	{
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var localValue))
			{
				Log(CacheAction.Use, key);
				localValue.LastAccessTime = DateTime.Now.Ticks;
				return localValue.Item;
			}
		}
		CacheItem<TValue> cacheItem = new CacheItem<TValue>(await generator());
		Log(CacheAction.Generate, key);
		if (!_allowsNullCacheEntries && cacheItem.Item == null)
		{
			return null;
		}
		lock (_cacheLock)
		{
			_cache[key] = cacheItem;
			_itemsInCache++;
		}
		CheckEvictions();
		return cacheItem.Item;
	}

	public async Task<TValue> GetOrMakeAsync<TModel>(TKey key, TModel model, Func<TModel, Task<TValue>> generator)
	{
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var localValue))
			{
				Log(CacheAction.Use, key);
				localValue.LastAccessTime = DateTime.Now.Ticks;
				return localValue.Item;
			}
		}
		CacheItem<TValue> cacheItem = new CacheItem<TValue>(await generator(model));
		Log(CacheAction.Generate, key);
		if (!_allowsNullCacheEntries && cacheItem.Item == null)
		{
			return null;
		}
		lock (_cacheLock)
		{
			_cache[key] = cacheItem;
			_itemsInCache++;
		}
		CheckEvictions();
		return cacheItem.Item;
	}

	public async Task<TValue> GetOrMakeAsync_NotThreadsafe<TModel>(TKey key, TModel model, Func<TModel, Task<TValue>> generator)
	{
		if (_cache.TryGetValue(key, out var localValue))
		{
			Log(CacheAction.Use, key);
			localValue.LastAccessTime = DateTime.Now.Ticks;
			return localValue.Item;
		}
		CacheItem<TValue> cacheItem = new CacheItem<TValue>(await generator(model));
		Log(CacheAction.Generate, key);
		if (!_allowsNullCacheEntries && cacheItem.Item == null)
		{
			return null;
		}
		_cache[key] = cacheItem;
		_itemsInCache++;
		CheckEvictionsInternal_LockAcquired();
		return cacheItem.Item;
	}

	public virtual void Set(TKey key, TValue tValue)
	{
		if (!_allowsNullCacheEntries && tValue == null)
		{
			return;
		}
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var cache))
			{
				cache.LastAccessTime = DateTime.Now.Ticks;
				cache.Item = tValue;
			}
			else
			{
				cache = new CacheItem<TValue>(tValue);
				_cache[key] = cache;
				CheckEvictionsInternal_LockAcquired();
			}
		}
	}

	protected void Log(CacheAction action, TKey key)
	{
		switch (action)
		{
		case CacheAction.Generate:
			Interlocked.Increment(ref _recentCacheMisses);
			break;
		case CacheAction.Use:
			Interlocked.Increment(ref _recentRequests);
			break;
		}
		if (LogIndividualActions)
		{
			_logger.LogTrace("({Identifier}) {Action} '{Key}'", LogIdentifier, action, key);
		}
		if (_recentRequests < 1000)
		{
			return;
		}
		lock (_logTileLock)
		{
			if (_recentRequests >= 1000)
			{
				_logger?.LogTrace("({Identifier}) Average cache hit rate {CacheHitRate}%", LogIdentifier, Math.Round(100f * (1f - (float)_recentCacheMisses / (float)_recentRequests)));
				_recentRequests = 0;
				_recentCacheMisses = 0;
			}
		}
	}

	public TValue GetOrMakeAndPin(TKey key, Func<TValue> generator)
	{
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var localValue))
			{
				Log(CacheAction.Use, key);
				localValue.Pinned++;
				localValue.LastAccessTime = DateTime.Now.Ticks;
				return localValue.Item;
			}
		}
		CacheItem<TValue> cacheItem = new CacheItem<TValue>(generator());
		Log(CacheAction.Generate, key);
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var localValue2))
			{
				_logger.LogDebug("({Identifier}) generated multiple instances of '{Key}'. This can happen under rare circumstances. If it happens too frequently, you may have excess lock contention.", LogIdentifier, key);
				localValue2.Pinned++;
				return localValue2.Item;
			}
			cacheItem.Pinned++;
			_cache[key] = cacheItem;
			_itemsInCache++;
		}
		CheckEvictions();
		return cacheItem.Item;
	}

	public TValue GetOrMakeAndPin_EnsureSingleGeneration(TKey key, Func<TValue> generator)
	{
		int recursionDepth = 0;
		return GetOrMakeAndPin_EnsureSingleGeneration_INTERNAL(key, generator, (Func<TValue> g) => g(), ref recursionDepth);
	}

	public TValue GetOrMakeAndPin_EnsureSingleGeneration<TState>(TKey key, TState state, Func<TState, TValue> generator)
	{
		int recursionDepth = 0;
		return GetOrMakeAndPin_EnsureSingleGeneration_INTERNAL(key, state, generator, ref recursionDepth);
	}

	private TValue GetOrMakeAndPin_EnsureSingleGeneration_INTERNAL<TState>(TKey key, TState state, Func<TState, TValue> generator, ref int recursionDepth)
	{
		if (recursionDepth > 100)
		{
			throw new Exception("Cache generation recursed too many times, likely error generating item.");
		}
		object keyLocks = null;
		bool condition = false;
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var localValue))
			{
				Log(CacheAction.Use, key);
				localValue.Pinned++;
				localValue.LastAccessTime = DateTime.Now.Ticks;
				return localValue.Item;
			}
			lock (_keyLocks)
			{
				if (_keyLocks.TryGetValue(key, out keyLocks))
				{
					condition = false;
				}
				else
				{
					keyLocks = new object();
					_keyLocks[key] = keyLocks;
					condition = true;
					Monitor.Enter(keyLocks);
				}
			}
		}
		if (condition)
		{
			try
			{
				CacheItem<TValue> cacheItem = new CacheItem<TValue>(generator(state));
				Log(CacheAction.Generate, key);
				lock (_cacheLock)
				{
					cacheItem.Pinned++;
					_cache[key] = cacheItem;
					_itemsInCache++;
					lock (_keyLocks)
					{
						_keyLocks.Remove(key);
					}
				}
				CheckEvictions();
				return cacheItem.Item;
			}
			catch
			{
				throw;
			}
			finally
			{
				Monitor.Exit(keyLocks);
			}
		}
		Monitor.Enter(keyLocks);
		Monitor.Exit(keyLocks);
		recursionDepth++;
		return GetOrMakeAndPin_EnsureSingleGeneration_INTERNAL(key, state, generator, ref recursionDepth);
	}

	public void Unpin(TKey key)
	{
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(key, out var localValue))
			{
				localValue.Pinned--;
			}
		}
	}

	public override void ForceEvictAll()
	{
		lock (_cacheLock)
		{
			ForceEvictAll_NotThreadsafe();
		}
	}

	public void ForceEvictAll_NotThreadsafe()
	{
		if (OnItemEvicted != null)
		{
			foreach (KeyValuePair<TKey, CacheItem<TValue>> cache in _cache)
			{
				ItemEvicted(cache.Key, cache.Value.Item);
			}
		}
		int count = _cache.Count;
		_cache.Clear();
		_itemsInCache = 0;
		_logger.LogTrace("({Identifier}) Cache cleared {Count} items.", LogIdentifier, count);
	}

	public void ForceEvict(TKey key)
	{
		lock (_cacheLock)
		{
			ForceEvict_NotThreadsafe(key);
		}
	}

	public void ForceEvict_NotThreadsafe(TKey key)
	{
		if (_cache.TryGetValue(key, out var localValue))
		{
			_cache.Remove(key);
			if (OnItemEvicted != null)
			{
				ItemEvicted(key, localValue.Item);
			}
			_itemsInCache--;
			_logger.LogTrace("({Identifier}) Cache cleared 1 item.", LogIdentifier);
		}
	}

	public bool ContainsKey(TKey key)
	{
		lock (_cacheLock)
		{
			return _cache.ContainsKey(key);
		}
	}

	public bool ContainsKey_NotThreadsafe(TKey key)
	{
		return _cache.ContainsKey(key);
	}

	private void ItemEvicted(TKey key, TValue tValue)
	{
		OnItemEvicted?.Invoke(key, tValue);
	}

	public override List<long> GetAllElementsLastAccessed()
	{
		lock (_cacheLock)
		{
			return _cache.Values.Select((CacheItem<TValue> v) => v.LastAccessTime).ToList();
		}
	}

	public override int ActualCacheSize()
	{
		return ActualCacheSizeInternal(lockAlreadyAcquired: false);
	}

	private int ActualCacheSizeInternal(bool lockAlreadyAcquired)
	{
		if (lockAlreadyAcquired)
		{
			return ActualCacheSizeInternal_LockAcquired();
		}
		lock (_cacheLock)
		{
			return ActualCacheSizeInternal_LockAcquired();
		}
	}

	private int ActualCacheSizeInternal_LockAcquired()
	{
		int getSizeInBytesSum = 0;
		foreach (KeyValuePair<TKey, CacheItem<TValue>> cache in _cache)
		{
			if (cache.Value.Pinned == 0)
			{
				getSizeInBytesSum += _getSizeInBytes(cache.Value.Item);
			}
		}
		return getSizeInBytesSum;
	}

	public override void CheckEvictions()
	{
		lock (_cacheLock)
		{
			CheckEvictionsInternal_LockAcquired();
		}
	}

	protected void CheckEvictionsInternal_LockAcquired()
	{
		long actualCacheSizeInternal = ActualCacheSizeInternal(lockAlreadyAcquired: true);
		if (actualCacheSizeInternal <= MaxCacheSizeBytes)
		{
			return;
		}
		long maxCacheSizeBytesEvictionFractionTerm = (long)((double)MaxCacheSizeBytes * (1.0 - EvictionFraction));
		long actualCacheSizeInternalTerm = actualCacheSizeInternal - maxCacheSizeBytesEvictionFractionTerm;
		long getSizeInBytesSum = 0L;
		if (_sortScratchSpace == null)
		{
			_sortScratchSpace = new List<KeyValuePair<TKey, CacheItem<TValue>>>();
		}
		_sortScratchSpace.Clear();
		_sortScratchSpace.AddRange(_cache);
		_sortScratchSpace.Sort((KeyValuePair<TKey, CacheItem<TValue>> x, KeyValuePair<TKey, CacheItem<TValue>> y) => x.Value.LastAccessTime.CompareTo(y.Value.LastAccessTime));
		int ordinal = 0;
		for (int intervalIndex = 0; intervalIndex < _sortScratchSpace.Count; intervalIndex++)
		{
			KeyValuePair<TKey, CacheItem<TValue>> sortScratchSpace = _sortScratchSpace[intervalIndex];
			if (sortScratchSpace.Value.Pinned <= 0)
			{
				long getSizeInBytes = _getSizeInBytes(sortScratchSpace.Value.Item);
				_cache.Remove(sortScratchSpace.Key, out var localValue);
				_itemsInCache--;
				ItemEvicted(sortScratchSpace.Key, localValue.Item);
				getSizeInBytesSum += getSizeInBytes;
				ordinal++;
				if (getSizeInBytesSum >= actualCacheSizeInternalTerm)
				{
					break;
				}
			}
		}
		_logger.LogTrace("({Identifier}) Cache exceeded allowed size ({ActualByteCount} > {AllowedByteCount}). Evicted {Count} items.", LogIdentifier, actualCacheSizeInternal, MaxCacheSizeBytes, ordinal);
	}

	public ICacheManager<TKeyTransform, TValue> GetView<TKeyTransform>(Func<TKeyTransform, TKey> keyTransformer)
	{
		return new CacheView<TKey, TKeyTransform, TValue>(this, keyTransformer);
	}
}
