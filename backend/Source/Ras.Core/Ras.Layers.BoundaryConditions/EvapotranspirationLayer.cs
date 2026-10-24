namespace Ras.Layers.BoundaryConditions;

public class EvapotranspirationLayer : SpatialScalarBoundaryConditionBase
{
	public new const string HDFGroupName = "/Boundary Conditions/Meteorology/Evapotranspiration";

	public override string LayerTypeName => "Evapotranspiration";

	public EvapotranspirationLayer(BoundaryCondition bc)
		: base(bc, "/Boundary Conditions/Meteorology/Evapotranspiration")
	{
	}
}
