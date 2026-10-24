using System;

namespace Geospatial.Rasters;

public interface IVelocityRasterizer<T> : IVelocityResampler<T>, IResample<T>, ISurface<T>, IRaster<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	void GetRangeVxVy(PixelRange range, Memory<T> outVx, Memory<T> outVy, Memory<T> outVelMag);
}
