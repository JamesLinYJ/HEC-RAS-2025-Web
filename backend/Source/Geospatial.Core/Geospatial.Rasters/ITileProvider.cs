using System;

namespace Geospatial.Rasters;

public interface ITileProvider<T> : IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition where T : unmanaged, IEquatable<T>
{
}
