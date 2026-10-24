using System.Collections.Generic;

namespace Ras;

public static class ClassificationStandards
{
	public static ClassificationStandard NLCD2011 = new ClassificationStandard("NLCD2011", "National Land Cover Database 2011 naming standard", new Dictionary<int, string>
	{
		{ 0, "NoData" },
		{ 11, "Open Water" },
		{ 12, "Perennial Ice/Snow" },
		{ 21, "Developed, Open Space" },
		{ 22, "Developed, Low Intensity" },
		{ 23, "Developed, Medium Intensity" },
		{ 24, "Developed, High Intensity" },
		{ 31, "Barren Land Rock/Sand/Clay" },
		{ 41, "Deciduous Forest" },
		{ 42, "Evergreen Forest" },
		{ 43, "Mixed Forest" },
		{ 51, "Dwarf Scrub" },
		{ 52, "Shrub/Scrub" },
		{ 71, "Grassland/Herbaceous" },
		{ 72, "Sedge/Herbaceous" },
		{ 73, "Lichens" },
		{ 74, "Moss" },
		{ 81, "Pasture/Hay" },
		{ 82, "Cultivated Crops" },
		{ 90, "Woody Wetlands" },
		{ 95, "Emergent Herbaceous Wetlands" }
	});
}
