using System;
using Ras.Layers.BoundaryConditions;
using Utility.Units;

namespace Ras.Observables;

public class ObservablePrecipitationLayer : ObservableSpatialScalarBoundaryConditionBase<PrecipitationLayer>
{
	private static Type[] _validTypes = new Type[2]
	{
		typeof(PeriodAveragePrecipitation),
		typeof(PeriodCumulativePrecipitation)
	};

	public override Type[] ValidUnitTypes => _validTypes;

	public ObservablePrecipitationLayer(ObservableBoundaryCondition bc)
		: base(bc, bc.BoundaryCondition.Precipitation)
	{
	}
}
