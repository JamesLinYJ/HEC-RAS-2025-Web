using System;
using Geospatial.PairedData;

namespace Visual.Mapping.Observables.TimeSeries;

public class ObservableIrregularRelativeTimeSeriesDefinition : ObservableTimeSeriesDefinitionBase
{
	private readonly IrregularRelativeTimeSeriesDefinition _time;

	public Memory<TimeSpan> Times
	{
		get
		{
			return _time.Times;
		}
		set
		{
			SetProperty(_time.Times, value, _time, (IrregularRelativeTimeSeriesDefinition r, Memory<TimeSpan> v) =>
			{
				r.Times = v;
			}, "Times");
			OnPropertyChanged("Count");
			NotifyTimingChanged();
		}
	}

	public ObservableIrregularRelativeTimeSeriesDefinition(IrregularRelativeTimeSeriesDefinition tsd)
		: base(tsd)
	{
		_time = tsd;
	}
}
