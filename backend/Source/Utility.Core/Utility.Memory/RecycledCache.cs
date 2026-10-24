using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Memory;

public class RecycledCache<TKey, TValue>
{
	internal class OnLoan<T> : ImOnLoan<T>, IDisposable
	{
		public long TimeLastAccessed;

		private int _activeCount;

		public int ActiveCount => _activeCount;

		public T Item { get; init; }

		public OnLoan(T localValue)
		{
			Item = localValue;
			TimeLastAccessed = DateTime.Now.Ticks;
		}

		public void IncrementActiveCount()
		{
			Interlocked.Increment(ref _activeCount);
		}

		public void Dispose()
		{
			lock (this)
			{
				TimeLastAccessed = DateTime.Now.Ticks;
				Interlocked.Decrement(ref _activeCount);
			}
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("RecycledCache");

	private readonly Dictionary<TKey, OnLoan<TValue>> _cache = new Dictionary<TKey, OnLoan<TValue>>();

	private readonly Stack<TValue> _pooled = new Stack<TValue>();

	private readonly Dictionary<TKey, object> _generateLocks = new Dictionary<TKey, object>();

	private readonly Func<TValue> _generator;

	private readonly Action<TKey, TValue> _populate;

	private readonly Action<TValue> _onEvicted;

	public int MaxCachedCount { get; set; } = 10;

	public float EvictionFraction { get; set; } = 0.25f;

	public int KeepPooledCount { get; set; } = 10;

	public RecycledCache(Func<TValue> generate, Action<TKey, TValue> populate, Action<TValue> onEvicted)
	{
		_generator = generate;
		_populate = populate;
		_onEvicted = onEvicted;
	}

	public ImOnLoan<TValue> Get(TKey key)
	{
		lock (_cache)
		{
			if (_cache.TryGetValue(key, out var localValue))
			{
				localValue.IncrementActiveCount();
				return localValue;
			}
		}
		object generateLocks = null;
		bool condition;
		lock (_generateLocks)
		{
			if (_generateLocks.TryGetValue(key, out generateLocks))
			{
				condition = false;
			}
			else
			{
				lock (_cache)
				{
					if (_cache.TryGetValue(key, out var localValue2))
					{
						localValue2.IncrementActiveCount();
						return localValue2;
					}
				}
				generateLocks = new object();
				_generateLocks[key] = generateLocks;
				condition = true;
				Monitor.Enter(generateLocks);
			}
		}
		if (condition)
		{
			try
			{
				TValue emptyItem = GetEmptyItem();
				_populate(key, emptyItem);
				OnLoan<TValue> onLoan = new OnLoan<TValue>(emptyItem);
				onLoan.IncrementActiveCount();
				lock (_cache)
				{
					_cache[key] = onLoan;
					CheckEvictionInternal();
				}
				return onLoan;
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Unknown cache error.");
				throw;
			}
			finally
			{
				lock (_generateLocks)
				{
					_generateLocks.Remove(key);
				}
				Monitor.Exit(generateLocks);
			}
		}
		try
		{
			Monitor.Enter(generateLocks);
			Monitor.Exit(generateLocks);
			lock (_cache)
			{
				if (_cache.TryGetValue(key, out var localValue3))
				{
					localValue3.IncrementActiveCount();
					return localValue3;
				}
			}
			_logger.LogInformation("Cache failed to retrieve {Key}, likely due to high contention.", key);
			return Get(key);
		}
		catch (Exception exception2)
		{
			_logger.LogWarning(exception2, "Unknown cache error.");
			throw;
		}
	}

	private void CheckEvictionInternal()
	{
		if (_cache.Count <= MaxCachedCount)
		{
			return;
		}
		List<(TKey, long)> valueTuples = new List<(TKey, long)>();
		foreach (KeyValuePair<TKey, OnLoan<TValue>> cache in _cache)
		{
			lock (cache.Value)
			{
				if (cache.Value.ActiveCount == 0)
				{
					valueTuples.Add((cache.Key, cache.Value.TimeLastAccessed));
				}
			}
		}
		(TKey, long)[] orderBy = valueTuples.OrderBy(((TKey, long) tup) => tup.Item2).ToArray();
		int count = Math.Min(valueTuples.Count, (int)Math.Ceiling(EvictionFraction * (float)MaxCachedCount));
		for (int intervalIndex = 0; intervalIndex < count; intervalIndex++)
		{
			OnLoan<TValue> onLoan = _cache[orderBy[intervalIndex].Item1];
			_cache.Remove(orderBy[intervalIndex].Item1);
			lock (_pooled)
			{
				_pooled.Push(onLoan.Item);
			}
			_onEvicted?.Invoke(onLoan.Item);
		}
	}

	private TValue GetEmptyItem()
	{
		lock (_pooled)
		{
			if (_pooled.Count > 0)
			{
				return _pooled.Pop();
			}
		}
		return _generator();
	}
}
