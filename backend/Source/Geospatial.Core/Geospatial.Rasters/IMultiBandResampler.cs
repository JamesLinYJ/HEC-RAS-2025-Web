using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public interface IMultiBandResampler<T> : ISurface<T> where T : unmanaged, IEquatable<T>
{
	int ProfileCount { get; }

	void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null);

	void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData);

	void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData);

	void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData);
}
