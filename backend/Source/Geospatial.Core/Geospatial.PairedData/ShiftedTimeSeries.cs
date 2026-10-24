using System;
using System.Collections.Generic;
using System.Linq;
using Utility.DataStructures;

namespace Geospatial.PairedData;

public class ShiftedTimeSeries : IDiscreteTimeSeries, ITimeSeries
{
	private readonly IDiscreteTimeSeries _baseTimeSeries;

	private TimeSpan _shift;

	public IReadOnlyList<DateTime> Times { get; }

	public IReadOnlyList<double> Values => _baseTimeSeries.Values;

	public DateTime StartTime => Times.FirstOrDefault();

	public DateTime EndTime => Times.LastOrDefault();

	public TimeSeriesDataBehavior Behavior
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public ShiftedTimeSeries(IDiscreteTimeSeries baseTimeSeries, TimeSpan shift)
	{
		_baseTimeSeries = baseTimeSeries;
		_shift = shift;
		Times = new TranslatedList<DateTime, DateTime>(() => Times, (DateTime dt) => dt.Add(_shift));
	}

	public void Shift(TimeSpan deltaTime)
	{
		_shift = _shift.Add(deltaTime);
	}

	public void Scale(double scaleFactor)
	{
		throw new NotImplementedException();
	}

	public double Sample(DateTime time)
	{
		throw new NotImplementedException();
	}

	public double Sample(int timestepIndex)
	{
		throw new NotImplementedException();
	}

	public double[] Sample(IList<DateTime> times)
	{
		throw new NotImplementedException();
	}
}
