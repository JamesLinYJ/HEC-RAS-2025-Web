using System;
using System.Buffers;
using System.Drawing;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Ras.Layers;
using Ras.Mapping;
using Utility.Memory;

namespace Ras.Rendering;

public class PaintGeometryRenderLayer : RenderLayer
{
	private readonly Geometry _geom;

	public override Extent ValidExtent => _geom?.Extent ?? Extent.NullExtent;

	public PaintGeometryRenderLayer(Geometry geom)
	{
		_geom = geom;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		if (!request.RasterQuery.Extent.Overlaps(ValidExtent))
		{
			return false;
		}
		GeometryMapPoints geometryMapPoints = new GeometryMapPoints(_geom, request.RasterQuery);
		int toARGB = Color.DarkGreen.ToARGB();
		int cellCount = request.RasterQuery.CellCount;
		using IMemoryOwner<int> memoryOwner = SharedMemoryPool<int>.Rent(cellCount);
		Memory<int> bufARGB = memoryOwner.Memory.Slice(0, cellCount);
		Span<int> bufARGBSpan = bufARGB.Span;
		bool condition = false;
		foreach (SlopingMeshMap slopingMapped2DArea in geometryMapPoints.SlopingMapped2DAreas)
		{
			for (int i = 0; i < slopingMapped2DArea.Cells.Count; i++)
			{
				Span<PointRange> pixelRanges = slopingMapped2DArea.Cells[i].PixelRanges.Span;
				for (int j = 0; j < pixelRanges.Length; j++)
				{
					PointRange pointRange = pixelRanges[j];
					for (int k = pointRange.QueryStartIndex; k <= pointRange.QueryEndIndex; k++)
					{
						bufARGBSpan[k] = toARGB;
					}
					condition = true;
				}
			}
		}
		foreach (FlatMeshMap flatMapped2DArea in geometryMapPoints.FlatMapped2DAreas)
		{
			for (int l = 0; l < flatMapped2DArea.Cells.Count; l++)
			{
				FlatCellMap flatCellMap = flatMapped2DArea.Cells[l];
				for (int m = 0; m < flatCellMap.PointIndexes.Count; m++)
				{
					bufARGBSpan[flatCellMap.PointIndexes[m]] = toARGB;
					condition = true;
				}
			}
		}
		if (condition)
		{
			request.Splat(bufARGB);
			return true;
		}
		return false;
	}
}
