using System;

namespace Geospatial.Rasters;

public interface IRasterPyramid<T> : IRaster<T>, IResample<T>, ISurface<T>, IRasterDefinition, IPyramidLevel where T : unmanaged, IEquatable<T>
{
	IRaster<T> GetPyramidLevel(int lvl);
}
