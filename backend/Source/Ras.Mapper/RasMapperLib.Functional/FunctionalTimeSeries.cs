using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using H5Assist.VB;

namespace RasMapperLib.Functional;

public class FunctionalTimeSeries : TimeSeries
{
	private Func<DateTime, double> _lambda;

	public override DateTime StartTime => DateTime.MinValue;

	public override DateTime EndTime => DateTime.MaxValue;

	public static FunctionalTimeSeries SineWave(double amplitude = 1.0, double frequency = 1.0, DateTime? offsetDate = null)
	{
		DateTime dateTime = DateTime.MinValue;
		if (offsetDate.HasValue)
		{
			dateTime = offsetDate.Value;
		}
		return new FunctionalTimeSeries([SpecialName] (DateTime dt) => amplitude * Math.Sin(frequency * (dt - dateTime).TotalSeconds));
	}

	public FunctionalTimeSeries(Func<DateTime, double> lambda)
		: this(lambda, "", "")
	{
	}

	public FunctionalTimeSeries(Func<DateTime, double> lambda, string varName, string units)
		: base(varName, units)
	{
		_lambda = lambda;
	}

	public override double Sample(DateTime dt, bool canExtrapolate = false)
	{
		return _lambda(dt);
	}

	public RegularTimeSeries Discretize(DateTime startDate, TimeSpan dt, int count)
	{
		List<double> doubleList = new List<double>();
		checked
		{
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				doubleList.Add(Sample(startDate.AddSeconds(dt.TotalSeconds * (double)i)));
			}
			return new RegularTimeSeries(startDate, doubleList, dt, Variable, Units);
		}
	}

	public override void Save(H5Writer h5w, string groupName)
	{
		throw new NotImplementedException();
	}

	protected override void SaveInternal(H5Writer hw, string groupName)
	{
		throw new NotImplementedException();
	}
}
