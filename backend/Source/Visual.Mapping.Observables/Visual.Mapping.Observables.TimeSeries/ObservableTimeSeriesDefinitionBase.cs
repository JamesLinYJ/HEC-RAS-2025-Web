using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.PairedData;

namespace Visual.Mapping.Observables.TimeSeries;

public class ObservableTimeSeriesDefinitionBase : ObservableValidator
{
	public delegate void TimingChangedEvent(ObservableTimeSeriesDefinitionBase sender);

	private TimeSeriesDefinitionBase _time;

	public TimeSeriesDefinitionBase Definition => _time;

	public int Count => _time.Count;

	public event TimingChangedEvent TimingChanged;

	public ObservableTimeSeriesDefinitionBase(TimeSeriesDefinitionBase tsd)
	{
		_time = tsd;
	}

	public DateTime TimeFor(DateTime injectedStartTime, int index)
	{
		return _time.TimeFor(injectedStartTime, index);
	}

	public DateTime[] GetAbsoluteTimes(int nOrdinates, DateTime startTime)
	{
		return _time.GetAbsoluteTimes(nOrdinates, startTime);
	}

	protected void NotifyTimingChanged()
	{
		TimingChanged?.Invoke(this);
	}

	public static ObservableTimeSeriesDefinitionBase From(TimeSeriesDefinitionBase tsd)
	{
		return tsd.Switch((Func<RegularTimeSeriesDefinition, ObservableTimeSeriesDefinitionBase>)((RegularTimeSeriesDefinition rts) => new ObservableRegularTimeSeriesDefinition(rts)), (Func<IrregularAbsoluteTimeSeriesDefinition, ObservableTimeSeriesDefinitionBase>)((IrregularAbsoluteTimeSeriesDefinition irts) => new ObservableIrregularAbsoluteTimeSeriesDefinition(irts)), (Func<IrregularRelativeTimeSeriesDefinition, ObservableTimeSeriesDefinitionBase>)((IrregularRelativeTimeSeriesDefinition fts) => new ObservableIrregularRelativeTimeSeriesDefinition(fts)));
	}
}
