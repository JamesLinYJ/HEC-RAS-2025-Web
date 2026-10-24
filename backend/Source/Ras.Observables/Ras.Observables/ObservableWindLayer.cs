using System;
using Ras.Layers;
using Utility.Units;

namespace Ras.Observables;

public class ObservableWindLayer : ObservableSpatialVectorBoundaryConditionBase<WindLayer>
{
	private static Type[] _validTypes = new Type[1] { typeof(WindSpeed) };

	public override Type[] ValidUnitTypes => _validTypes;

	public ObservableWindLayer(ObservableBoundaryCondition bc)
		: base(bc, bc.BoundaryCondition.Wind)
	{
	}
}
