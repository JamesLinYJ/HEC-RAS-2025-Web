using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Geospatial.Vectors.Polylines;
using Geospatial.Vectors.Rasterizers;
using Utility.Memory;

namespace Geospatial.Rendering.Systems.Rendering;

public class CPUPolylineRenderer : IPolylineRenderer
{
	private static Pool<Polygon> scratchSpacePolygons = new Pool<Polygon>(() => new Polygon(new Point[4]));

	private static Pool<PolygonRasterizer> scratchSpaceRasterizers = new Pool<PolygonRasterizer>(() => PolygonRasterizer.MakeUninitializedPolygonRasterizer());

	private static bool Render_Internal(in Segment seg, Extent ext, double distance, Polygon pgon, PolygonRasterizer rasterizer, Memory<int> clrBuf, int clr)
	{
		if (!ext.Contains(seg.Extent))
		{
			return false;
		}
		Segment segment = seg.Offset(distance);
		Segment segment2 = seg.Offset(0.0 - distance);
		pgon[0] = segment.A;
		pgon[1] = segment.B;
		pgon[2] = segment2.B;
		pgon[3] = segment2.A;
		return rasterizer.Rasterize(pgon, (clrBuf, clr), (int index, (Memory<int>, int) payload) =>
		{
			payload.Item1.Span[index] = payload.Item2;
		});
	}

	public bool Render<T, K>(PlotRequest request, K model, Func<int, K, Polyline> getPolyline, IList<FeatureSegmentSubset> candidates, VectorSymbology symbology) where T : Polyline
	{
		if (symbology is IConditionalVectorSymbology { HasAnyOverrides: not false })
		{
			if (symbology is ConditionalVectorSymbology<ConditionalVectorSymbology.Unit> symbology2)
			{
				return RenderConditional_Internal(request, model, getPolyline, candidates, ConditionalVectorSymbology.Unit.Value, symbology2);
			}
			if (symbology is ConditionalVectorSymbology<K> symbology3)
			{
				return RenderConditional_Internal(request, model, getPolyline, candidates, model, symbology3);
			}
			throw new InvalidOperationException("Model type does not match conditional vector symbology type.");
		}
		return RenderUniform_Internal<T, K>(request, model, getPolyline, candidates, symbology);
	}

