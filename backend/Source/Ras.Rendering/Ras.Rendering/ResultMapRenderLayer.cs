using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Systems;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Ras.Helpers;
using Ras.Hydraulics;
using Ras.Mapping;
using Ras.Output;
using Utility;
using Utility.Attributes;
using Utility.Interfaces;
using Utility.Logging;
using Utility.Memory;
using Utility.Units;

namespace Ras.Rendering;

public class ResultMapRenderLayer : RenderLayer, IBandedRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IProfileStateProvider, IRasterRenderLayer
{
	public enum VelocityDisplayMode
	{
		[EnumDisplayName("Magnitude (Default)")]
		Magnitude,
		[EnumDisplayName("Vx Component")]
		Vx,
		[EnumDisplayName("Vy Component")]
		Vy
	}

	private class MapCache : IDisposable
	{
		public GeometryMapPoints MapPoints;

		public IMemoryOwner<float> TerrainDataOwner;

		public bool ForceHorizontal;

		public Memory<float> TerrainData => TerrainDataOwner.Memory.Slice(0, MapPoints.InputCount);

		public bool HasTerrainData => TerrainDataOwner != null;

		public MapCache(GeometryMapPoints pts, IMemoryOwner<float> trData)
		{
			MapPoints = pts;
			TerrainDataOwner = trData;
		}

		public void Dispose()
		{
			TerrainDataOwner?.Dispose();
		}
	}

	private static readonly RasterSymbology _dbgZSymbology = MultiTinRenderLayer.InterpolationSurfaceZSymbology();

	private static readonly RasterSymbology _dbgMSymbology = MultiTinRenderLayer.InterpolationSurfaceMSymbology();

	private readonly IHorizontalOutputBlock _ob;

	private readonly MapTypes _mt;

	private readonly Mapper _mapper;

	private readonly ILogger _logger = LogHelper.GetLogger("WaterSurfaceResampler");

	private IResample<float> _terrainResampler;

	private readonly Versioned<ContourLayerStats> _terrainContourStats = new Versioned<ContourLayerStats>();

	private RasterSymbology _symb;

	private bool _forceHzRender;

	private VelocityDisplayMode _vmode;

	private bool _mapDiagnostics;

	public IHorizontalOutputBlock OutputBlock => _ob;

	public Mapper Mapper => _mapper;

	public ContourLayerStats TerrainContourStats
	{
		get
		{
			if (!(_terrainResampler is ISpatialVersion spatialVersion))
			{
				return ContourLayerStats.None;
			}
			return _terrainContourStats.Get(spatialVersion.VersionXY, _terrainResampler, (IResample<float> r) => ContourLayerStatistics.Compute(r));
		}
	}

	public IResample<float> TerrainResampler => _terrainResampler;

	public ProfileState ProfileState { get; }

	IProfileState IProfileStateProvider.ProfileState => ProfileState;

	public RasterSymbology RasterSymbology
	{
		get
		{
			return _symb;
		}
		set
		{
			if (_symb != value)
			{
				_symb = value;
				IncrementRenderState();
			}
		}
	}

