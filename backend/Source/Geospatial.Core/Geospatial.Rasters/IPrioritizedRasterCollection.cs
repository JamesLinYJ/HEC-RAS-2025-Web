using System;
using System.Collections.Generic;

namespace Geospatial.Rasters;

public interface IPrioritizedRasterCollection<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	List<IRaster<T>> RasterCollection { get; }
}
