using System.Collections.Generic;
using Geospatial.DataStructures;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers;

public class SpatialIndexRenderLayer : PolygonRenderLayer
{
	public static List<Polygon> BuildPolygons(SpatialIndex spIdx)
	{
		List<Polygon> cellCount = new List<Polygon>(spIdx.Raster.CellCount);
		for (int i = 0; i < spIdx.Raster.CellCount; i++)
		{
			cellCount.Add(Polygon.FromExtent(spIdx.Raster.CellExtent(i)));
		}
		return cellCount;
	}

	public SpatialIndexRenderLayer(SpatialIndex spIdx)
		: base(BuildPolygons(spIdx))
	{
		RenderFill = false;
	}
}
