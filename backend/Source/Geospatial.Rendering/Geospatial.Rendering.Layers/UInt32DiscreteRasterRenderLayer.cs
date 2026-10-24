using System;
using System.Collections.Generic;
using System.Globalization;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

/// <summary>Renders UInt32 categories without narrowing their source keys.</summary>
public class UInt32DiscreteRasterRenderLayer : RenderLayer
{
	private readonly IResample<uint> _raster;

	private ColorMap<uint> _map = new ColorMap<uint>(0u);

	/// <summary>Optionally resolves a UInt32 category key to its display name.</summary>
	public Func<uint, string> ResolveName { get; init; }

	public bool MapHoverDisplayNoDataText { get; set; }

	public ColorMap<uint> ColorMap
	{
		get => _map;
		set
		{
			_map = value;
			IncrementRenderState();
		}
	}

	public override Extent ValidExtent => _raster.ValidExtent;

	public uint NoDataValue { get; }

	public UInt32DiscreteRasterRenderLayer(IResample<uint> raster)
	{
		if (raster == null)
		{
			throw new ArgumentNullException(nameof(raster));
		}

		_raster = raster;
		NoDataValue = raster.NoDataValue.GetValueOrDefault();
		_map = new ColorMap<uint>(NoDataValue);
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		if (plot == null || !plot.RasterQuery.Overlaps(_raster.ValidExtent))
		{
			return false;
		}

		int cellCount = plot.RasterQuery.CellCount;
		Memory<uint> values;
		using (SharedMemoryPool<uint>.RentNoFill(cellCount, out values))
		{
			ResampleArgs args = new ResampleArgs(plot.RasterQuery, InterpolationMode.Nearest);
			_raster.Resample(args, values);
			Memory<int> colors;
			using (SharedMemoryPool<int>.RentNoFill(cellCount, out colors))
			{
				_map.ParallelPaint(values, colors);
				plot.Splat(colors);
				return true;
			}
		}
	}

	public override List<MapHover> MapHover(HitTestArgs args, ref object hitTestCache)
	{
		uint samplePoint = _raster.SamplePoint(args.QueryPoint);
		if (samplePoint == NoDataValue)
		{
			if (MapHoverDisplayNoDataText)
			{
				return new List<MapHover> { new MapHover(args.QueryPoint, "(No Data)") };
			}
			return null;
		}

		string resolvedName = ResolveName?.Invoke(samplePoint);
		string displayValue = string.IsNullOrWhiteSpace(resolvedName)
			? samplePoint.ToString(CultureInfo.InvariantCulture)
			: resolvedName;
		return new List<MapHover> { new MapHover(args.QueryPoint, displayValue) };
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + ((_raster as ISpatialVersion)?.VersionXY ?? 0);
	}
}
