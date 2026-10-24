using System;
using System.Collections.Generic;

namespace Geospatial.Rasters;

public class BandedRasterData<T> where T : unmanaged, IEquatable<T>
{
	public readonly List<T[]> Bands;

	public readonly RasterDefinition Raster;

	public readonly PixelRange ValidRegion;

	public BandedRasterData(RasterDefinition rasterDef, List<T[]> bands)
	{
		Raster = rasterDef;
		Bands = bands;
		ValidRegion = rasterDef.FullRange();
	}

	public BandedRasterData(RasterDefinition rasterDef, List<T[]> bands, PixelRange validRegion)
	{
		Raster = rasterDef;
		Bands = bands;
		ValidRegion = validRegion;
	}
}
