namespace Ras.Layers.BoundaryConditions;

public class AirDensityLayer : SpatialScalarBoundaryConditionBase
{
	public const string DefaultHDFGroupName = "/Boundary Conditions/Meteorology/Air Density";

	public override string LayerTypeName => "Air Density";

	protected override void SetDefaultInternal() => ConstantValue = 1.225f;

	protected override bool IsDefaultInternal() => ConstantValue == 1.225f;

	public AirDensityLayer(BoundaryCondition bc)
		: base(bc, "/Boundary Conditions/Meteorology/Air Density")
	{
		ConstantValue = 1.225f;
	}
}
