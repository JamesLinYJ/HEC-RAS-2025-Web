using System.Collections.Generic;

namespace RasMapperLib;

public class LandCoverCategories
{
	public static List<LandCoverCategory> NLCD2011Categories()
	{
		return new List<LandCoverCategory>
		{
			new LandCoverCategory(0, Color.FromArgb(0, 0, 0), "NoData"),
			new LandCoverCategory(11, Color.FromArgb(84, 117, 168), "Open Water"),
			new LandCoverCategory(12, Color.FromArgb(255, 255, 255), "Perennial Ice/Snow"),
			new LandCoverCategory(21, Color.FromArgb(232, 209, 209), "Developed, Open Space"),
			new LandCoverCategory(22, Color.FromArgb(226, 158, 140), "Developed, Low Intensity"),
			new LandCoverCategory(23, Color.FromArgb(255, 0, 0), "Developed, Medium Intensity"),
			new LandCoverCategory(24, Color.FromArgb(181, 0, 0), "Developed, High Intensity"),
			new LandCoverCategory(31, Color.FromArgb(210, 205, 192), "Barren Land Rock/Sand/Clay"),
			new LandCoverCategory(41, Color.FromArgb(133, 199, 126), "Deciduous Forest"),
			new LandCoverCategory(42, Color.FromArgb(56, 129, 78), "Evergreen Forest"),
			new LandCoverCategory(43, Color.FromArgb(212, 231, 176), "Mixed Forest"),
			new LandCoverCategory(51, Color.FromArgb(175, 150, 60), "Dwarf Scrub"),
			new LandCoverCategory(52, Color.FromArgb(220, 202, 143), "Shrub/Scrub"),
			new LandCoverCategory(71, Color.FromArgb(253, 233, 170), "Grassland/Herbaceous"),
			new LandCoverCategory(72, Color.FromArgb(209, 209, 130), "Sedge/Herbaceous"),
			new LandCoverCategory(73, Color.FromArgb(163, 204, 81), "Lichens"),
			new LandCoverCategory(74, Color.FromArgb(130, 186, 158), "Moss"),
			new LandCoverCategory(81, Color.FromArgb(251, 246, 93), "Pasture/Hay"),
			new LandCoverCategory(82, Color.FromArgb(202, 145, 70), "Cultivated Crops"),
			new LandCoverCategory(90, Color.FromArgb(200, 230, 248), "Woody Wetlands"),
			new LandCoverCategory(95, Color.FromArgb(100, 179, 203), "Emergent Herbaceous Wetlands")
		};
	}

	public static List<LandCoverCategory> AndersonLevel2Categories()
	{
		return new List<LandCoverCategory>
		{
			new LandCoverCategory(0, Color.FromArgb(0, 0, 0), "NoData"),
			new LandCoverCategory(11, Color.FromArgb(226, 158, 140), "Residential"),
			new LandCoverCategory(12, Color.FromArgb(255, 0, 0), "Commercial and services"),
			new LandCoverCategory(13, Color.FromArgb(181, 0, 0), "Industrial"),
			new LandCoverCategory(14, Color.FromArgb(0, 0, 0), "Transportation, communication, utilities"),
			new LandCoverCategory(15, Color.FromArgb(181, 0, 0), "Industrial and commercial complexes"),
			new LandCoverCategory(16, Color.FromArgb(226, 158, 140), "Mixed urban or built-up land"),
			new LandCoverCategory(17, Color.FromArgb(232, 209, 209), "Other urban or built-up land"),
			new LandCoverCategory(21, Color.FromArgb(251, 246, 93), "Cropland and pasture"),
			new LandCoverCategory(22, Color.FromArgb(202, 145, 70), "Orchards, groves, vineyards, nurseries, and ornamental horticultural"),
			new LandCoverCategory(23, Color.FromArgb(251, 246, 93), "Confined feeding operations"),
			new LandCoverCategory(24, Color.FromArgb(225, 200, 80), "Other agricultural land"),
			new LandCoverCategory(31, Color.FromArgb(253, 233, 170), "Herbaceous rangeland"),
			new LandCoverCategory(32, Color.FromArgb(220, 202, 143), "Shrub and brush rangeland"),
			new LandCoverCategory(33, Color.FromArgb(235, 210, 155), "Mixed rangeland"),
			new LandCoverCategory(41, Color.FromArgb(133, 199, 126), "Deciduous forest land"),
			new LandCoverCategory(42, Color.FromArgb(56, 129, 78), "Evergreen forest land"),
			new LandCoverCategory(43, Color.FromArgb(212, 231, 176), "Mixed forest land"),
			new LandCoverCategory(51, Color.FromArgb(84, 117, 168), "Streams and canals"),
			new LandCoverCategory(52, Color.FromArgb(84, 117, 168), "Lakes"),
			new LandCoverCategory(53, Color.FromArgb(84, 117, 168), "Reservoirs"),
			new LandCoverCategory(54, Color.FromArgb(84, 117, 168), "Bays and estuaries"),
			new LandCoverCategory(61, Color.FromArgb(200, 230, 248), "Forested wetland"),
			new LandCoverCategory(62, Color.FromArgb(100, 179, 213), "Nonforested wetland"),
			new LandCoverCategory(71, Color.FromArgb(210, 205, 192), "Dry salt flats"),
			new LandCoverCategory(72, Color.FromArgb(210, 205, 192), "Beaches"),
			new LandCoverCategory(73, Color.FromArgb(210, 205, 192), "Sandy areas not beaches"),
			new LandCoverCategory(74, Color.FromArgb(210, 205, 192), "Bare exposed rock"),
			new LandCoverCategory(75, Color.FromArgb(210, 205, 192), "Strip mines, quarries, gravel pits"),
			new LandCoverCategory(76, Color.FromArgb(210, 205, 192), "Transitional areas"),
			new LandCoverCategory(77, Color.FromArgb(210, 205, 192), "Mixed Barren Land"),
			new LandCoverCategory(81, Color.FromArgb(170, 186, 158), "Shrub and brush tundra"),
			new LandCoverCategory(82, Color.FromArgb(130, 210, 158), "Herbaceous tundra"),
			new LandCoverCategory(83, Color.FromArgb(210, 205, 192), "Bare ground"),
			new LandCoverCategory(84, Color.FromArgb(130, 186, 220), "Wet tundra"),
			new LandCoverCategory(85, Color.FromArgb(130, 186, 158), "Mixed tundra"),
			new LandCoverCategory(91, Color.FromArgb(255, 255, 255), "Perennial snowfields"),
			new LandCoverCategory(92, Color.FromArgb(255, 255, 255), "Glaciers")
		};
	}

