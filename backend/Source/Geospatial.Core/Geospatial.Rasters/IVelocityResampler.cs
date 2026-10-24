using System;
using Geospatial.Vectors;
using Utility.Units;

namespace Geospatial.Rasters;

public interface IVelocityResampler<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	void ResampleVxVy(ResampleArgs args, Memory<T> outVx, Memory<T> outVy, Memory<T> outVelMag);

	void ResampleMagDir(ResampleArgs args, Memory<T> outMag, Memory<T> outDir, DirectionConvention dirConvention = DirectionConvention.GoingToward);

	void SamplePointsVxVy(Memory<Point> points, Memory<T> outVx, Memory<T> outVy);
}
