using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class EqualDefinitionRasterSelector : IRasterSelector
{
	public float PolygonOversampleMaxRatio { get; } = 10f;

	public int ApproximateResamplePixelCount { get; }

	public EqualDefinitionRasterSelector(int resamplePxCt)
	{
		ApproximateResamplePixelCount = resamplePxCt;
	}

	public RasterDefinition GetAppropriateResampleRaster(Extent ext)
	{
		return RasterDefinition.ComputeBoundingRaster(ext, ApproximateResamplePixelCount);
	}

	public RasterDefinition GetAppropriateResampleRaster(Polygon poly)
	{
		double absoluteArea = Math.Abs(poly.Area);
		Extent extent = poly.Extent;
		double areaOverAbsoluteArea = extent.Area / absoluteArea;
		areaOverAbsoluteArea = Math.Min(areaOverAbsoluteArea, PolygonOversampleMaxRatio);
		return RasterDefinition.ComputeBoundingRaster(extent, (int)((double)ApproximateResamplePixelCount * areaOverAbsoluteArea));
	}
}
