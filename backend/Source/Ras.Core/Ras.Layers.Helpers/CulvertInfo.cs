namespace Ras.Layers.Helpers;

public class CulvertInfo
{
	public int BarrelCount { get; set; }

	public int BarrelTypeCount { get; set; }

	public int OpeningTypeCount { get; set; }

	public CulvertInfo()
	{
	}

	public CulvertInfo(Geometry gg)
	{
		BarrelCount = gg.CulvertBarrelLayer.Count;
		BarrelTypeCount = gg.BarrelPropertiesLayer.Count;
		OpeningTypeCount = gg.OpeningPropertiesLayer.Count;
	}
}
