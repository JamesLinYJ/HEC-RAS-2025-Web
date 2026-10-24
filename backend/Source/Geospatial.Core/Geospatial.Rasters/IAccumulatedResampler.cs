using System;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility.Units;

namespace Geospatial.Rasters;

public interface IAccumulatedResampler<T> : ISurface<T>, IDiscreteTimesteps where T : unmanaged, IEquatable<T>
{
	Time TimeDenominator { get; }

	bool HasDiscreteTimesteps { get; }

	TimeSeriesDataType NativeDataType { get; }

	void ResampleAccumulated(ResampleArgs args, DateTime start, DateTime end, Memory<T> outData);

	void ResampleRates(ResampleArgs args, DateTime time, Memory<T> outData);

	RateTimeSeries SampleRateTimeSeries(Point pt);
}
