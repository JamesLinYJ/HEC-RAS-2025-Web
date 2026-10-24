using System;

namespace Geospatial.Rasters;

public interface IMultiBandRaster<T> : IMultiBandResampler<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	void GetRange(PixelRange range, int bandIndex, Memory<T> result);
}
