using System;

namespace Geospatial.Rasters;

public static class IMultiBandPyramidTileProviderExtensions
{
	public static IPyramidTileProvider<T> AsSingleProfile<T>(this IMultiBandPyramidTileProvider<T> src, int bandIndex) where T : unmanaged, IEquatable<T>
	{
		if (src.CachedBands != null && src.CachedBands.TryGetValue(bandIndex, out var localValue))
		{
			return localValue;
		}
		PinnedBandPyramidTileProvider<T> pinnedBandPyramidTileProvider = new PinnedBandPyramidTileProvider<T>(src, bandIndex);
		if (src.CachedBands != null)
		{
			src.CachedBands[bandIndex] = pinnedBandPyramidTileProvider;
		}
		return pinnedBandPyramidTileProvider;
	}

	public static IMultiBandRaster<T> AsRaster<T>(this IMultiBandPyramidTileProvider<T> src, bool fallbackToLowerResOnReadFailure = false) where T : unmanaged, IEquatable<T>
	{
		return new BandedRasterPyramid<T>(src)
		{
			FallbackToLowerResOnReadFailure = fallbackToLowerResOnReadFailure
		};
	}
}
