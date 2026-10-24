using System;

namespace Geospatial.Rasters;

public interface IMultiBandPyramidRasterProvider<T> where T : unmanaged, IEquatable<T>
{
	IMultiBandRasterProvider<T>[] PyramidLevel { get; }
}