	public static List<LandCoverCategory> CCAPCategories()
	{
		return new List<LandCoverCategory>
		{
			new LandCoverCategory(0, Color.FromArgb(0, 0, 0), "NoData"),
			new LandCoverCategory(1, Color.FromArgb(0, 0, 0), "Unclassified"),
			new LandCoverCategory(2, Color.FromArgb(255, 255, 255), "Developed - High Intensity"),
			new LandCoverCategory(3, Color.FromArgb(194, 194, 194), "Developed - Medium Intensity"),
			new LandCoverCategory(4, Color.FromArgb(151, 151, 151), "Developed - Low Intensity"),
			new LandCoverCategory(5, Color.FromArgb(204, 204, 0), "Developed - Open Space"),
			new LandCoverCategory(6, Color.FromArgb(84, 33, 0), "Cultivated Crops"),
			new LandCoverCategory(7, Color.FromArgb(193, 160, 79), "Pasture/Hay"),
			new LandCoverCategory(8, Color.FromArgb(242, 186, 135), "Grassland/Herbaceous"),
			new LandCoverCategory(9, Color.FromArgb(0, 242, 0), "Deciduous Forest"),
			new LandCoverCategory(10, Color.FromArgb(0, 58, 0), "Evergreen Forest"),
			new LandCoverCategory(11, Color.FromArgb(7, 160, 58), "Mixed Forest"),
			new LandCoverCategory(12, Color.FromArgb(109, 109, 0), "Scrub/Shrub"),
			new LandCoverCategory(20, Color.FromArgb(242, 242, 0), "Barren Land"),
			new LandCoverCategory(24, Color.FromArgb(249, 209, 0), "Tundra"),
			new LandCoverCategory(25, Color.FromArgb(170, 249, 239), "Perennial Ice/Snow"),
			new LandCoverCategory(13, Color.FromArgb(0, 91, 91), "Palustrine Forested Wetland"),
			new LandCoverCategory(14, Color.FromArgb(242, 109, 248), "Palustrine Scrub/Shrub Wetland"),
			new LandCoverCategory(15, Color.FromArgb(242, 0, 242), "Palustrine Emergent Wetland"),
			new LandCoverCategory(16, Color.FromArgb(61, 0, 61), "Estuarine Forested Wetland"),
			new LandCoverCategory(17, Color.FromArgb(109, 0, 109), "Estuarine Scrub/Shrub Wetland"),
			new LandCoverCategory(18, Color.FromArgb(175, 0, 175), "Estuarine Emergent Wetland"),
			new LandCoverCategory(19, Color.FromArgb(0, 242, 242), "Unconsolidated Shore"),
			new LandCoverCategory(21, Color.FromArgb(0, 0, 119), "Open Water"),
			new LandCoverCategory(22, Color.FromArgb(0, 0, 242), "Palustrine Aquatic Bed"),
			new LandCoverCategory(23, Color.FromArgb(102, 117, 219), "Estuarine Aquatic Bed")
		};
	}