	public bool ForceHorizontalRendering
	{
		get
		{
			return _forceHzRender;
		}
		set
		{
			if (_forceHzRender != value)
			{
				_forceHzRender = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("ForceRecomputeMapPoints", "Force Refresh Map Points", "Refreshes Map Points per draw call. Debug Option.", true)]
	public bool ForceRecomputeMapPoints { get; set; }

	[PlotOption("VelDisplayMode", "Velocity Display Mode")]
	public VelocityDisplayMode VelocityDisplay
	{
		get
		{
			return _vmode;
		}
		set
		{
			if (_vmode != value)
			{
				_vmode = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("MapDiagnostics", "Map Diagnostics")]
	public bool MapDiagnostics
	{
		get
		{
			return _mapDiagnostics;
		}
		set
		{
			if (_mapDiagnostics != value)
			{
				_mapDiagnostics = value;
				IncrementRenderState();
			}
		}
	}

	public GenericMapType GenericMapType { get; set; }

	public override Extent ValidExtent => _ob.Geometry.Extent;

	public ResultMapResampler MakeResampler(bool chartFriendly = false)
	{
		return new ResultMapResampler(Mapper, _terrainResampler, GetSamplingParams)
		{
			ReplaceNoDataWithChartFriendlyValues = chartFriendly
		};
	}

	public ResultMapRenderLayer(IHorizontalOutputBlock ob, MapTypes mapType, IResample<float> terrainResampler, MeshPropertyTables[] tables, ProfileState profileState = null)
	{
		if (terrainResampler == null)
		{
			throw new ArgumentNullException("terrainResampler");
		}
		_ob = ob;
		_mt = mapType;
		_terrainResampler = terrainResampler;
		_mapper = Mapper.CreateMapper(mapType, ob, tables);
		if (profileState != null)
		{
			if (profileState.ProfileCount != _ob.ProfileCount)
			{
				throw new ArgumentException("The ProfileState's profile count doesn't match the output block's profile count.");
			}
			ProfileState = profileState;
		}
		else
		{
			ProfileState = _ob.CreateTrackedProfileState();
		}
		RasterSymbology = RasterSymbology.DefaultRasterSymbology();
	}

	public ResultMapRenderLayer(IHorizontalOutputBlock ob, GenericMapType mt, IResample<float> terrainResampler, MeshPropertyTables[] tables, ProfileState profileState = null)
	{
		if (terrainResampler == null)
		{
			throw new ArgumentNullException("terrainResampler");
		}
		_ob = ob;
		_mt = MapTypes.Generic;
		GenericMapType = mt;
		_terrainResampler = terrainResampler;
		_mapper = new GenericSurfaceMapper(mt, ob, tables);
		RasterSymbology = RasterSymbology.DefaultRasterSymbology();
		if (profileState != null)
		{
			if (profileState.ProfileCount != _ob.ProfileCount)
			{
				throw new ArgumentException("The ProfileState's profile count doesn't match the output block's profile count.");
			}
			ProfileState = profileState;
		}
		else
		{
			ProfileState = _ob.CreateTrackedProfileState();
		}
	}

	public static string LayerName(MapTypes mt)
	{
		return mt switch
		{
			MapTypes.WaterSurfaceElevation => "Water Surface", 
			MapTypes.Depth => "Depth", 
			MapTypes.Velocity => "Velocity", 
			MapTypes.DepthTimesVelocity => "D*V", 
			MapTypes.DepthTimesVelocitySquared => "D*V^2",
			_ => "Result Map", 
		};
	}

	public void AssignTerrain(IResample<float> terrainResampler)
	{
		_terrainResampler = terrainResampler;
	}

	public void SetDefaultSymbology(UnitSystem units = UnitSystem.USCustomary)
	{
		switch (_mt)
		{
		case MapTypes.WaterSurfaceElevation:
		{
			if (_terrainResampler == null)
			{
				RasterSymbology rasterSymbology3 = new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.WaterSurface, Histogram.SingleValue(0.0)));
				rasterSymbology3.HillshadeProperties.IsEnabled = false;
				RasterSymbology = rasterSymbology3;
				break;
			}
			Histogram histogram = Histogram.ComputeTiled(_terrainResampler, 1048576, 256);
			if (histogram.IsNull())
			{
				RasterSymbology = ((units == UnitSystem.SI) ? new RasterSymbology(MetricSurfaceFills.WSElevationDefaultValues) : new RasterSymbology(USCustomarySurfaceFills.WSElevationDefaultValues));
				break;
			}
			RasterSymbology rasterSymbology4 = new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.WaterSurface, histogram));
			rasterSymbology4.HillshadeProperties.IsEnabled = false;
			RasterSymbology = rasterSymbology4;
			break;
		}
		case MapTypes.Depth:
		{
			RasterSymbology rasterSymbology2 = ((units == UnitSystem.SI) ? new RasterSymbology(MetricSurfaceFills.DepthsValues) : new RasterSymbology(USCustomarySurfaceFills.DepthsValues));
			rasterSymbology2.HillshadeProperties.IsEnabled = false;
			RasterSymbology = rasterSymbology2;
			break;
		}
		case MapTypes.Velocity:
		{
			RasterSymbology rasterSymbology5 = ((units == UnitSystem.SI) ? new RasterSymbology(MetricSurfaceFills.VelocitiesValues) : new RasterSymbology(USCustomarySurfaceFills.VelocitiesValues));
			rasterSymbology5.HillshadeProperties.IsEnabled = false;
			RasterSymbology = rasterSymbology5;
			break;
		}
		case MapTypes.DepthTimesVelocity:
		{
			RasterSymbology rasterSymbology6 = new RasterSymbology(SurfaceFills.HazardValues);
			rasterSymbology6.HillshadeProperties.IsEnabled = false;
			RasterSymbology = rasterSymbology6;
			break;
		}
		case MapTypes.DepthTimesVelocitySquared:
		case MapTypes.Generic:
		{
			RasterSymbology rasterSymbology = ((units == UnitSystem.SI) ? new RasterSymbology(MetricSurfaceFills.GenericDefaultValues) : new RasterSymbology(USCustomarySurfaceFills.GenericDefaultValues));
			rasterSymbology.HillshadeProperties.IsEnabled = false;
			RasterSymbology = rasterSymbology;
			break;
		}
		}
	}

