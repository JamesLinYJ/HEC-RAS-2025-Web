using System;
using Geospatial.PairedData;

namespace Visual.Mapping.Observables.TimeSeries;

public class ObservableIrregularAbsoluteTimeSeriesDefinition : ObservableTimeSeriesDefinitionBase
{
	private readonly IrregularAbsoluteTimeSeriesDefinition _time;

	public static Type Type => typeof(ObservableIrregularAbsoluteTimeSeriesDefinition);

	public Memory<DateTime> Times
	{
		get
		{
			return _time.Times;
		}
		set
		{
			SetProperty(_time.Times, value, _time, (IrregularAbsoluteTimeSeriesDefinition r, Memory<DateTime> v) =>
			{
				r.Times = v;
			}, "Times");
			OnPropertyChanged("Count");
			NotifyTimingChanged();
		}
	}

	public ObservableIrregularAbsoluteTimeSeriesDefinition(IrregularAbsoluteTimeSeriesDefinition tsd)
		: base(tsd)
	{
		_time = tsd;
	}
}
