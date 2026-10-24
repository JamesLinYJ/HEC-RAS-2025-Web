using System.Collections.Generic;
using System.Linq;

namespace Utility.Memory;

public class SharedCacheManager
{
	private readonly List<CacheManagerBase> _caches = new List<CacheManagerBase>();

	public void Add(CacheManagerBase cache)
	{
	}

	public void CheckEvictions()
	{
		lock (_caches)
		{
			_caches.Sum((CacheManagerBase c) => c.ActualCacheSize());
			_caches.SelectMany((CacheManagerBase c) => c.GetAllElementsLastAccessed()).ToList();
		}
	}
}