	private bool RenderUniform_Internal<T, K>(PlotRequest request, K model, Func<int, K, Polyline> getPolyline, IList<FeatureSegmentSubset> candidates, VectorSymbology symbology) where T : Polyline
	{
		if (symbology.PenWidth * VectorManagerOptions.RenderScaling <= 1.0)
		{
			return RenderSegments(request, getPolyline, model, candidates, symbology.PenColor.ToArgb());
		}
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(request.RasterQuery.CellCount, out mem))
		{
			int clr = symbology.PenColor.ToArgb();
			bool condition = false;
			PoolHandle<Polygon> poolHandle = scratchSpacePolygons.Acquire();
			PoolHandle<PolygonRasterizer> poolHandle2 = scratchSpaceRasterizers.Acquire();
			Polygon data = poolHandle.Data;
			PolygonRasterizer data2 = poolHandle2.Data;
			data2.Reset(request.RasterQuery);
			double distance = symbology.PenWidth * VectorManagerOptions.RenderScaling * request.RasterQuery.CellSize / 2.0;
			Extent extent = request.RasterQuery.Extent;
			for (int i = 0; i < candidates.Count; i++)
			{
				FeatureSegmentSubset featureSegmentSubset = candidates[i];
				Polyline polyline = getPolyline(featureSegmentSubset.Fid, model);
				bool polylineWithPolygon = polyline is Polygon;
				FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(polyline, isClosed: false);
				for (int j = 0; j < featureSegmentSubset.Ranges.Count; j++)
				{
					Range1D range1D = featureSegmentSubset.Ranges[j];
					bool endRange1DTerm = polylineWithPolygon && range1D.End == fastSegmentIterator.CountWhenOpen;
					int end = (endRange1DTerm ? (range1D.End - 1) : range1D.End);
					for (int k = range1D.Start; k <= end; k++)
					{
						condition |= Render_Internal(in fastSegmentIterator[k], extent, distance, data, data2, mem, clr);
					}
					if (endRange1DTerm)
					{
						condition |= Render_Internal(fastSegmentIterator.GetClosingSegment(), extent, distance, data, data2, mem, clr);
					}
				}
			}
			if (condition)
			{
				request.Splat(mem);
			}
			scratchSpacePolygons.Return(poolHandle);
			scratchSpaceRasterizers.Return(poolHandle2);
			return condition;
		}
	}

	private bool RenderConditional_Internal<T, K>(PlotRequest request, T model, Func<int, T, Polyline> getPolyline, IList<FeatureSegmentSubset> candidates, K symbModel, ConditionalVectorSymbology<K> symbology)
	{
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(request.RasterQuery.CellCount, out mem))
		{
			bool condition = false;
			PoolHandle<Polygon> poolHandle = scratchSpacePolygons.Acquire();
			PoolHandle<PolygonRasterizer> poolHandle2 = scratchSpaceRasterizers.Acquire();
			Polygon data = poolHandle.Data;
			PolygonRasterizer data2 = poolHandle2.Data;
			data2.Reset(request.RasterQuery);
			Extent extent = request.RasterQuery.Extent;
			for (int i = 0; i < candidates.Count; i++)
			{
				FeatureSegmentSubset featureSegmentSubset = candidates[i];
				int clr = symbology.GetPenColorDynamic(symbModel, featureSegmentSubset.Fid).ToArgb();
				double distance = symbology.GetPenWidthDynamic(symbModel, featureSegmentSubset.Fid) * VectorManagerOptions.RenderScaling * request.RasterQuery.CellSize / 2.0;
				Polyline polyline = getPolyline(featureSegmentSubset.Fid, model);
				bool polylineWithPolygon = polyline is Polygon;
				FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(polyline, isClosed: false);
				for (int j = 0; j < featureSegmentSubset.Ranges.Count; j++)
				{
					Range1D range1D = featureSegmentSubset.Ranges[j];
					bool endRange1DTerm = polylineWithPolygon && range1D.End == fastSegmentIterator.CountWhenOpen;
					int end = (endRange1DTerm ? (range1D.End - 1) : range1D.End);
					for (int k = range1D.Start; k <= end; k++)
					{
						condition |= Render_Internal(in fastSegmentIterator[k], extent, distance, data, data2, mem, clr);
					}
					if (endRange1DTerm)
					{
						condition |= Render_Internal(fastSegmentIterator.GetClosingSegment(), extent, distance, data, data2, mem, clr);
					}
				}
			}
			if (condition)
			{
				request.Splat(mem);
			}
			scratchSpacePolygons.Return(poolHandle);
			scratchSpaceRasterizers.Return(poolHandle2);
			return condition;
		}
	}

	public bool RenderSegments<K>(PlotRequest request, Func<int, K, Polyline> getPolyline, K model, IList<FeatureSegmentSubset> candidates, int clr)
	{
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(request.RasterQuery.CellCount, out mem))
		{
			bool condition = false;
			Extent extent = request.RasterQuery.Extent;
			for (int i = 0; i < candidates.Count; i++)
			{
				FeatureSegmentSubset featureSegmentSubset = candidates[i];
				Polyline polyline = getPolyline(featureSegmentSubset.Fid, model);
				bool polylineWithPolygon = polyline is Polygon;
				FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(polyline, isClosed: false);
				for (int j = 0; j < featureSegmentSubset.Ranges.Count; j++)
				{
					Range1D range1D = featureSegmentSubset.Ranges[j];
					bool endRange1DTerm = polylineWithPolygon && range1D.End == fastSegmentIterator.CountWhenOpen;
					int end = (endRange1DTerm ? (range1D.End - 1) : range1D.End);
					for (int k = range1D.Start; k <= end; k++)
					{
						Segment other = fastSegmentIterator[k];
						if (extent.Overlaps(in other))
						{
							condition = true;
							other.Rasterize(request.RasterQuery, (mem, clr), (int index, (Memory<int>, int) payload) =>
							{
								payload.Item1.Span[index] = payload.Item2;
							});
						}
					}
					if (!endRange1DTerm)
					{
						continue;
					}
					Segment other2 = fastSegmentIterator.GetClosingSegment();
					if (extent.Overlaps(in other2))
					{
						condition = true;
						other2.Rasterize(request.RasterQuery, (mem, clr), (int index, (Memory<int>, int) payload) =>
						{
							payload.Item1.Span[index] = payload.Item2;
						});
					}
				}
			}
			if (condition)
			{
				request.Splat(mem);
			}
			return condition;
		}
	}

	public bool Render(PlotRequest request, IReadOnlyList<Segment> segments, VectorSymbology symbology)
	{
		if (symbology.PenWidth * VectorManagerOptions.RenderScaling > 1.0)
		{
			Memory<int> thickLineMemory;
			using (SharedMemoryPool<int>.Rent(request.RasterQuery.CellCount, 0, out thickLineMemory))
			{
				bool renderedAnySegment = false;
				PoolHandle<Polygon> polygonHandle = scratchSpacePolygons.Acquire();
				PoolHandle<PolygonRasterizer> rasterizerHandle = scratchSpaceRasterizers.Acquire();
				try
				{
					Polygon polygon = polygonHandle.Data;
					PolygonRasterizer rasterizer = rasterizerHandle.Data;
					rasterizer.Reset(request.RasterQuery);
					double distance = symbology.PenWidth * VectorManagerOptions.RenderScaling
						* request.RasterQuery.CellSize / 2.0;
					Extent extent = request.RasterQuery.Extent;
					int color = symbology.PenColor.ToArgb();
					foreach (Segment segment in segments)
						renderedAnySegment |= Render_Internal(in segment, extent, distance, polygon,
							rasterizer, thickLineMemory, color);
					if (renderedAnySegment)
						request.Splat(thickLineMemory);
					return renderedAnySegment;
				}
				finally
				{
					scratchSpacePolygons.Return(polygonHandle);
					scratchSpaceRasterizers.Return(rasterizerHandle);
				}
			}
		}

		int cellCount = request.RasterQuery.CellCount;
		bool condition = false;
		int toArgb = symbology.PenColor.ToArgb();
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(cellCount, 0, out mem))
		{
			(Memory<int>, int) t = (mem, toArgb);
			foreach (Segment segment in segments)
			{
				condition |= segment.Rasterize(request.RasterQuery, t, RENDER);
			}
			if (condition)
			{
				request.Splat(mem);
				return true;
			}
			return condition;
		}
		static void RENDER(int idx, (Memory<int>, int) payload)
		{
			payload.Item1.Span[idx] = payload.Item2;
		}
	}
}
