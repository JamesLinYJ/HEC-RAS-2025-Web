using System;

namespace Geospatial.PairedData.TimeSeriesNew;

public class FunctionalTimeSeries : ITimeSeries<double>, IFunctionalSeries<long, double>
{
	private readonly Func<long, double> _fxn;

	public TimeWindow TimeWindow => new TimeWindow(new DateTime(LowerBound), new DateTime(UpperBound));

	public double? NoDataValue { get; set; }

	public long LowerBound { get; set; }

	public long UpperBound { get; set; }

	public FunctionalTimeSeries(DateTime min, DateTime max, Func<long, double> function)
	{
		LowerBound = min.Ticks;
		UpperBound = max.Ticks;
		_fxn = function;
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
