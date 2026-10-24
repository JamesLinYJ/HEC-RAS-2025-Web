using System;
using System.Linq;
using Utility.Memory;

namespace Geospatial.Rasters;

public static class ITimeSeriesRasterProviderExtensions
{
	public static ITimeSeriesRaster<T> AsTimeSeriesRasterizer<T>(this ITimeSeriesRasterProvider<T> datasource, bool readSourceAndDisconnect = false) where T : unmanaged, IEquatable<T>
	{
		if (readSourceAndDisconnect)
		{
			int profileCount = datasource.ProfileCount;
			return new MultiBandRaster<T>(datasource.RasterDefinition, (from t in Enumerable.Range(0, profileCount)
				select datasource.ReadProfile(t)).ToArray(), datasource.NoDataValue).AsTimeSeriesRaster(datasource.Times.ToList());
		}
		return new TimeSeriesDataSourceRasterizer<T>(datasource);
	}

	public static ITimeSeriesRasterProvider<T> WithBlockCache<T>(this ITimeSeriesRasterProvider<T> datasource, IArrayCacheManager<BlockKey, T> cache) where T : unmanaged, IEquatable<T>
	{
		return new CachedTimeSeriesRasterProvider<T>(datasource, cache);
	}
}
