using System;
using System.Threading;
using System.Threading.Tasks;

namespace Utility;

public class LazyThreadsafe<T>
{
	private readonly Lock _lock = new Lock();

	private T _item;

	private readonly Func<T> _creator;

	public static T AllocateOnce(ref T inst, object lockItem, Func<T> alloc)
	{
		if (inst == null)
		{
			lock (lockItem)
			{
				if (inst == null)
				{
					inst = alloc();
				}
			}
		}
		return inst;
	}

	public static T AllocateOnce<K>(ref T inst, object lockItem, K k, Func<K, T> alloc)
	{
		if (inst == null)
		{
			lock (lockItem)
			{
				if (inst == null)
				{
					inst = alloc(k);
				}
			}
		}
		return inst;
	}

	public static T AllocateOnce<K>(ref T inst, Lock lockItem, K k, Func<K, T> alloc)
	{
		if (inst == null)
		{
			using (lockItem.EnterScope())
			{
				if (inst == null)
				{
					inst = alloc(k);
				}
			}
		}
		return inst;
	}

	public static async Task<T> AllocateOnceAsync<K>(T inst, SemaphoreSlim semaphore, K context, Func<K, Task<T>> alloc)
	{
		if (inst == null)
		{
			try
			{
				await semaphore.WaitAsync();
				return await alloc(context);
			}
			finally
			{
				semaphore.Release();
			}
		}
		return inst;
	}

	public static T AllocateOnce(T[] coll, int idx, object lockItem, Func<T> alloc)
	{
		T coll2 = coll[idx];
		if (coll2 == null)
		{
			lock (lockItem)
			{
				coll2 = coll[idx];
				if (coll2 == null)
				{
					coll2 = (coll[idx] = alloc());
				}
			}
		}
		return coll2;
	}

	public static T EnsureVersion(ref T currentValue, ref long currentVersion, long actualVersion, object computeLock, Func<T> compute)
	{
		if (currentVersion == actualVersion)
		{
			return currentValue;
		}
		lock (computeLock)
		{
			if (currentVersion == actualVersion)
			{
				return currentValue;
			}
			currentValue = compute();
			currentVersion = actualVersion;
			return currentValue;
		}
	}

	public static T EnsureVersion_NotThreadsafe(ref T currentValue, ref long currentVersion, long actualVersion, Func<T> compute)
	{
		if (currentVersion == actualVersion)
		{
			return currentValue;
		}
		currentValue = compute();
		currentVersion = actualVersion;
		return currentValue;
	}

	public static T EnsureVersion<K>(ref T currentValue, ref long currentVersion, long actualVersion, object computeLock, K k, Func<K, T> compute)
	{
		if (currentVersion == actualVersion)
		{
			return currentValue;
		}
		lock (computeLock)
		{
			if (currentVersion == actualVersion)
			{
				return currentValue;
			}
			currentValue = compute(k);
			currentVersion = actualVersion;
			return currentValue;
		}
	}

	public static T EnsureVersion_NotThreadsafe<K>(ref T currentValue, ref long currentVersion, long actualVersion, K k, Func<K, T> compute)
	{
		if (currentVersion == actualVersion)
		{
			return currentValue;
		}
		if (currentVersion == actualVersion)
		{
			return currentValue;
		}
		currentValue = compute(k);
		currentVersion = actualVersion;
		return currentValue;
	}

	public LazyThreadsafe(Func<T> creator)
	{
		_creator = creator;
	}

	public T Touch()
	{
		return _item;
	}

	public T Acquire()
	{
		T localValue = _item;
		if (localValue != null)
		{
			return localValue;
		}
		using (_lock.EnterScope())
		{
			localValue = _item;
			if (localValue != null)
			{
				return localValue;
			}
			return _item = _creator();
		}
	}

	public void Evict()
	{
		_item = default;
	}
}
