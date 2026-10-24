using System;

namespace Geospatial.Rasters;

public interface ICacheEviction
{
	void EvictCache();

	IDisposable TryPauseCache()
	{
		return NoOpDisposable.Instance;
	}
}
