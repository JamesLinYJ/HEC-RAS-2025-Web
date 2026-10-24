using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;

namespace Geospatial.Rasters;

public class PolygonFillRasterizer : IRaster<float>, IResample<float>, ISurface<float>, IRasterDefinition
{
	private readonly RasterDefinition _rasterDefinition;

	private readonly List<Polygon> _polygons;

	private readonly float _fillValue;

	private readonly float _backgroundValue;

	public RasterDefinition RasterDefinition => _rasterDefinition;

	public Extent ValidExtent => _rasterDefinition.Extent;

	public float? NoDataValue => null;

	public PolygonFillRasterizer(RasterDefinition rasterDefinition, List<Polygon> polygons, float fillValue, float backgroundValue)
	{
		_rasterDefinition = rasterDefinition;
		_polygons = polygons;
		_fillValue = fillValue;
		_backgroundValue = backgroundValue;
	}

	public void GetRange(PixelRange range, Memory<float> outData)
	{
		RasterDefinition subRaster = _rasterDefinition.GetSubRaster(range);
		Resample(new ResampleArgs(subRaster), outData);
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		RasterDefinition raster = args.Raster;
		outData.Span.Fill(_backgroundValue);
		Extent other = raster.Extent;
		PolygonRasterizer polygonRasterizer = new PolygonRasterizer(raster);
		foreach (Polygon polygon in _polygons)
		{
			if (polygon == null || polygon.Count < 3 || !polygon.Extent.Overlaps(in other))
			{
				continue;
			}
			polygonRasterizer.Rasterize(polygon, (outData, _fillValue, raster), (int idx, (Memory<float> outData, float _fillValue, RasterDefinition queryRaster) payload) =>
			{
				if (idx >= 0 && idx < payload.queryRaster.CellCount)
				{
					payload.outData.Span[idx] = payload._fillValue;
				}
			});
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		outData.Span.Fill(_backgroundValue);
	}
}
