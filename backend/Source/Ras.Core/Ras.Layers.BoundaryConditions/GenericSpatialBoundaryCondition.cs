namespace Ras.Layers.BoundaryConditions;

public class GenericSpatialBoundaryCondition : SpatialScalarBoundaryConditionBase
{
	public override string LayerTypeName => "Generic";

	public GenericSpatialBoundaryCondition(BoundaryCondition bc, string h5GroupName)
		: base(bc, h5GroupName)
	{
	}
}
