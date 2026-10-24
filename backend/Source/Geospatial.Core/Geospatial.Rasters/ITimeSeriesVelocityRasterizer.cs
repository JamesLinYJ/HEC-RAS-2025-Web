using System;

namespace Geospatial.Rasters;

public interface ITimeSeriesVelocityRasterizer<T> : ITimeSeriesVelocityResampler<T>, ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	void GetRangeVxVy(PixelRange range, int bandIndex, Memory<T> vxResult, Memory<T> vyResult, Memory<T> magResult);

	void GetRangeMagDir(PixelRange range, int bandIndex, Memory<T> magResult, Memory<T> dirResult);
}
