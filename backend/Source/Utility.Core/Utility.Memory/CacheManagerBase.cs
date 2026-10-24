using System;
using System.Collections.Generic;

namespace Utility.Memory;

public abstract class CacheManagerBase
{
	public const int SingleTileSize = 262144;

	public const int DefaultMaxCacheSize = 16777216;

	public const double DefaultEvictionFrac = 0.25;

	private int _maxCacheSizeBytes = 16777216;

	private double _evictionFrac = 0.25;

	public int MaxCacheSizeBytes
	{
		get
		{
			return _maxCacheSizeBytes;
		}
		set
		{
			if (value < 0)
			{
				throw new ArgumentOutOfRangeException();
			}
			int maxCacheSizeBytes = _maxCacheSizeBytes;
			_maxCacheSizeBytes = value;
			if (maxCacheSizeBytes > _maxCacheSizeBytes)
			{
				CheckEvictions();
			}
		}
	}

	public double EvictionFraction
	{
		get
		{
			return _evictionFrac;
		}
		set
		{
			if (value <= 0.0 || value > 1.0)
			{
				throw new ArgumentOutOfRangeException();
			}
			_evictionFrac = value;
		}
	}

	public abstract int ActualCacheSize();

	public abstract void ForceEvictAll();

	public abstract void CheckEvictions();

	internal abstract object GetLock();

	public abstract List<long> GetAllElementsLastAccessed();
}
