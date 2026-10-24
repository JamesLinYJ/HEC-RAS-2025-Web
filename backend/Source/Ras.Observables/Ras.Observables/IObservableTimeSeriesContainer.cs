using System.ComponentModel;
using Visual.Mapping.Observables.TimeSeries;

namespace Ras.Observables;

public interface IObservableTimeSeriesContainer : INotifyPropertyChanged
{
	ObservableTimeSeriesDefinitionBase TimeSeriesDefinition { get; }

	int Count { get; }
}
