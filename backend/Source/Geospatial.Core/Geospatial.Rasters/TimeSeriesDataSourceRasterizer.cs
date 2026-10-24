using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility;

namespace Geospatial.Rasters;

internal class TimeSeriesDataSourceRasterizer<T> : BandedDataSourceRasterizer<T>, ITimeSeriesRaster<T>, ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, IMultiBandRaster<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly ITimeSeriesRasterProvider<T> _ds;

	public IReadOnlyList<DateTime> Times => _ds.Times;

	public bool CanInterpolateTimesteps => false;

	public TimeSeriesDataSourceRasterizer(ITimeSeriesRasterProvider<T> dataSource)
		: base((IMultiBandRasterProvider<T>)dataSource)
	{
		_ds = dataSource;
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null)
	{
		if (CanInterpolateTimesteps)
		{
			throw new NotImplementedException();
		}
		int binarySearch = MathHelpers.BinarySearch(Times, time);
		if (binarySearch > 0)
		{
			Resample(args, binarySearch, outData, cache);
			return;
		}
		throw new ArgumentException("Given timestep is not a discrete index, and we cannot interpolate.");
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		if (this is IMultiBandResampler<float> ts)
		{
			float[] source = ts.SamplePoint(pt);
			return new TimeSeries(Times, ((IEnumerable<float>)source).Select((Func<float, double>)((float f) => f)));
		}
		if (this is IMultiBandResampler<double> ts2)
		{
			double[] values = ts2.SamplePoint(pt);
			return new TimeSeries(Times, values);
		}
		throw new NotImplementedException();
	}
}
