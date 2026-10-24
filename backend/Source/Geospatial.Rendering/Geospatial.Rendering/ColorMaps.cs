namespace Geospatial.Rendering;

public static class ColorMaps
{
	public static ColorMap<int> NLCD2011()
	{
		ColorMap<int> colorMap = new ColorMap<int>(0);
		int count = ColorPalettes.NLCD.Colors.Count;
		for (int i = 0; i < count; i++)
		{
			colorMap.SetColor(ColorPalettes.NLCDCategories[i], ColorPalettes.NLCD.Colors[i]);
		}
		return colorMap;
	}
}
