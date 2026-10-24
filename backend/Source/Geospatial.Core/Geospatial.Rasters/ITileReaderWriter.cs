using System;

namespace Geospatial.Rasters;

public interface ITileReaderWriter<T> : ITileProvider<T>, IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition, ITileWriter<T>, IRasterWriter<T> where T : unmanaged, IEquatable<T>
{
}
