using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Rendering.Editing;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Utility;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class ExtentRenderLayer : VectorRenderLayer
{
	private readonly IList<Extent> _extents;

	private readonly object _spIdxLock = new object();

	private SpatialIndex<int> _spIdx;

	public override Extent ValidExtent => Extent.Union(_extents);

	public override int Count
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public ExtentRenderLayer(IList<Extent> extents)
	{
		_extents = extents.ToList();
	}

	public override ISpatialVersion GetSpatialVersionContext()
	{
		return null;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		if (!ValidExtent.Overlaps(request.RasterQuery.Extent))
		{
			return false;
		}
		IEnumerable<int> enumerable = Enumerable.Range(0, _extents.Count);
		int cellCount = request.RasterQuery.CellCount;
		using IMemoryOwner<int> memoryOwner = SharedMemoryPool<int>.Rent(cellCount);
		Memory<int> clrBuffer = memoryOwner.Memory.Slice(0, cellCount);
		int clr = VectorSymbology.PenColor.ToArgb();
		bool anyPaint = false;
		if (VectorSymbology.PenWidth <= 1.0)
		{
			foreach (int enumerable2 in enumerable)
			{
				Extent extent = _extents[enumerable2];
				extent.TopWall.Rasterize(request.RasterQuery, (int idx) =>
				{
					anyPaint = true;
					clrBuffer.Span[idx] = clr;
				});
				extent.BottomWall.Rasterize(request.RasterQuery, (int idx) =>
				{
					anyPaint = true;
					clrBuffer.Span[idx] = clr;
				});
				extent.LeftWall.Rasterize(request.RasterQuery, (int idx) =>
				{
					anyPaint = true;
					clrBuffer.Span[idx] = clr;
				});
				extent.RightWall.Rasterize(request.RasterQuery, (int idx) =>
				{
					anyPaint = true;
					clrBuffer.Span[idx] = clr;
				});
			}
		}
		else
		{
			new PolygonRasterizer(request.RasterQuery);
			_ = VectorSymbology.PenWidth * request.RasterQuery.CellSize / 2.0;
			foreach (int enumerable3 in enumerable)
			{
				Extent extent2 = _extents[enumerable3];
				PolylineRenderLayer.Renderer.Render(request, Polygon.FromExtent(extent2), VectorSymbology);
			}
		}
		if (anyPaint)
		{
			request.Splat(clrBuffer);
			return true;
		}
		return false;
	}

	public override bool HitTest(int candidateIdx, Point pt, double hitDistance)
	{
		return _extents[candidateIdx].Contains(pt);
	}

	private SpatialIndex<int> ComputeSpatialIndex()
	{
		return SpatialIndex.FromExtents(_extents);
	}

	public override SpatialIndex<int> EnsureGetSpatialIndex()
	{
		return LazyThreadsafe<SpatialIndex<int>>.AllocateOnce(ref _spIdx, _spIdxLock, this, (ExtentRenderLayer t) => t.ComputeSpatialIndex());
	}

	public override bool Highlight(int idx, PlotRequest req)
	{
		return false;
	}

	public override bool HitTest(int candidateIdx, Extent ext)
	{
		throw new NotImplementedException();
	}

	public override double DistanceTo(int idx, Point pt)
	{
		return _extents[idx].Center.DistanceTo(pt);
	}

	public override bool HitTest(int candidateIdx, Polyline ext)
	{
		throw new NotImplementedException();
	}

	public override bool HitTest(int candidateIdx, Polygon lasso, MarqueeHitMode hitMode)
	{
		Extent ext = _extents[candidateIdx];
		if (hitMode != MarqueeHitMode.Contained)
		{
			return lasso.AreaIntersects(ext);
		}
		return lasso.Contains(ext);
	}
}
