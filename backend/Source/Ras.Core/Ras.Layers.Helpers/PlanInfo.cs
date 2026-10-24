namespace Ras.Layers.Helpers;

public class PlanInfo : CoreLayerInfo
{
	public static PlanInfo From(string filename, CoreFileOpenOptions opts = null)
	{
		using Plan pl = new Plan(filename);
		return From(pl, opts);
	}

	public static PlanInfo From(Plan pl, CoreFileOpenOptions opts = null)
	{
		opts = opts ?? CoreFileOpenOptions.Default();
		PlanInfo planInfo = new PlanInfo();
		planInfo.Init(pl, opts);
		return planInfo;
	}
}
