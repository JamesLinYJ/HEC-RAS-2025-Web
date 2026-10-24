using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing;
using Geospatial.Vectors;
using Utility;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class RasterDefinitionRenderLayer : VectorRenderLayer
{
	private readonly IList<RasterDefinition> _rasterDefs;

	private readonly object _spIdxLock = new object();

	private SpatialIndex<int> _spIdx;

	public override Extent ValidExtent => Extent.Union(_rasterDefs.Select((RasterDefinition r) => r.Extent));

	public double PixelsPerCellRenderCutoff { get; set; } = 5.0;

	public override int Count => _rasterDefs.Count;

	public RasterDefinitionRenderLayer(RasterDefinition raster)
	{
		_rasterDefs = new List<RasterDefinition>();
		_rasterDefs.Add(raster);
	}

	public RasterDefinitionRenderLayer(IList<RasterDefinition> rasters)
	{
		_rasterDefs = rasters.ToList();
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
		IEnumerable<int> enumerable = Enumerable.Range(0, _rasterDefs.Count);
		Memory<int> clrBuf;
		using (SharedMemoryPool<int>.Rent(request.RasterQuery.CellCount, out clrBuf))
		{
			int clr = VectorSymbology.PenColor.ToArgb();
			bool anyPaint = false;
			if (VectorSymbology.PenWidth <= 1.0)
			{
				foreach (int enumerable2 in enumerable)
				{
					RasterDefinition rasterDefinition = _rasterDefs[enumerable2];
					PixelRange pixelRange = rasterDefinition.GetPixelRange(request.RasterQuery.Extent);
					if (pixelRange.IsNull() || rasterDefinition.CellSize / request.RasterQuery.CellSize <= PixelsPerCellRenderCutoff)
					{
						continue;
					}
					double x = rasterDefinition.CellMinX(pixelRange.MinCol);
					double x2 = rasterDefinition.CellMaxX(pixelRange.MaxCol);
					double y = rasterDefinition.CellMaxY(pixelRange.MinRow);
					double y2 = rasterDefinition.CellMinY(pixelRange.MaxRow);
					for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow + 1; i++)
					{
						double y3 = rasterDefinition.CellMaxY(i);
						new Segment(new Point(x, y3), new Point(x2, y3)).Rasterize(request.RasterQuery, (int idx) =>
						{
							anyPaint = true;
							clrBuf.Span[idx] = clr;
						});
					}
					for (int minCol = pixelRange.MinCol; minCol <= pixelRange.MaxCol + 1; minCol++)
					{
						double x3 = rasterDefinition.CellMinX(minCol);
						new Segment(new Point(x3, y), new Point(x3, y2)).Rasterize(request.RasterQuery, (int idx) =>
						{
							anyPaint = true;
							clrBuf.Span[idx] = clr;
						});
					}
				}
				if (anyPaint)
				{
					request.Splat(clrBuf);
				}
				return anyPaint;
			}
			throw new NotImplementedException();
		}
	}

	public override bool Highlight(int idx, PlotRequest req)
	{
		return false;
	}

	public override bool HitTest(int candidateIdx, Point pt, double hitDistance)
	{
		return _rasterDefs[candidateIdx].Extent.Contains(pt);
	}

	public override bool HitTest(int candidateIdx, Polyline ext)
	{
		return Polygon.FromExtent(_rasterDefs[candidateIdx].Extent).ContainsOrIntersects(ext);
	}

	public override bool HitTest(int candidateIdx, Polygon lasso, MarqueeHitMode hitMode)
	{
		Polygon pgon = Polygon.FromExtent(_rasterDefs[candidateIdx].Extent);
		if (hitMode == MarqueeHitMode.Contained)
		{
			return lasso.Contains(pgon);
		}
		return lasso.AreaIntersects(pgon);
	}

	public override double DistanceTo(int idx, Point pt)
	{
		return _rasterDefs[idx].Center.DistanceTo(pt);
	}

	private SpatialIndex<int> ComputeSpatialIndex()
	{
		return SpatialIndex.FromExtents(_rasterDefs.Select((RasterDefinition rd) => rd.Extent).ToList());
	}

	public override SpatialIndex<int> EnsureGetSpatialIndex()
	{
		return LazyThreadsafe<SpatialIndex<int>>.AllocateOnce(ref _spIdx, _spIdxLock, this, (RasterDefinitionRenderLayer t) => t.ComputeSpatialIndex());
	}

	public override bool HitTest(int candidateIdx, Extent ext)
	{
		throw new NotImplementedException();
	}
}
