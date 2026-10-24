using System;

namespace Geospatial.Rasters;

public static class IMultiBandTileProviderExtensions
{
	public static IMultiBandRaster<T> AsRasterizer<T>(this IMultiBandTileProvider<T> tp) where T : unmanaged, IEquatable<T>
	{
		return new BandedTiledRaster<T>(tp);
	}

	public static ITileProvider<T> AsSingleProfileProviderRef<T>(this IMultiBandTileProvider<T> tp, int pfIdx) where T : unmanaged, IEquatable<T>
	{
		return new SingleBandTileProviderRef<T>(tp, pfIdx);
	}

	public static ITileProvider<T> AsSingleProfileProvider<T>(this IMultiBandTileProvider<T> tp, int bandIndex) where T : unmanaged, IEquatable<T>
	{
		return new SingleBandTileProvider<T>(tp, bandIndex);
	}
}
