using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public interface IResample<T> : ISurface<T> where T : unmanaged, IEquatable<T>
{
	void Resample(ResampleArgs args, Memory<T> outData);

	void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams));

	T SamplePoint(Point point)
	{
		Point[] points = new Point[1] { point };
		T[] values = new T[1];
		SamplePoints(points, values);
		return values[0];
	}
}
