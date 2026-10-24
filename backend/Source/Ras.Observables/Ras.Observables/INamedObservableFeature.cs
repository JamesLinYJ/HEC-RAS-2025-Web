using System.ComponentModel;

namespace Ras.Observables;

public interface INamedObservableFeature : INotifyPropertyChanged
{
	string Name { get; set; }
}
