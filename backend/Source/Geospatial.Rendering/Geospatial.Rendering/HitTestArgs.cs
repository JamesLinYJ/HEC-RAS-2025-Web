using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Rendering;

public class HitTestArgs
{
	public Point QueryPoint { get; set; }

	public RasterDefinition Window { get; set; }

	public Extent HitBox(int pixelRadius)
	{
		return Extent.CenteredOn(QueryPoint, Window.CellSize * (double)pixelRadius * 2.0);
	}

	public HitTestArgs(Point pt, RasterDefinition window)
	{
		QueryPoint = pt;
		Window = window;
	}

	public static HitTestArgs Generate(Point centerPt, double cellSize, int nPixelsPerDimension = 100)
	{
		double cellSizeNPixelsPerDimensionTerm = cellSize * (double)nPixelsPerDimension / 2.0;
		return new HitTestArgs(window: new RasterDefinition(centerPt.Translate(0.0 - cellSizeNPixelsPerDimensionTerm, cellSizeNPixelsPerDimensionTerm), cellSize, nPixelsPerDimension, nPixelsPerDimension), pt: centerPt);
	}
}
