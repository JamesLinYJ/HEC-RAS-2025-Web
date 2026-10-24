using System;
using Geospatial.Vectors;
using Utility.Units;

namespace Geospatial.Rasters;

public interface ITimeSeriesVelocityResampler<T> : ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps where T : unmanaged, IEquatable<T>
{
	void ResampleVxVy(ResampleArgs args, int bandIndex, Memory<T> outVx, Memory<T> outVy, Memory<T> outVelMag);

	void ResampleMagDir(ResampleArgs args, int bandIndex, Memory<T> outMag, Memory<T> outDir, DirectionConvention dirConvention = DirectionConvention.GoingToward);

	void SamplePointsVxVy(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] outVx, Memory<T>[] outVy, Memory<T>[] outVelMag);

	void SamplePointsVxVy(Memory<Point> points, int bandIndex, Memory<T> outVx, Memory<T> outVy, Memory<T> outMagnitude);
}