	public void SetLinearSymbology(double min, double max)
	{
		if (RasterSymbology != null && RasterSymbology.SurfaceFill.ColorValues != null && RasterSymbology.SurfaceFill.ColorValues.Length > 1)
		{
			RasterSymbology rasterSymbology = RasterSymbology.Clone();
			rasterSymbology.SurfaceFill = rasterSymbology.SurfaceFill.Clone(rasterSymbology.SurfaceFill.Colors, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { min, max }));
			rasterSymbology.HillshadeProperties.IsEnabled = false;
			RasterSymbology = rasterSymbology;
		}
		else if (_mt == MapTypes.WaterSurfaceElevation)
		{
			RasterSymbology rasterSymbology2 = new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.WaterSurface, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { min, max })));
			rasterSymbology2.HillshadeProperties.IsEnabled = false;
			RasterSymbology = rasterSymbology2;
		}
		else if (_mt == MapTypes.Depth)
		{
			RasterSymbology rasterSymbology3 = new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.Depths, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { min, max })));
			rasterSymbology3.HillshadeProperties.IsEnabled = false;
			RasterSymbology = rasterSymbology3;
		}
		else
		{
			RasterSymbology rasterSymbology4 = new RasterSymbology(new InterpolatedSurfaceFill(RasterSymbology.SurfaceFill.Colors, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { min, max })));
			rasterSymbology4.HillshadeProperties.IsEnabled = false;
			RasterSymbology = rasterSymbology4;
		}
	}

	private void SymbologyPropertyChanged(object sender, PropertyChangedEventArgs e)
	{
		IncrementRenderState();
	}

	private bool GeometryDataCheck()
	{
		bool condition = false;
		for (int i = 0; i < _ob.Geometry.FlowAreaLayer.Count; i++)
		{
			FlowArea flowArea = _ob.Geometry.FlowAreaLayer[i];
			if (flowArea == null)
			{
				_logger.LogOnce(LogLevel.Warning, "FlowArea {idx} did not load successfully.", i);
				condition = true;
				continue;
			}
			if (flowArea.Mesh == null)
			{
				_logger.LogOnce(LogLevel.Warning, "FlowArea {idx} ({name}) did not load a mesh.", i, flowArea.Name);
				condition = true;
			}
			if (flowArea.PropertyTables == null || flowArea.PropertyTables.CellMinimumElevations == null || flowArea.PropertyTables.FaceMinimumElevations == null)
			{
				_logger.LogOnce(LogLevel.Warning, "FlowArea {idx} ({name}) did not load property tables.", i, flowArea.Name);
				condition = true;
			}
		}
		return !condition;
	}

	private bool MapCacheMatchesCurrentState(MapCache mc)
	{
		if (mc == null)
		{
			return false;
		}
		if (mc.ForceHorizontal != ForceHorizontalRendering)
		{
			return false;
		}
		return true;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		if (request.Reporter.CancelRequested)
		{
			return false;
		}
		if (!ProfileState.IsRegularProfile)
		{
			_logger.LogOnce(LogLevel.Warning, "{Layer} Attempted to map bad profile: {ProfileIdx} (Subsequent errors will be ignored).", DisplayName, ProfileState.CurrentProfileIndex);
			return false;
		}
		if (!GeometryDataCheck())
		{
			return false;
		}
		MapCache mapCache = cache as MapCache;
		RasterSymbology rasterSymbology = RasterSymbology;
		bool hasRasterSymbology = rasterSymbology != null && rasterSymbology.ContourProperties?.IsEnabled == true;
		RasterDefinition rasterDefinition = (hasRasterSymbology ? request.RasterQuery.ExtendRaster(ContourRenderHelper.GutterCells) : request.RasterQuery);
		if (ForceRecomputeMapPoints || !MapCacheMatchesCurrentState(mapCache) || !mapCache.MapPoints.Raster.Equals(rasterDefinition))
		{
			GeometryMapPoints pts = new GeometryMapPoints(_ob.Geometry, rasterDefinition, GetSamplingParams());
			if (request.Reporter.CancelRequested)
			{
				return false;
			}
			mapCache = (MapCache)(cache = new MapCache(pts, null)
			{
				ForceHorizontal = ForceHorizontalRendering
			});
		}
		if (request.Reporter.CancelRequested)
		{
			return false;
		}
		if (mapCache.MapPoints.MappedPixelCount == 0)
		{
			return false;
		}
		Memory<float> terrain;
		if (mapCache.HasTerrainData)
		{
			terrain = mapCache.TerrainData;
		}
		else
		{
			if (request.CommonData == null)
			{
				return false;
			}
			if (!hasRasterSymbology && request.CommonData.TryRetrieveCommonData(_terrainResampler, out var data) && data is Memory<float> terrainSpan)
			{
				terrain = terrainSpan;
			}
			else
			{
				ResampleArgs args = new ResampleArgs(rasterDefinition, InterpolationMode.Linear);
				IMemoryOwner<float> terrainDataOwner = SharedMemoryPool<float>.RentNoFill(rasterDefinition.CellCount, out var mem);
				_terrainResampler.Resample(args, mem);
				terrain = mem;
				mapCache.TerrainDataOwner = terrainDataOwner;
				if (!hasRasterSymbology)
				{
					request.CommonData.RegisterCommonData(_terrainResampler, mem);
				}
			}
		}
		IMemoryOwner<float> data2 = SharedMemoryPool<float>.Rent(request.RasterQuery.CellCount, out var mem2);
		ContourValueContext contourContext = default;
		IMemoryOwner<float> memoryOwner = null;
		List<(Geospatial.Vectors.Point, string)> diagLabels;
		if (hasRasterSymbology)
		{
			memoryOwner = SharedMemoryPool<float>.Rent(rasterDefinition.CellCount, out var mem3);
			ComputeSwitch(mapCache.MapPoints, terrain, mem3, out diagLabels);
			RasterOperations.CopyTo(rasterDefinition, mem3, request.RasterQuery, mem2);
			ContourLayerStats terrainContourStats = TerrainContourStats;
			contourContext = new ContourValueContext(rasterDefinition, mem3, new ContourLayerStats(terrainContourStats.ReferenceSlope, double.NaN, double.NaN, terrainContourStats.ScaleCellSizes, terrainContourStats.ScaleSlopes));
		}
		else
		{
			ComputeSwitch(mapCache.MapPoints, terrain, mem2, out diagLabels);
		}
		request.SetRasterData_TransferOwnership(this, data2);
		if (diagLabels != null)
		{
			for (int i = 0; i < diagLabels.Count; i++)
			{
				(Geospatial.Vectors.Point, string) tuple = diagLabels[i];
				request.AddLabel(new LabelDefinition(tuple.Item2, tuple.Item1));
			}
		}
		if (request.Reporter.CancelRequested)
		{
			memoryOwner?.Dispose();
			return false;
		}
		try
		{
			RasterRenderHelper.Render(mem2, request, RasterSymbology, -9999f, contourContext);
		}
		finally
		{
			memoryOwner?.Dispose();
		}
		return true;
	}

	private GeometrySamplingParameters GetSamplingParams()
	{
		if (ForceHorizontalRendering)
		{
			return GeometrySamplingParameters.ForceHorizontalMode;
		}
		return GeometrySamplingParameters.Default;
	}

	public bool Resample(Memory<Geospatial.Vectors.Point> points, out float[] values)
	{
		GeometryMapPoints geometryMapPoints = new GeometryMapPoints(_ob.Geometry, points, GetSamplingParams());
		if (geometryMapPoints.MappedPixelCount == 0)
		{
			values = null;
			return false;
		}
		float[] samplePoints = _terrainResampler.SamplePoints(points);
		values = new float[samplePoints.Length];
		ComputeSwitch(geometryMapPoints, samplePoints, values, out var _);
		return true;
	}

	public bool Resample(RasterDefinition rdef, out float[] values)
	{
		GeometryMapPoints geometryMapPoints = new GeometryMapPoints(_ob.Geometry, rdef, GetSamplingParams());
		if (geometryMapPoints.MappedPixelCount == 0)
		{
			values = null;
			return false;
		}
		float[] resample = _terrainResampler.Resample(rdef);
		values = new float[resample.Length];
		ComputeSwitch(geometryMapPoints, resample, values, out var _);
		return true;
	}

	private void ComputeSwitch(GeometryMapPoints mapPts, Memory<float> terrain, Memory<float> output, out List<(Geospatial.Vectors.Point, string)> diagLabels)
	{
		diagLabels = null;
		if (mapPts.InputCount == 0)
		{
			return;
		}
		int currentProfileIndex = ProfileState.CurrentProfileIndex;
		switch (_mt)
		{
		case MapTypes.WaterSurfaceElevation:
			(_mapper as WaterSurfaceMapper).Compute(currentProfileIndex, mapPts, terrain, output);
			break;
		case MapTypes.Depth:
			(_mapper as DepthMapper).Compute(currentProfileIndex, mapPts, terrain, output);
			break;
		case MapTypes.Velocity:
		{
			VelocityMapper velocityMapper = _mapper as VelocityMapper;
			if (VelocityDisplay == VelocityDisplayMode.Magnitude)
			{
				velocityMapper.Compute(currentProfileIndex, mapPts, terrain, output);
			}
			else
			{
				Memory<float> mem;
				using (SharedMemoryPool<float>.Rent(mapPts.InputCount, out mem))
				{
					Memory<float> mem2;
					using (SharedMemoryPool<float>.Rent(mapPts.InputCount, out mem2))
					{
						velocityMapper.Compute(currentProfileIndex, mapPts, terrain, mem, mem2, output);
						if (VelocityDisplay == VelocityDisplayMode.Vx)
						{
							mem.CopyTo(output);
						}
						else if (VelocityDisplay == VelocityDisplayMode.Vy)
						{
							mem2.CopyTo(output);
						}
						Span<float> outputSpan = output.Span;
						for (int i = 0; i < output.Length; i++)
						{
							if (outputSpan[i] != -9999f)
							{
								outputSpan[i] = Math.Abs(outputSpan[i]);
							}
						}
					}
				}
			}
			if (!MapDiagnostics)
			{
				break;
			}
			diagLabels = new List<(Geospatial.Vectors.Point, string)>();
			int[] intValues = new int[16];
			for (int j = 0; j < mapPts.SlopingMapped2DAreas.Count; j++)
			{
				SlopingMeshMap slopingMeshMap = mapPts.SlopingMapped2DAreas[j];
				if (slopingMeshMap.Cells.Count > 1000)
				{
					continue;
				}
				Mesh mesh = _ob.Geometry.FlowAreaLayer[slopingMeshMap.MeshIndex].Mesh;
				using ImOnLoan<VectorMeshInputs> imOnLoan = velocityMapper.MapperCache.Get2DVelocityInputs(slopingMeshMap.MeshIndex, currentProfileIndex);
				for (int k = 0; k < slopingMeshMap.Cells.Count; k++)
				{
					int cellIndex = slopingMeshMap.Cells[k].CellIndex;
					Cell cell = mesh.Cells[cellIndex];
					Span<int> faces = cell.Faces.Span;
					mesh.CellFacePoints(cellIndex, intValues);
					for (int l = 0; l < faces.Length; l++)
					{
						int ordinal = intValues[l];
						FacePoint facePoint = mesh.FacePoints[ordinal];
						_ = ref imOnLoan.Item.FacepointVectors.Span[ordinal];
						diagLabels.Add((facePoint.Point, ordinal.ToString()));
					}
				}
			}
			break;
		}
		case MapTypes.DepthTimesVelocitySquared:
			(_mapper as DepthVelocitySquaredMapper).Compute(currentProfileIndex, mapPts, terrain, output);
			break;
		case MapTypes.Generic:
			(_mapper as GenericSurfaceMapper).Compute(currentProfileIndex, mapPts, terrain, output);
			break;
		default:
			_mapper.Compute(currentProfileIndex, mapPts, terrain, output);
			break;
		}
	}

	private bool PaintGeometry(PlotRequest request, GeometryMapPoints mapPts)
	{
		if (!request.RasterQuery.Extent.Overlaps(ValidExtent))
		{
			return false;
		}
		int toARGB = Color.DarkGreen.ToARGB();
		Color.LightBlue.ToARGB();
		Color.LightGreen.ToARGB();
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(request.RasterQuery.CellCount, 0, out mem))
		{
			Span<int> memSpan = mem.Span;
			bool condition = false;
			foreach (SlopingMeshMap slopingMapped2DArea in mapPts.SlopingMapped2DAreas)
			{
				for (int i = 0; i < slopingMapped2DArea.Cells.Count; i++)
				{
					Span<PointRange> pixelRanges = slopingMapped2DArea.Cells[i].PixelRanges.Span;
					for (int j = 0; j < pixelRanges.Length; j++)
					{
						PointRange pointRange = pixelRanges[j];
						for (int k = pointRange.QueryStartIndex; k <= pointRange.QueryEndIndex; k++)
						{
							memSpan[k] = toARGB;
						}
						condition = true;
					}
				}
			}
			foreach (FlatMeshMap flatMapped2DArea in mapPts.FlatMapped2DAreas)
			{
				for (int l = 0; l < flatMapped2DArea.Cells.Count; l++)
				{
					FlatCellMap flatCellMap = flatMapped2DArea.Cells[l];
					for (int m = 0; m < flatCellMap.PointIndexes.Count; m++)
					{
						memSpan[flatCellMap.PointIndexes[m]] = toARGB;
						condition = true;
					}
				}
			}
			if (condition)
			{
				request.Splat(mem);
				return true;
			}
			return false;
		}
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + RasterSymbology.VersionXY + (ProfileState?.Version ?? 0);
	}
}
