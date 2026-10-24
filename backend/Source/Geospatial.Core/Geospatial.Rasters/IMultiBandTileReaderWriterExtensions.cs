using System;

namespace Geospatial.Rasters;

public static class IMultiBandTileReaderWriterExtensions
{
	public static ITileReaderWriter<T> AsSingleProfileWriter<T>(this IMultiBandTileReaderWriter<T> writer, int profileIndex) where T : unmanaged, IEquatable<T>
	{
		return new SingleProfileChainWriter<T>(writer, profileIndex);
	}
}
