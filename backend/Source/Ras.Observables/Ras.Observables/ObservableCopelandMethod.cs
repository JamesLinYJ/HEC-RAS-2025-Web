using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Hydraulics.HydraulicDesign;

namespace Ras.Observables;

public class ObservableCopelandMethod : ObservableObject
{
	private CopelandMethod _copeland;

	public CopelandMethod Copeland => _copeland;

	public ObservableCopelandMethod(CopelandMethod copeland)
	{
		_copeland = copeland;
	}
}
