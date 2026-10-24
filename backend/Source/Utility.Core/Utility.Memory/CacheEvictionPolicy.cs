namespace Utility.Memory;

public class CacheEvictionPolicy
{
	public const int DefaultMaxCacheSize = 16777216;

	public const double DefaultEvictionFrac = 0.25;

	public bool CheckEvictionsAutomatically { get; set; }

	public float EvictionFraction { get; set; }

	public int MaxCacheSizeBytes { get; set; }

	public static CacheEvictionPolicy NoEvictions()
	{
		return new CacheEvictionPolicy
		{
			CheckEvictionsAutomatically = false,
			MaxCacheSizeBytes = int.MaxValue
		};
	}
}
