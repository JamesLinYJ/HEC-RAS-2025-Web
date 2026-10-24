using System;

namespace Geospatial.Rasters;

public interface IRaster<T> : IResample<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	void GetRange(PixelRange range, Memory<T> outData);
}
