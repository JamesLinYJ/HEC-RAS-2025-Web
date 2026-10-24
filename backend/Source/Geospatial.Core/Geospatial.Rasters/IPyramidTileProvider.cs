using System;
using System.Linq;

namespace Geospatial.Rasters;

public interface IPyramidTileProvider<T> : ISurface<T> where T : unmanaged, IEquatable<T>
{
	ITileProvider<T>[] PyramidLevel { get; }

	ITileProvider<T> BasePyramidLevel => PyramidLevel.FirstOrDefault();
}
