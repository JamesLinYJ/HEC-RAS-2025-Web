using System;
using Geospatial.PairedData;

namespace Visual.Mapping.Observables.TimeSeries;

public class ObservableRegularTimeSeriesDefinition : ObservableTimeSeriesDefinitionBase
{
	private readonly RegularTimeSeriesDefinition _time;

	public static Type Type => typeof(ObservableRegularTimeSeriesDefinition);

	public TimeSpan Interval
	{
		get
		{
			return _time.Interval;
		}
		set
		{
			SetProperty(_time.Interval, value, _time, (RegularTimeSeriesDefinition r, TimeSpan v) =>
			{
				r.Interval = v;
			}, "Interval");
			NotifyTimingChanged();
		}
	}

	public DateTime FixedStartTime
	{
		get
		{
			return _time.FixedStartTime;
		}
		set
		{
			SetProperty(_time.FixedStartTime, value, _time, (RegularTimeSeriesDefinition r, DateTime v) =>
			{
				r.FixedStartTime = v;
			}, "FixedStartTime");
			OnPropertyChanged("UseFixedStartTime");
			NotifyTimingChanged();
		}
	}

	public bool UseFixedStartTime
	{
		get
		{
			return _time.UseFixedStartTime;
		}
		set
		{
			throw new InvalidOperationException("Can not set UseFixedStartTime. To do so, set FixedStartTime to null. Setter is only exposed for binding purposes.");
		}
	}

	public ObservableRegularTimeSeriesDefinition(RegularTimeSeriesDefinition tsd)
		: base(tsd)
	{
		_time = tsd;
	}
}
