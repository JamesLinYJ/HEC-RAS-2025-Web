using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ras.Observables;

public class ExplicitCollectionChangedEventArgs<ObsFt> where ObsFt : ObservableObject
{
	public readonly List<ObsFt> NewItems;

	public readonly List<ObsFt> OldItems;

	public ExplicitCollectionChangedEventArgs(List<ObsFt> newItems, List<ObsFt> oldItems)
	{
		NewItems = newItems;
		OldItems = oldItems;
	}
}
