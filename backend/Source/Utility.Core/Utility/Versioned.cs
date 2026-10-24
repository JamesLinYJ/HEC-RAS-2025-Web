using System;

namespace Utility;

public class Versioned<T>
{
	public T Item;

	public long Version = long.MinValue;

	public T Get(long actualVersion, Func<T> compute)
	{
		return LazyThreadsafe<T>.EnsureVersion(ref Item, ref Version, actualVersion, this, compute);
	}

	public T Get<K>(long actualVersion, K k, Func<K, T> compute)
	{
		return LazyThreadsafe<T>.EnsureVersion(ref Item, ref Version, actualVersion, this, k, compute);
	}

	public T Get_NotThreadsafe<K>(long actualVersion, K k, Func<K, T> compute)
	{
		return LazyThreadsafe<T>.EnsureVersion_NotThreadsafe(ref Item, ref Version, actualVersion, k, compute);
	}
}
