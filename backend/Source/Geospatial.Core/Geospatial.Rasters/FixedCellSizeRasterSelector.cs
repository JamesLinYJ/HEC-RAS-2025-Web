using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class FixedCellSizeRasterSelector : IRasterSelector
{
	public double CellSize { get; set; }

	public FixedCellSizeRasterSelector(double cs)
	{
		CellSize = cs;
	}

	public RasterDefinition GetAppropriateResampleRaster(Extent ext)
	{
		return new RasterDefinition(ext, CellSize);
	}

	public RasterDefinition GetAppropriateResampleRaster(Polygon poly)
	{
		return GetAppropriateResampleRaster(poly.Extent);
	}
}
