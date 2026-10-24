using System;

namespace Geospatial.PairedData.TimeSeriesNew;

public class TestTimeSeries : ITimeSeries<double>, IFunctionalSeries<long, double>
{
	public TimeWindow TimeWindow
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public double? NoDataValue
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public long LowerBound
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public long UpperBound
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public double Sample(long input)
	{
		throw new NotImplementedException();
	}

	public void Sample(Memory<long> inputs, Memory<double> outData)
	{
		throw new NotImplementedException();
	}
}
