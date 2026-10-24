using System;

namespace Geospatial.Rasters;

public interface IMultiBandTileWriter<T> : IMultiBandRasterWriter<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T> where T : unmanaged, IEquatable<T>
{
}
