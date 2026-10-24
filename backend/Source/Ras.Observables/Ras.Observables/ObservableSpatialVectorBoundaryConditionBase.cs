using Geospatial;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Units;

namespace Ras.Observables;

public abstract class ObservableSpatialVectorBoundaryConditionBase<T> : ObservableSpatialBoundaryConditionBase<T> where T : SpatialVectorBoundaryConditionBase
{
	public float ConstantVx
	{
		get
		{
			return _layer.ConstantVx;
		}
		set
		{
			SetProperty(_layer.ConstantVx, value, _layer, (T layer, float newval) =>
			{
				layer.SetConstantVxVy(newval, layer.ConstantVy);
			}, "ConstantVx");
		}
	}

	public float ConstantVy
	{
		get
		{
			return _layer.ConstantVy;
		}
		set
		{
			SetProperty(_layer.ConstantVy, value, _layer, (T layer, float newval) =>
			{
				layer.SetConstantVxVy(layer.ConstantVx, newval);
			}, "ConstantVy");
		}
	}

	public float ConstantMagnitude => _layer.ConstantMagnitude;

	public void SetConstantVxVy(float vx, float vy)
	{
		IsEnabled = true;
		SpatialDataType = SpatialDataType.Constant;
		ConstantVx = vx;
		ConstantVy = vy;
	}

	public void SetConstantMagDir(float magnitude, float direction)
	{
		IsEnabled = true;
		SpatialDataType = SpatialDataType.Constant;
		VelocityMath.ConvertToVxVy((double)direction, (double)magnitude, DirectionConvention.ComingFrom, out double vx, out double vy);
		ConstantVx = (float)vx;
		ConstantVy = (float)vy;
	}

	public ObservableSpatialVectorBoundaryConditionBase(ObservableBoundaryCondition bc, T layer)
		: base(bc, layer)
	{
	}
}
