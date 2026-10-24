using System;

namespace Geospatial.Rasters;

public interface IMultiBandTileProvider<T> : IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition where T : unmanaged, IEquatable<T>
{
}
