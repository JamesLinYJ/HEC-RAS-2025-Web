using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Hydraulics.HydraulicDesign;

namespace Ras.Observables;

public class ObservableRegimeMethod : ObservableObject
{
	private RegimeMethod _regime;

	public RegimeMethod Regime => _regime;

	public ObservableRegimeMethod(RegimeMethod regime)
	{
		_regime = regime;
	}
}
