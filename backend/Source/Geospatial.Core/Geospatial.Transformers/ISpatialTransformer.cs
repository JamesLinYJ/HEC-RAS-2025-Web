using System;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public interface ISpatialTransformer
{
	bool IsReversible { get; }

	void Transform(Memory<Point> points, Memory<Point> transformed);

	void ReverseTransform(Memory<Point> points, Memory<Point> transformed);

	Extent TransformExtentRasterized(Extent srcExtent)
	{
		RasterDefinition rasterDefinition = RasterDefinition.ComputeBoundingRaster(srcExtent, 1000);
		Point[] points = new Point[rasterDefinition.CellCount];
		for (int i = 0; i < points.Length; i++)
		{
			points[i] = rasterDefinition.CellCenter(i);
		}
		Transform(points, points);
		return PointCollection.ComputeValidExtent(points);
	}
}
