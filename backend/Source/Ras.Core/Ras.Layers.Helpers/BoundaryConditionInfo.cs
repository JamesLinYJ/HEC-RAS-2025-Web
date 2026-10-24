namespace Ras.Layers.Helpers;

public class BoundaryConditionInfo : CoreLayerInfo
{
	public static BoundaryConditionInfo From(string filename, CoreFileOpenOptions opts = null)
	{
		using BoundaryCondition bc = new BoundaryCondition(filename);
		return From(bc, opts);
	}

	public static BoundaryConditionInfo From(BoundaryCondition bc, CoreFileOpenOptions opts = null)
	{
		opts = opts ?? CoreFileOpenOptions.Default();
		BoundaryConditionInfo boundaryConditionInfo = new BoundaryConditionInfo();
		boundaryConditionInfo.Init(bc, opts);
		return boundaryConditionInfo;
	}
}
