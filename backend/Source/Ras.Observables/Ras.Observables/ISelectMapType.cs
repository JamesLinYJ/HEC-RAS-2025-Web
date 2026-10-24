using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Ras.Observables;

public interface ISelectMapType : INotifyPropertyChanged
{
	public delegate void SelectedMapTypeChangedEvent(ISelectMapType sender, string newValue);

	ObservableCollection<string> AvailableMapTypes { get; }

	string SelectedMapType { get; set; }

	event SelectedMapTypeChangedEvent SelectedMapTypeChanged;
}
