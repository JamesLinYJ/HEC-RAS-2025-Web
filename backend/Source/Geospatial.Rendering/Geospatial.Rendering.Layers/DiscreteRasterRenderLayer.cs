using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class DiscreteRasterRenderLayer : RenderLayer
{
	private readonly IResample<int> _raster;

	private ColorMap<int> _map = new ColorMap<int>(0);

	public Func<UniqueClassificationMap> GetNameIDMap { get; init; }

	public bool MapHoverDisplayNoDataText { get; set; }

	public ColorMap<int> ColorMap
	{
		get
		{
			return _map;
		}
		set
		{
			_map = value;
			IncrementRenderState();
		}
	}

	public override Extent ValidExtent => _raster.ValidExtent;

	public int NoDataValue { get; }

	public DiscreteRasterRenderLayer(IResample<int> raster)
	{
		if (raster == null)
		{
			throw new ArgumentNullException("raster");
		}
		_raster = raster;
		NoDataValue = raster.NoDataValue.GetValueOrDefault();
		_map = new ColorMap<int>(NoDataValue);
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		if (plot == null)
		{
			return false;
		}
		if (!plot.RasterQuery.Overlaps(_raster.ValidExtent))
		{
			return false;
		}
		int cellCount = plot.RasterQuery.CellCount;
		Memory<int> mem;
		using (SharedMemoryPool<int>.RentNoFill(cellCount, out mem))
		{
			ResampleArgs args = new ResampleArgs(plot.RasterQuery, InterpolationMode.Nearest);
			_raster.Resample(args, mem);
			Memory<int> mem2;
			using (SharedMemoryPool<int>.RentNoFill(cellCount, out mem2))
			{
				_map.ParallelPaint(mem, mem2);
				plot.Splat(mem2);
				return true;
			}
		}
	}

	public override List<MapHover> MapHover(HitTestArgs args, ref object hitTestCache)
	{
		UniqueClassificationMap uniqueClassificationMap = GetNameIDMap?.Invoke();
		int samplePoint = _raster.SamplePoint(args.QueryPoint);
		if (samplePoint == NoDataValue)
		{
			if (MapHoverDisplayNoDataText)
			{
				MapHover mapHover = new MapHover(args.QueryPoint, "(No Data)");
				return new List<MapHover> { mapHover };
			}
			return null;
		}
		if (uniqueClassificationMap == null)
		{
			MapHover mapHover2 = new MapHover(args.QueryPoint, samplePoint.ToString());
			return new List<MapHover> { mapHover2 };
		}
		if (uniqueClassificationMap.TryGetName(samplePoint, out var className))
		{
			MapHover mapHover3 = new MapHover(args.QueryPoint, className);
			return new List<MapHover> { mapHover3 };
		}
		MapHover mapHover4 = new MapHover(args.QueryPoint, "(Missing Data)");
		return new List<MapHover> { mapHover4 };
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + GetRasterRenderState();
	}

	private long GetRasterRenderState()
	{
		return (_raster as ISpatialVersion)?.VersionXY ?? 0;
	}
}
