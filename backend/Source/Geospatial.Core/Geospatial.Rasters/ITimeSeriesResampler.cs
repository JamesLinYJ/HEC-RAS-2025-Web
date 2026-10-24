using System;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility;

namespace Geospatial.Rasters;

public interface ITimeSeriesResampler<T> : IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps where T : unmanaged, IEquatable<T>
{
	bool CanInterpolateTimesteps { get; }

	void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null);

	TimeSeries SamplePointTimeSeries(Point pt);

	int IndexOfTime(DateTime dt)
	{
		return MathHelpers.BinarySearch(Times, dt);
	}
}
