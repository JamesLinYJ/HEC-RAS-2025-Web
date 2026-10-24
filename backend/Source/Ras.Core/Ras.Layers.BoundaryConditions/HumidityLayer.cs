namespace Ras.Layers.BoundaryConditions;

public class HumidityLayer : SpatialScalarBoundaryConditionBase
{
	public new const string HDFGroupName = "/Boundary Conditions/Meteorology/Humidity";

	public override string LayerTypeName => "Humidity";

	public HumidityLayer(BoundaryCondition bc)
		: base(bc, "/Boundary Conditions/Meteorology/Humidity")
	{
	}
}
