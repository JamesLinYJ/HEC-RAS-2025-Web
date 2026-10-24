using System;
using Geospatial.Rasters;

namespace Geospatial.Vectors.Rasterizers;

public class ExtentRasterizer
{
	private readonly RasterDefinition _raster;

	public ExtentRasterizer(RasterDefinition raster)
	{
		if (raster.IsNull())
		{
			throw new ArgumentNullException("raster");
		}
		_raster = raster;
	}

	public void Rasterize(Extent ext, Action<int> fillCellAction)
	{
		PixelRange pixelRange = _raster.GetPixelRange(ext, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
			{
				fillCellAction(_raster.CellIndex(i, j));
			}
		}
	}
}
