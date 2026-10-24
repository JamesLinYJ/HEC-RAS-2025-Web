using Utility.Interfaces;

namespace Visual.Observables.Interfaces;

public interface IObservableReadOnlyDisplayName : IReadOnlyDisplayName
{
	public delegate void NameChangedEvent(IObservableReadOnlyDisplayName sender, string newName);

	event NameChangedEvent NameChanged;
}
