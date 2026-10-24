using System;

namespace Geospatial.Rasters;

public interface IMultiBandTileReaderWriter<T> : IMultiBandTileProvider<T>, IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition, IMultiBandTileWriter<T>, IMultiBandRasterWriter<T> where T : unmanaged, IEquatable<T>
{
}
