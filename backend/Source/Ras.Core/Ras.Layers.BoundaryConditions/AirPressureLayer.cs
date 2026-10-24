namespace Ras.Layers.BoundaryConditions;

public class AirPressureLayer : SpatialScalarBoundaryConditionBase
{
	public new const string HDFGroupName = "/Boundary Conditions/Meteorology/Air Pressure";

	public override string LayerTypeName => "Air Pressure";

	protected override void SetDefaultInternal() => ConstantValue = 1013.2f;

	protected override bool IsDefaultInternal() => ConstantValue == 1013.2f;

	public AirPressureLayer(BoundaryCondition bc)
		: base(bc, "/Boundary Conditions/Meteorology/Air Pressure")
	{
		ConstantValue = 1013.2f;
	}
}
