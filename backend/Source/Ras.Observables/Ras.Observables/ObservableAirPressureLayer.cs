using System;
using Ras.Layers.BoundaryConditions;
using Utility.Units;

namespace Ras.Observables;

public class ObservableAirPressureLayer : ObservableSpatialScalarBoundaryConditionBase<AirPressureLayer>
{
	private static Type[] _validTypes = new Type[1] { typeof(AirPressure) };

	public override Type[] ValidUnitTypes => _validTypes;

	public ObservableAirPressureLayer(ObservableBoundaryCondition bc)
		: base(bc, bc.BoundaryCondition.AirPressure)
	{
	}
}
