using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public interface ISurface<T> where T : unmanaged, IEquatable<T>
{
	Extent ValidExtent { get; }

	T? NoDataValue { get; }
}
