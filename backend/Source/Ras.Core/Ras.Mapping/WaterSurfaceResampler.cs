using System;
using System.Buffers;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Ras.Output;
using Utility.Logging;
using Utility.Memory;

namespace Ras.Mapping;

public class WaterSurfaceResampler : IMultiBandResampler<float>, ISurface<float>
{
	private readonly IHorizontalOutputBlock _ob;

	private readonly IResample<float> _terrain;

	private readonly WaterSurfaceMapper _renderer;

	private readonly ILogger _logger = LogHelper.GetLogger("WaterSurfaceResampler");

	private readonly LogAggregator _logAggregator;

	public Extent ValidExtent { get; }

	public float? NoDataValue => -9999f;

	public int ProfileCount => _ob.ProfileCount;

	public Func<RasterDefinition, GeometryMapPoints> OverrideGetMapPoints { get; set; }

	public WaterSurfaceResampler(IHorizontalOutputBlock ob, IResample<float> terrain, MeshPropertyTables[] tables)
	{
		_ob = ob;
		_terrain = terrain;
		_renderer = new WaterSurfaceMapper(ob, tables);
		ValidExtent = _ob.Geometry.Extent;
		_logAggregator = GeometryMapPoints.CreateDefaultRasterTimings(_logger);
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<float> outData, ResampleCache cache = null)
	{
		GeometryMapPoints geometryMapPoints;
		if (OverrideGetMapPoints == null)
		{
			geometryMapPoints = new GeometryMapPoints(_ob.Geometry, args.Raster);
		}
		else
		{
			geometryMapPoints = OverrideGetMapPoints(args.Raster);
			if (geometryMapPoints == null)
			{
				geometryMapPoints = new GeometryMapPoints(_ob.Geometry, args.Raster);
			}
		}
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(args.Raster.CellCount, out mem))
		{
			_terrain.Resample(args, mem);
			_renderer.Compute(profileIndex, geometryMapPoints, mem, outData);
		}
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		this.Resample_NaiiveLoop(args, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] result)
	{
		if (result.Length != bandCount)
		{
			throw new ArgumentException();
		}
		GeometryMapPoints mapPts = new GeometryMapPoints(_ob.Geometry, points);
		int length = points.Length;
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(length);
		Memory<float> slice = memoryOwner.Memory.Slice(0, length);
		_terrain.SamplePoints(points, slice);
		_ = points.Length;
		for (int i = 0; i < bandCount; i++)
		{
			int pfIdx = i + bandStart;
			_renderer.Compute(pfIdx, mapPts, slice, result[i]);
		}
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<float> result)
	{
		if (result.Length != points.Length)
		{
			throw new ArgumentException();
		}
		GeometryMapPoints mapPts = new GeometryMapPoints(_ob.Geometry, points);
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(points.Length, out mem))
		{
			_terrain.SamplePoints(points, mem);
			_renderer.Compute(profileIndex, mapPts, mem, result);
		}
	}
}
