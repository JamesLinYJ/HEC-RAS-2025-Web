using Ras.Layers.BoundaryConditions;

namespace Ras.Observables;

public abstract class ObservableSpatialScalarBoundaryConditionBase<T> : ObservableSpatialBoundaryConditionBase<T> where T : SpatialScalarBoundaryConditionBase
{
	public float ConstantValue
	{
		get
		{
			return _layer.ConstantValue;
		}
		set
		{
			SetProperty(_layer.ConstantValue, value, _layer, (T layer, float newval) =>
			{
				layer.SetConstantValue(newval);
			}, "ConstantValue");
		}
	}

	public ObservableSpatialScalarBoundaryConditionBase(ObservableBoundaryCondition bc, T layer)
		: base(bc, layer)
	{
	}
}
