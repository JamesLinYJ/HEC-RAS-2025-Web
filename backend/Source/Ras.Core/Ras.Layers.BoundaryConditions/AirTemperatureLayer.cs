namespace Ras.Layers.BoundaryConditions;

public class AirTemperatureLayer : SpatialScalarBoundaryConditionBase
{
	public new const string HDFGroupName = "/Boundary Conditions/Meteorology/Air Temperature";

	public override string LayerTypeName => "Air Temperature";

	public AirTemperatureLayer(BoundaryCondition bc)
		: base(bc, "/Boundary Conditions/Meteorology/Air Temperature")
	{
	}
}
