using System;

namespace Geospatial.Rasters;

public interface ITimeSeriesRasterProvider<T> : IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, IDiscreteTimesteps where T : unmanaged, IEquatable<T>
{
}
