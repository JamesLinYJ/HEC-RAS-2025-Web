using Ras.Layers;

namespace Ras.Observables;

public static class ObservableExtensions
{
	public static ObservableBoundaryCondition AsObservable(this BoundaryCondition bc, ObservableProject prj)
	{
		return new ObservableBoundaryCondition(prj, bc);
	}
}
