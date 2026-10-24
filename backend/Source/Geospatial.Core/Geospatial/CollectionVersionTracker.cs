using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Utility;

namespace Geospatial;

public struct CollectionVersionTracker
{
	private const int ParallelVersionCheckThreshold = 50000;

	private long _frozenVersion = 0L;

	private int _frozenCount = 0;

	public const long InitialVersion = 1L;

	private long _versionStore = 1L;

	private bool IsFrozen => _frozenCount != 0;

	public void FreezeOne<T>(in IEnumerable<T> items) where T : ISpatialVersion
	{
		if (_frozenCount == 0)
		{
			_frozenVersion = GetVersion(in items);
		}
		Interlocked.Increment(ref _frozenCount);
	}

	public void FreezeOne<T>(in IReadOnlyList<T> items) where T : ISpatialVersion
	{
		if (_frozenCount == 0)
		{
			_frozenVersion = GetVersion(items);
		}
		Interlocked.Increment(ref _frozenCount);
	}

	public void FreezeOne<T>(in T localValue) where T : ISpatialVersion
	{
		if (_frozenCount == 0)
		{
			_frozenVersion = GetVersion(in localValue);
		}
		Interlocked.Increment(ref _frozenCount);
	}

	public void ThawOne()
	{
		Interlocked.Decrement(ref _frozenCount);
	}

	public CollectionVersionTracker()
	{
	}

	public void OnDeleted(ISpatialVersion iSpatialVersion)
	{
		long deletionIncrement = GetDeletionIncrement(iSpatialVersion);
		Interlocked.Add(ref _versionStore, deletionIncrement);
	}

	private static long GetDeletionIncrement(ISpatialVersion iSpatialVersion)
	{
		if (iSpatialVersion == null)
		{
			return 1L;
		}
		return iSpatialVersion.VersionXY + 1;
	}

	public void OnDeleted<T>(in IEnumerable<T> items) where T : ISpatialVersion
	{
		long sum = items.Sum((T localValue) => GetDeletionIncrement(localValue));
		Interlocked.Add(ref _versionStore, sum);
	}

	public void OnDeleted<T>(in IReadOnlyList<T> items) where T : ISpatialVersion
	{
		long deletionIncrementSum = 0L;
		for (int i = 0; i < items.Count; i++)
		{
			deletionIncrementSum += GetDeletionIncrement(items[i]);
		}
		Interlocked.Add(ref _versionStore, deletionIncrementSum);
	}

	public void OnDeleted()
	{
		Interlocked.Increment(ref _versionStore);
	}

	public void OnDeleted(long count)
	{
		Interlocked.Add(ref _versionStore, count);
	}

	public long GetVersion<T>(in IEnumerable<T> items) where T : ISpatialVersion
	{
		if (IsFrozen)
		{
			return _frozenVersion;
		}
		return items.Sum((T i) => i?.VersionXY ?? 1) + _versionStore;
	}

	public long GetVersion<T>(IReadOnlyList<T> items) where T : ISpatialVersion
	{
		if (IsFrozen)
		{
			return _frozenVersion;
		}
		return GetVersion(items, 1L) + _versionStore;
	}

	public static long GetVersion<T>(IReadOnlyList<T> items, long initialVersion) where T : ISpatialVersion
	{
		if (items.Count >= 50000)
		{
			return GetVersionParallel(items, initialVersion);
		}
		long versionXYInitialVersionTerm = 0L;
		for (int i = 0; i < items.Count; i++)
		{
			long ordinal = versionXYInitialVersionTerm;
			T localValue = items[i];
			versionXYInitialVersionTerm = ordinal + ((localValue != null) ? localValue.VersionXY : initialVersion);
		}
		return versionXYInitialVersionTerm;
	}

	private static long GetVersionParallel<T>(IReadOnlyList<T> items, long initialVersion) where T : ISpatialVersion
	{
		ThreadLocal<long> localSums = new ThreadLocal<long>(trackAllValues: true);
		Parallel.SmartFor(items.Count, (int start, int end) =>
		{
			long versionXYInitialVersionTerm = localSums.Value;
			for (int i = start; i <= end; i++)
			{
				long ordinal = versionXYInitialVersionTerm;
				T localValue = items[i];
				versionXYInitialVersionTerm = ordinal + ((localValue != null) ? localValue.VersionXY : initialVersion);
			}
			localSums.Value = versionXYInitialVersionTerm;
		}, 50000);
		long versionParallel = 0L;
		for (int intervalIndex = 0; intervalIndex < localSums.Values.Count; intervalIndex++)
		{
			versionParallel += localSums.Values[intervalIndex];
		}
		return versionParallel;
	}

	public long GetVersion<T>(in T localValue2) where T : ISpatialVersion
	{
		if (IsFrozen)
		{
			return _frozenVersion;
		}
		T localValue = localValue2;
		return ((localValue != null) ? localValue.VersionXY : 1) + _versionStore;
	}

	public long GetVersion()
	{
		return _versionStore;
	}
}
