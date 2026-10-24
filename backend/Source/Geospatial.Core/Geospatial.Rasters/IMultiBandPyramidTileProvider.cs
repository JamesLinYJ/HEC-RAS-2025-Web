using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Geospatial.Rasters;

public interface IMultiBandPyramidTileProvider<T> : ISurface<T> where T : unmanaged, IEquatable<T>
{
	ConcurrentDictionary<int, IPyramidTileProvider<T>> CachedBands => null;

	int ProfileCount { get; }

	IMultiBandTileProvider<T>[] PyramidLevel { get; }

	IMultiBandTileProvider<T> BasePyramidLevel => PyramidLevel.FirstOrDefault();
}