	public static List<LandCoverCategory> SoilCategories()
	{
		return new List<LandCoverCategory>
		{
			new LandCoverCategory(0, Color.FromArgb(0, 0, 0), "NoData"),
			new LandCoverCategory(1908238, Color.FromArgb(226, 158, 140), "W"),
			new LandCoverCategory(2108778, Color.FromArgb(220, 202, 143), "GP"),
			new LandCoverCategory(568346, Color.FromArgb(253, 233, 170), "RpF2"),
			new LandCoverCategory(568345, Color.FromArgb(220, 202, 143), "RpE2"),
			new LandCoverCategory(568344, Color.FromArgb(235, 210, 155), "Mx"),
			new LandCoverCategory(568343, Color.FromArgb(133, 199, 126), "MnF2"),
			new LandCoverCategory(568342, Color.FromArgb(56, 129, 78), "MlF3"),
			new LandCoverCategory(568341, Color.FromArgb(212, 231, 176), "MlF2"),
			new LandCoverCategory(568340, Color.FromArgb(84, 117, 168), "MeC3"),
			new LandCoverCategory(568337, Color.FromArgb(84, 117, 168), "LoE3"),
			new LandCoverCategory(568336, Color.FromArgb(200, 230, 248), "LoE2"),
			new LandCoverCategory(568335, Color.FromArgb(100, 179, 213), "LoD3"),
			new LandCoverCategory(568334, Color.FromArgb(210, 205, 192), "LoD2"),
			new LandCoverCategory(568333, Color.FromArgb(210, 205, 192), "LoD"),
			new LandCoverCategory(568332, Color.FromArgb(210, 205, 192), "LoC3"),
			new LandCoverCategory(568331, Color.FromArgb(210, 205, 192), "LoC2"),
			new LandCoverCategory(568330, Color.FromArgb(210, 205, 192), "LoC"),
			new LandCoverCategory(568329, Color.FromArgb(210, 205, 192), "LoB3"),
			new LandCoverCategory(568328, Color.FromArgb(210, 205, 192), "LoB2"),
			new LandCoverCategory(568327, Color.FromArgb(170, 186, 158), "He"),
			new LandCoverCategory(568326, Color.FromArgb(130, 210, 158), "Gu"),
			new LandCoverCategory(568325, Color.FromArgb(210, 205, 192), "Gs"),
			new LandCoverCategory(568324, Color.FromArgb(130, 186, 220), "GrD3"),
			new LandCoverCategory(568323, Color.FromArgb(130, 186, 158), "GrD2"),
			new LandCoverCategory(568322, Color.FromArgb(255, 255, 255), "GrC3"),
			new LandCoverCategory(568321, Color.FromArgb(255, 255, 255), "GrC2"),
			new LandCoverCategory(568320, Color.FromArgb(84, 117, 168), "GrB3"),
			new LandCoverCategory(568319, Color.FromArgb(84, 117, 168), "GrB2"),
			new LandCoverCategory(568314, Color.FromArgb(235, 210, 155), "Fl"),
			new LandCoverCategory(568313, Color.FromArgb(1, 199, 126), "Fa"),
			new LandCoverCategory(568311, Color.FromArgb(56, 1, 78), "CpF2"),
			new LandCoverCategory(568310, Color.FromArgb(212, 231, 1), "Co"),
			new LandCoverCategory(568309, Color.FromArgb(1, 117, 168), "Cm"),
			new LandCoverCategory(568308, Color.FromArgb(84, 1, 168), "CaB"),
			new LandCoverCategory(568307, Color.FromArgb(84, 117, 1), "CaA")
		};
	}

	public static Dictionary<string, Color> GetGlobalNameColorMap()
	{
		Dictionary<string, Color> globalNameColorMap = new Dictionary<string, Color>();
		List<LandCoverCategory> nlcD2011Categories = NLCD2011Categories();
		nlcD2011Categories.AddRange(AndersonLevel2Categories());
		nlcD2011Categories.AddRange(CCAPCategories());
		foreach (LandCoverCategory nlcD2011Category in nlcD2011Categories)
		{
			if (!globalNameColorMap.ContainsKey(nlcD2011Category.Name))
			{
				globalNameColorMap.Add(nlcD2011Category.Name, nlcD2011Category.Color);
			}
		}
		return globalNameColorMap;
	}
}
