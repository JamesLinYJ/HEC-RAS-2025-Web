using Geospatial.Rasters;
using Utility.Units;

namespace Ras.Layers;

public class TerrainRasterImportConfig
{
	public bool SmartMerge;

	public RoundTo Rounding;

	public bool ForceReprocessValidRasterFiles;

	public UnitSystem FallbackSourceUnits;

	public static TerrainRasterImportConfig Default()
	{
		return new TerrainRasterImportConfig
		{
			SmartMerge = true,
			Rounding = RoundTo.None,
			ForceReprocessValidRasterFiles = true,
			FallbackSourceUnits = UnitSystem.SI
		};
	}
}
