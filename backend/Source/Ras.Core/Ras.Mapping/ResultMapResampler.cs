using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Ras.Layers;
using Ras.Output;
using Utility.Logging;
using Utility.Memory;

namespace Ras.Mapping;

public class ResultMapResampler : ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps, IDiscreteTimestepsWithEvent
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ResultMapResampler");

	private readonly IHorizontalOutputBlock _ob;

	private readonly IResample<float> _terrain;

	private readonly Mapper _mapper;

	private Func<GeometrySamplingParameters> _samplingParamsProvider;

	private GeometrySamplingParameters SamplingParams => _samplingParamsProvider?.Invoke() ?? GeometrySamplingParameters.Default;

	public Extent ValidExtent => _ob.Geometry.HydraulicExtent;

	public float? NoDataValue => -9999f;

	public int ProfileCount => _ob.ProfileCount;

	public Mapper Mapper => _mapper;

	public bool CanInterpolateTimesteps => false;

	public IReadOnlyList<DateTime> Times => _ob.Times;

	public bool ReplaceNoDataWithChartFriendlyValues { get; init; }

	public event TimestepAddedEvent TimestepAdded
	{
		add
		{
			_ob.TimestepAdded += value;
		}
		remove
		{
			_ob.TimestepAdded -= value;
		}
	}

	public ResultMapResampler(MapTypes mt, IHorizontalOutputBlock ob, IResample<float> terrain, MeshPropertyTables[] tables)
	{
		_ob = ob;
		_terrain = terrain;
		_mapper = Mapper.CreateMapper(mt, ob, tables);
	}

	public ResultMapResampler(Result res, MapTypes mt)
		: this(mt, res.BaseOutputBlock, res.AssociatedTerrain, res.Geometry.FlowAreaLayer.AllPropertyTables())
	{
	}

	public ResultMapResampler(Result res, MapTypes mt, Func<GeometrySamplingParameters> samplingParamsProvider)
		: this(mt, res.BaseOutputBlock, res.AssociatedTerrain, res.Geometry.FlowAreaLayer.AllPropertyTables())
	{
		_samplingParamsProvider = samplingParamsProvider;
	}

	public ResultMapResampler(Mapper mapper, IResample<float> terrain, Func<GeometrySamplingParameters> samplingParamsProvider = null)
	{
		_ob = mapper.OutputBlock;
		_terrain = terrain;
		_mapper = mapper;
		_samplingParamsProvider = samplingParamsProvider;
	}

	public void EnableStoredMapSettings()
	{
		_samplingParamsProvider = () => GeometrySamplingParameters.StoringToDisk;
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<float> outData, ResampleCache cache = null)
	{
		if (cache != null && cache.SpatialCache != null)
		{
			Tuple<GeometryMapPoints, Memory<float>> spatialCache = cache.SpatialCache as Tuple<GeometryMapPoints, Memory<float>>;
			GeometryMapPoints geometryMapPoints2 = spatialCache.Item1;
			Memory<float> spatialCacheTerm = spatialCache.Item2;
			_mapper.Compute(profileIndex, geometryMapPoints2, spatialCacheTerm, outData);
			return;
		}
		GeometryMapPoints geometryMapPoints = new GeometryMapPoints(_ob.Geometry, args.Raster, SamplingParams);
		IMemoryOwner<float> memoryOwner = null;
		try
		{
			memoryOwner = SharedMemoryPool<float>.Rent(args.Raster.CellCount, -9999f, out var mem);
			_terrain.Resample(args, mem);
			_mapper.Compute(profileIndex, geometryMapPoints, mem, outData);
			if (ReplaceNoDataWithChartFriendlyValues)
			{
				CleanResult(outData, mem);
			}
			if (cache != null)
			{
				cache.SpatialCache = new Tuple<GeometryMapPoints, Memory<float>>(geometryMapPoints, mem);
				cache.TrackDisposable(memoryOwner);
			}
		}
		finally
		{
			if (cache == null)
			{
				memoryOwner.Dispose();
			}
		}
	}

	private void CleanResult(Memory<float> mapValues, Memory<float> terrain)
	{
		if (_mapper.MapType == MapTypes.WaterSurfaceElevation)
		{
			Span<float> mapValuesSpan = mapValues.Span;
			Span<float> terrainSpan = terrain.Span;
			for (int i = 0; i < mapValuesSpan.Length; i++)
			{
				if (mapValuesSpan[i] == -9999f)
				{
					mapValuesSpan[i] = terrainSpan[i];
				}
			}
		}
		else
		{
			if (_mapper.MapType != MapTypes.Depth && _mapper.MapType != MapTypes.Velocity)
			{
				return;
			}
			Span<float> mapValues2 = mapValues.Span;
			for (int j = 0; j < mapValues2.Length; j++)
			{
				if (mapValues2[j] == -9999f)
				{
					mapValues2[j] = 0f;
				}
			}
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
		GeometryMapPoints mapPts = new GeometryMapPoints(_ob.Geometry, points, SamplingParams);
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(points.Length, -9999f, out mem))
		{
			_terrain.SamplePoints(points, mem);
			for (int i = 0; i < bandCount; i++)
			{
				int pfIdx = i + bandStart;
				_mapper.Compute(pfIdx, mapPts, mem, result[i]);
				if (ReplaceNoDataWithChartFriendlyValues)
				{
					CleanResult(result[i], mem);
				}
			}
		}
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<float> result)
	{
		if (result.Length != points.Length)
		{
			throw new ArgumentException();
		}
		GeometryMapPoints mapPts = new GeometryMapPoints(_ob.Geometry, points, SamplingParams);
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(points.Length, -9999f, out mem))
		{
			_terrain.SamplePoints(points, mem);
			_mapper.Compute(profileIndex, mapPts, mem, result);
			if (ReplaceNoDataWithChartFriendlyValues)
			{
				CleanResult(result, mem);
			}
		}
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<float> outData, ResampleCache cache = null)
	{
		throw new NotImplementedException();
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		throw new NotImplementedException();
	}
}
