using System;

namespace Geospatial.Rasters;

public interface IAccumulatedRasterizer<T> : IAccumulatedResampler<T>, ISurface<T>, IDiscreteTimesteps, IRasterDefinition where T : unmanaged, IEquatable<T>
{
}
