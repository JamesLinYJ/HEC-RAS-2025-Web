using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using H5Assist.Caching.VB;
using H5Assist.VB;
using Hec.Dss;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Render;
using RasMapperLib.Terrain;
using RasMapperLib.Utilities;
using TiffAssist;
using Utility;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class RASResultsMap : FeatureLayer, IAnimatingLayer, IVelocityLayer
{
	bool IAnimatingLayer.CanAnimate => OutputModeAnimateable;
	int IAnimatingLayer.CurrentProfileIndex { get => ProfileIndex; set => ProfileIndex = value; }
	string IAnimatingLayer.CurrentProfileName => ProfileName;

	public enum ElevationSources
	{
		Terrain,
		Point,
		AttributeColumn
	}

	[CompilerGenerated]
private sealed class _Closure_0024__78_002D0
	{
		public RASGeometry geometry;

		public object bufLock;

		public Stack<float[]> unusedBuffers;

		public int totalProgress;

		public float[] fileProgress;

		public ProgressReporter reporter;

		public TerrainLayer terrain;

		public int prevOverallProgress;

		public object ftrLock;

		public object h5Lock;

		public ThreadLocal<ContourBandRaster> tlContours;

		public ConcurrentBag<ContourBandRaster> savedContours;

		public Func<RASGeometryMapPoints, float[], float[]> computer;

		public CacheCollection cache;

		public Tin tin;

		public RASResultsMap rasResultsMap;

		public _Closure_0024__78_002D0(_Closure_0024__78_002D0 other)
		{
			if (other != null)
			{
				geometry = other.geometry;
				bufLock = other.bufLock;
				unusedBuffers = other.unusedBuffers;
				totalProgress = other.totalProgress;
				fileProgress = other.fileProgress;
				reporter = other.reporter;
				terrain = other.terrain;
				prevOverallProgress = other.prevOverallProgress;
				ftrLock = other.ftrLock;
				h5Lock = other.h5Lock;
				tlContours = other.tlContours;
				savedContours = other.savedContours;
				computer = other.computer;
				cache = other.cache;
				tin = other.tin;
			}
		}

		[SpecialName]
		internal RASGeometryMapPoints _Lambda_0024__0()
		{
			return new RASGeometryMapPoints(geometry);
		}

		[SpecialName]
		internal float[] _Lambda_0024__2()
		{
			object bufLock = this.bufLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(bufLock);
			bool lockTaken = false;
			try
			{
				Monitor.Enter(bufLock, ref lockTaken);
				if (unusedBuffers.Count > 0)
				{
					return unusedBuffers.Pop();
				}
				return new float[66049];
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(bufLock);
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(float[] buf)
		{
			object bufLock = this.bufLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(bufLock);
			bool lockTaken = false;
			try
			{
				Monitor.Enter(bufLock, ref lockTaken);
				if (unusedBuffers.Count <= 256)
				{
					unusedBuffers.Push(buf);
				}
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(bufLock);
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__78_002D1
	{
		public object progressLock;

		public int tilesCompleted;

		public int nTiles;

		public int prevProgressWithin;

		public RasterFileInfo rfi;

		public Action tileDone;

		public ThreadLocal<TerrainLayer.RenderTileData> tlRenderTile;

		public SpatialIndex<int> spIdx;

		public _Closure_0024__78_002D4 outerClosure;

		public _Closure_0024__78_002D1(_Closure_0024__78_002D1 other)
		{
			if (other != null)
			{
				progressLock = other.progressLock;
				tilesCompleted = other.tilesCompleted;
				nTiles = other.nTiles;
				prevProgressWithin = other.prevProgressWithin;
				rfi = other.rfi;
				tileDone = other.tileDone;
				tlRenderTile = other.tlRenderTile;
				spIdx = other.spIdx;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			object progressLock = this.progressLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(progressLock);
			bool lockTaken = false;
			checked
			{
				try
				{
					Monitor.Enter(progressLock, ref lockTaken);
					tilesCompleted++;
					int systemTerm = (int)System.Math.Round((double)tilesCompleted / (double)nTiles * 100.0);
					int systemTerm2 = (int)System.Math.Round((double)outerClosure.outerClosure.outerClosure.outerClosure.totalProgress + 1.0 * (double)systemTerm * (double)outerClosure.outerClosure.outerClosure.outerClosure.fileProgress[outerClosure.outerClosure.rastL]);
					if (prevProgressWithin < systemTerm)
					{
						outerClosure.outerClosure.outerClosure.outerClosure.reporter.ReportMessage("File " + (outerClosure.outerClosure.rastL + 1) + " of " + outerClosure.outerClosure.outerClosure.outerClosure.terrain.RasterFileCount + ": " + systemTerm + "% processed");
					}
					if (outerClosure.outerClosure.outerClosure.outerClosure.prevOverallProgress < systemTerm2)
					{
						outerClosure.outerClosure.outerClosure.outerClosure.reporter.ReportProgress(systemTerm2);
						outerClosure.outerClosure.outerClosure.outerClosure.prevOverallProgress = systemTerm2;
					}
				}
				finally
				{
					if (lockTaken)
					{
						Monitor.Exit(progressLock);
					}
				}
			}
		}

		[SpecialName]
		internal TerrainLayer.RenderTileData _Lambda_0024__5()
		{
			TerrainLayer.RenderTileData renderTileData = new TerrainLayer.RenderTileData(outerClosure.outerClosure.outerClosure.hr);
			renderTileData.SetRaster(outerClosure.tr, rfi);
			return renderTileData;
		}

		[SpecialName]
		internal void _Lambda_0024__6(int t)
		{
			tileDone();
			if (!rfi.TileHasAnyData[0][t])
			{
				return;
			}
			RasterM rasterM = new RasterM();
			outerClosure.outerClosure.outerClosure.outerClosure.terrain.RasterFileRenderTile(outerClosure.outerClosure.rastL, 0, t, rasterM);
			if (!outerClosure.outerClosure.outerClosure.outerClosure.geometry.Overlaps(rasterM))
			{
				return;
			}
			TerrainLayer.RenderTileData renderTileData = tlRenderTile.Value;
			object ftrLock = outerClosure.outerClosure.outerClosure.outerClosure.ftrLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(ftrLock);
			bool lockTaken = false;
			bool setTile;
			try
			{
				Monitor.Enter(ftrLock, ref lockTaken);
				object h5Lock = outerClosure.outerClosure.outerClosure.outerClosure.h5Lock;
				ObjectFlowControl.CheckForSyncLockOnValueType(h5Lock);
				bool lockTaken2 = false;
				try
				{
					Monitor.Enter(h5Lock, ref lockTaken2);
					setTile = renderTileData.SetTile(0, t);
					if (renderTileData.IsTileCompletelyMasked)
					{
						return;
					}
				}
				finally
				{
					if (lockTaken2)
					{
						Monitor.Exit(h5Lock);
					}
				}
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(ftrLock);
				}
			}
			if (!setTile)
			{
				return;
			}
			bool isValueCreated = outerClosure.outerClosure.outerClosure.outerClosure.tlContours.IsValueCreated;
			ContourBandRaster contourBandRaster = outerClosure.outerClosure.outerClosure.outerClosure.tlContours.Value;
			if (!isValueCreated)
			{
				outerClosure.outerClosure.outerClosure.outerClosure.savedContours.Add(contourBandRaster);
			}
			RASGeometryMapPoints rASGeometryMapPoints = new RASGeometryMapPoints(outerClosure.outerClosure.outerClosure.outerClosure.geometry);
			rASGeometryMapPoints.ComputeForRaster(renderTileData.TileWithPerimeterRaster);
			float[] values = null;
			if (outerClosure.outerClosure.outerClosure.outerClosure.computer == null)
			{
				object h5Lock2 = outerClosure.outerClosure.outerClosure.outerClosure.h5Lock;
				ObjectFlowControl.CheckForSyncLockOnValueType(h5Lock2);
				bool lockTaken3 = false;
				try
				{
					Monitor.Enter(h5Lock2, ref lockTaken3);
					outerClosure.outerClosure.outerClosure.outerClosure.rasResultsMap.Results.ComputeSwitch(outerClosure.outerClosure.outerClosure.outerClosure.rasResultsMap, rASGeometryMapPoints, outerClosure.outerClosure.outerClosure.outerClosure.rasResultsMap.ProfileIndex, renderTileData.TileWithPerimeterValues, outerClosure.outerClosure.outerClosure.outerClosure.cache, ref values);
				}
				finally
				{
					if (lockTaken3)
					{
						Monitor.Exit(h5Lock2);
					}
				}
			}
			else
			{
				values = outerClosure.outerClosure.outerClosure.outerClosure.computer(rASGeometryMapPoints, renderTileData.TileWithPerimeterValues);
			}
			contourBandRaster.ContourRaster(renderTileData.TileWithPerimeterRaster, values);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__78_002D2
	{
		public int rastL;

		public _Closure_0024__78_002D3 outerClosure;

		public _Closure_0024__78_002D2(_Closure_0024__78_002D2 other)
		{
			if (other != null)
			{
				rastL = other.rastL;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__78_002D3
	{
		public H5Reader hr;

		public _Closure_0024__78_002D0 outerClosure;

		public _Closure_0024__78_002D3(_Closure_0024__78_002D3 other)
		{
			if (other != null)
			{
				hr = other.hr;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__78_002D4
	{
		public FloatTiffReader tr;

		public _Closure_0024__78_002D2 outerClosure;

		public _Closure_0024__78_002D4(_Closure_0024__78_002D4 other)
		{
			if (other != null)
			{
				tr = other.tr;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__78_002D5
	{
		public float[] terrainValues;

		public _Closure_0024__78_002D0 outerClosure;

		public _Closure_0024__78_002D5(_Closure_0024__78_002D5 other)
		{
			if (other != null)
			{
				terrainValues = other.terrainValues;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__7(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				terrainValues[i] = (float)outerClosure.tin.PointM(i).Z;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__81_002D0
	{
		public RASGeometry geometry;

		public int pfCount;

		public CacheCollection cache;

		public float[] tileValues;

		public List<Stats> allStats;

		public RASResultsMap rasResultsMap;

		public _Closure_0024__81_002D0(_Closure_0024__81_002D0 other)
		{
			if (other != null)
			{
				geometry = other.geometry;
				pfCount = other.pfCount;
				cache = other.cache;
				tileValues = other.tileValues;
				allStats = other.allStats;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(RasterM tileRasterM, float[] terrainData)
		{
			RASGeometryMapPoints rASGeometryMapPoints = geometry.MapPixels(tileRasterM);
			if (rASGeometryMapPoints.MappedPixelCount != 0)
			{
				List<Stats> stats = new List<Stats>();
				if (!rasResultsMap.MapType.CanAnimate)
				{
					throw new NotImplementedException();
				}
				float[][] pointTimeSeries = null;
				rasResultsMap.Results.ComputeSwitchTimeSeries(rasResultsMap, rASGeometryMapPoints, terrainData, cache, ref pointTimeSeries, convertToGraphableData: false);
				float[][] pointTimeSeriesArray = pointTimeSeries;
				foreach (float[] pointTimeSery in pointTimeSeriesArray)
				{
					stats.Add(Stats.Compute(pointTimeSery, -9999f, pointTimeSery.Length, 1, pointTimeSery.Length, 1));
				}
				allStats.Add(Stats.Combine(stats));
			}
		}
	}

	public const string sPlotConnectedFaces = "Plot 2D Hydraulic Connectivity";

	public const string sPlotWaterSurfaceGradient = "Plot 2D Water Surface Gradient";

	public const string sDrawValues = "Draw Map Values";

	public const string sDrawPerpFaceVels = "Draw Perpendicular Face Velocities";

	public const string sDrawTrueFaceVels = "Draw True Face Velocities (interpolated)";

	public const string sFaceCentroid = "Face Low-Elevation Centroid";

	public const string sDisplayArrivalTimeDates = "Display Arrival Times as Dates";

	public const string sDeficiency = "Plot Model Boundary Deficiencies";

	public const string sUpToDate = "Map files up to date";

	public const string sFilesDoNotExist = "Map not created yet";

	public const string sFileOutOfDate = "Map files are out of date";

	public const string sResultsDoNotExist = "Results do not exist";

	public const string sTerrainNotFound = "Terrain not found";

	public const string sMapValue = "Map Value";

	public const string sMaxVelWarning = "Max Velocity is built from summary data, and does not necessarily represent true velocities. See documentation for details.";

	public const int NullProfileIndex = -1;

	public const string NullProfileName = "None";

	public const int MaxProfileIndex = int.MaxValue;

	public const string MaxProfileName = "Max";

	public const int MinProfileIndex = int.MinValue;

	public const string MinProfileName = "Min";

	private MapTypes _mapType;

	private int _profileIndex;

	private OutputModes _outputMode;

	private string _overwriteTerrainLayerName;

	private RasterM _raster;

	private string _baseLayerName;

	private const float MinDepthThreshold = 0.001f;

	private float _depthThreshold;

	private ArrivalStartModes _arrivalStartMode;

	private float _arrivalSimDays;

	private float _arrivalSimHours;

	private float _arrivalSimMinutes;

	private string _arrivalAbsDateTime;

	private bool _arrivalAsDelta;

	private bool _useHours;

	private bool _useParameters;


	private string _pointLayername;

	private ElevationSources _elevationSource;

	private string _elevationColumnName;

	private PointMs _points;

	private float[] _pointsZ;

	private RASGeometryMapPoints _pointMPS;

	private string _outputBlockName;

	private bool FORCE_POLYGON_SERIAL;

	private string _overwriteOutputFilename;

	private TerrainLayer _overrideTerrain;


	public override bool CanMoveToAnotherLevel { get; }

	public override bool CanRename { get; }

	public override bool CanEdit => false;

	public bool IsIndundationBoundaryMap
	{
		get
		{
			if (MapType == MapTypes.Depth)
			{
				return OutputMode == OutputModes.StoredPolygonSpecifiedDepth;
			}
			return false;
		}
	}

	public RASResults Results { get; set; }

	public string OutputBlockName
	{
		get
		{
			return _outputBlockName;
		}
		set
		{
			_outputBlockName = value;
		}
	}

	public TimeProfile OutputBlock
	{
		get
		{
			TimeProfile timeProfile;
			if (string.IsNullOrEmpty(_outputBlockName))
			{
				timeProfile = Results?.OutputBlocks?.BaseOutput;
			}
			else
			{
				TimeProfile timeProfile2 = null;
				Results?.OutputBlocks.Blocks.TryGetValue(_outputBlockName, out timeProfile2);
				timeProfile = ((timeProfile2 != null) ? timeProfile2 : Results?.OutputBlocks.BaseOutput);
			}
			if (timeProfile == null)
			{
				return new TimeProfile();
			}
			return timeProfile;
		}
	}

	public string VRTFilename => StoredMapBaseFilename + ".vrt";

	public string SHPFilename => StoredMapBaseFilename + ".shp";

	public override string SourceFilename
	{
		get
		{
			if (OutputModeIsStoredType)
			{
				if (_outputMode.Equals(OutputModes.StoredDefaultTerrain) || _outputMode.Equals(OutputModes.StoredSpecifiedTerrain))
				{
					return VRTFilename;
				}
				return SHPFilename;
			}
			return "";
		}
		set
		{
		}
	}

	public string BaseDescription => GetBaseDescription(MapType, _outputMode);

	private string DescriptionParameters => GetDescriptionParams();

	public string Description => BaseDescription + DescriptionParameters;

	public new string LayerName
	{
		get
		{
			if (string.IsNullOrEmpty(_baseLayerName))
			{
				return BaseDescription;
			}
			return _baseLayerName;
		}
		set
		{
			_baseLayerName = value;
		}
	}

	public bool UseParameters
	{
		get
		{
			return _useParameters;
		}
		set
		{
			_useParameters = value;
		}
	}

	public string LayerNameWithParameters
	{
		get
		{
			if (_useParameters)
			{
				return LayerName + DescriptionParameters;
			}
			return LayerName;
		}
	}

	public string OverwriteOutputFilename
	{
		get
		{
			return _overwriteOutputFilename;
		}
		set
		{
			_overwriteOutputFilename = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(value), System.IO.Path.GetFileNameWithoutExtension(value));
		}
	}

	private string StoredMapBaseFilename
	{
		get
		{
			if (string.IsNullOrEmpty(_overwriteOutputFilename))
			{
				string description = Description;
				description = description.Replace(":", " ");
				description = description.Replace("*", "_");
				return System.IO.Path.Combine(Results.OutputFolder, description);
			}
			return System.IO.Path.Combine(Results.OutputFolder, _overwriteOutputFilename);
		}
	}

	public int ProfileIndex
	{
		get
		{
			return _profileIndex;
		}
		set
		{
			if (Results == null || OutputBlock == null || OutputBlock.ProfileNames == null || value < OutputBlock.ProfileCount || value == int.MaxValue)
			{
				_profileIndex = value;
			}
		}
	}

	public string ProfileName
	{
		get
		{
			if (IsMinProfile)
			{
				return "Min";
			}
			if (IsMaxProfile)
			{
				return "Max";
			}
			if (IsNullProfile)
			{
				return "None";
			}
			if (Results == null || OutputBlock == null || OutputBlock.ProfileCount == 0)
			{
				return "None";
			}
			if (OutputBlock.ProfileNames.Count() <= _profileIndex)
			{
				return "None";
			}
			return OutputBlock.ProfileNames[_profileIndex];
		}
	}

	public bool IsMaxProfile => _profileIndex == int.MaxValue;

	public bool IsMinProfile => _profileIndex == int.MinValue;

	public bool IsProfile => !(IsMinProfile | IsMaxProfile | IsNullProfile);

	public bool IsNullProfile => _profileIndex == -1;

	public float ContourPolygonValue { get; set; }

	public override Extent Extent
	{
		get
		{
			if (_extent == null && Results != null)
			{
				RASGeometry geometry = Results.Geometry;
				_extent = Vector.MaximumExtent(_extent, geometry.XSInterpolationSurface.Extent);
				_extent = Vector.MaximumExtent(_extent, geometry.StorageArea.Extent);
				_extent = Vector.MaximumExtent(_extent, geometry.D2FlowArea.Extent);
			}
			return _extent;
		}
	}

	public TerrainLayer Terrain
	{
		get
		{
			if (_outputMode.Equals(OutputModes.StoredSpecifiedTerrain))
			{
				if (_overrideTerrain == null)
				{
					_overrideTerrain = SharedData.RasMapper?.TerrainsGroup.FindTerrainByName(_overwriteTerrainLayerName);
				}
				return _overrideTerrain;
			}
			return Results.Geometry.Terrain;
		}
		set
		{
			_overrideTerrain = value;
			_overwriteTerrainLayerName = value?.TerrainName;
		}
	}

	public string OverwriteTerrainLayerName
	{
		get
		{
			return _overwriteTerrainLayerName;
		}
		set
		{
			_overwriteTerrainLayerName = value;
			_overrideTerrain = null;
		}
	}

	public override bool CanExportAsShapefile => OutputModeIsStoredType & !OutputModeIsRaster;

	public override bool CanExportAsPoints { get; }

	public override bool CanLabelFeatures => _outputMode.IsPoint;

	public MapTypes MapType
	{
		get
		{
			if ((object)_mapType == null)
			{
				return MapTypes.Depth;
			}
			return _mapType;
		}
		set
		{
			_mapType = value;
			SetDisplayName(_mapType.DefaultLayerName);
		}
	}

	public string MapUnits
	{
		get
		{
			MapTypes mapType = MapType;
			if (mapType == MapTypes.Depth)
			{
				return SharedData.LengthUnits;
			}
			if (mapType == MapTypes.Elevation)
			{
				return SharedData.LengthUnits;
			}
			if (mapType == MapTypes.Flow)
			{
				return SharedData.FlowUnits;
			}
			if (mapType == MapTypes.Velocity)
			{
				return SharedData.VelocityUnits;
			}
			if (mapType == MapTypes.DepthTimesVelocity)
			{
				return SharedData.LengthUnits + " " + SharedData.VelocityUnits;
			}
			if (mapType == MapTypes.ArrivalTime || mapType == MapTypes.Duration || mapType == MapTypes.Recession)
			{
				if (_useHours)
				{
					return "hours";
				}
				return "days";
			}
			return "unknown";
		}
	}

	public OutputModes OutputMode
	{
		get
		{
			return _outputMode;
		}
		set
		{
			_outputMode = value;
		}
	}

	public bool OutputModeIsStoredType => _outputMode.IsStored;

	public RoundTo OutputModeRounding
	{
		get
		{
			if (_outputMode.IsDynamic)
			{
				if (MapType == MapTypes.ArrivalTime || MapType == MapTypes.Duration || MapType == MapTypes.Recession)
				{
					return RoundTo.None;
				}
				_ = SharedData.Units;
				_ = 1;
				return RoundTo.ThousandTwentyFourths;
			}
			return RoundTo.None;
		}
	}

	public string OutputModeFormat
	{
		get
		{
			if (MapType == MapTypes.ArrivalTime || MapType == MapTypes.Duration || MapType == MapTypes.Recession)
			{
				return "0.0000";
			}
			if (MapType == MapTypes.LaplacianU || MapType == MapTypes.LaplacianV)
			{
				return "0.0000000";
			}
			return SharedData.GetOutputDecimalPlacesStringFormat();
		}
	}

	public bool OutputModeAnimateable
	{
		get
		{
			if (_outputMode.IsDynamic)
			{
				return MapType.CanAnimate;
			}
			return false;
		}
	}

	public bool OutputModeNeedsTerrain
	{
		get
		{
			if (_outputMode.IsPoint)
			{
				return false;
			}
			return true;
		}
	}

	public bool OutputModeIsRaster
	{
		get
		{
			if (_outputMode.IsRaster)
			{
				return true;
			}
			return false;
		}
	}

	public float DepthThreshold
	{
		get
		{
			return _depthThreshold;
		}
		set
		{
			_depthThreshold = value;
		}
	}

	public double ArrivalStartingSimulationTime
	{
		get
		{
			if (_arrivalStartMode == ArrivalStartModes.AtBeginning)
			{
				return 0.0;
			}
			if (_arrivalStartMode == ArrivalStartModes.UseSimTime)
			{
				double computed = 0.0;
				if (!float.IsNaN(_arrivalSimDays))
				{
					computed += (double)_arrivalSimDays;
				}
				if (!float.IsNaN(_arrivalSimHours))
				{
					computed += (double)_arrivalSimHours / 24.0;
				}
				if (!float.IsNaN(_arrivalSimMinutes))
				{
					computed += (double)_arrivalSimMinutes / 24.0 / 60.0;
				}
				return computed;
			}
			if (_arrivalStartMode == ArrivalStartModes.UseAbsDateTime)
			{
				DateTime dateTime = DSS.ConvertFromDSSDateTime(_arrivalAbsDateTime);
				if ((DateTime.Compare(dateTime, DateTime.MinValue) != 0) & (Results.ProfileDateTimes.Count() > 0))
				{
					return (dateTime - Results.ProfileDateTimes[0]).TotalDays;
				}
				return 0.0;
			}
			return 0.0;
		}
	}

	public ArrivalStartModes ArrivalStartMode
	{
		get
		{
			return _arrivalStartMode;
		}
		set
		{
			_arrivalStartMode = value;
		}
	}

	public float ArrivalSimDays
	{
		get
		{
			return _arrivalSimDays;
		}
		set
		{
			_arrivalSimDays = value;
		}
	}

	public float ArrivalSimHours
	{
		get
		{
			return _arrivalSimHours;
		}
		set
		{
			_arrivalSimHours = value;
		}
	}

	public float ArrivalSimMinutes
	{
		get
		{
			return _arrivalSimMinutes;
		}
		set
		{
			_arrivalSimMinutes = value;
		}
	}

	public string ArrivalAbsDateTime
	{
		get
		{
			return _arrivalAbsDateTime;
		}
		set
		{
			_arrivalAbsDateTime = value;
		}
	}

	public bool ArrivalAsDelta
	{
		get
		{
			return _arrivalAsDelta;
		}
		set
		{
			_arrivalAsDelta = value;
		}
	}

	public bool UseHours
	{
		get
		{
			return _useHours;
		}
		set
		{
			_useHours = value;
		}
	}

	public string Units
	{
		get
		{
			if (MapType.LengthUnits)
			{
				return SharedData.LengthUnits;
			}
			if (MapType.TimeUnits)
			{
				if (UseHours)
				{
					return "Hours";
				}
				return "Days";
			}
			if (MapType.SpeedUnits)
			{
				return SharedData.VelocityUnits;
			}
			if (MapType.PercentUnits)
			{
				return "Percent";
			}
			return "";
		}
	}

	public string PointLayerName
	{
		get
		{
			return _pointLayername;
		}
		set
		{
			_pointLayername = value;
			ClearFeatureTable();
		}
	}

	public ElevationSources ElevationSource
	{
		get
		{
			return _elevationSource;
		}
		set
		{
			_elevationSource = value;
			ClearFeatureTable();
		}
	}

	public string ElevationColumnName
	{
		get
		{
			return _elevationColumnName;
		}
		set
		{
			_elevationColumnName = value;
			ClearFeatureTable();
		}
	}

	public bool CanProvideVelocity => OutputModeAnimateable;

	public IList<string> AvailableProfiles => OutputBlock.ProfileNames;

	public bool CanProvideMinMax
	{
		get
		{
			if (_outputMode.IsDynamic)
			{
				return MapType.SupportsDynamicMinMaxProfile;
			}
			return false;
		}
	}

	public RASResultsMap(RASResults res)
		: this(res, MapTypes.Depth)
	{
	}

	public RASResultsMap(RASResults res, MapTypes mapType)
		: base(mapType.DefaultLayerName)
	{
		_outputMode = OutputModes.DynamicSurface;
		_raster = new RasterM();
		_depthThreshold = float.NaN;
		_arrivalStartMode = ArrivalStartModes.AtBeginning;
		_arrivalSimDays = float.NaN;
		_arrivalSimHours = float.NaN;
		_arrivalSimMinutes = float.NaN;
		_arrivalAbsDateTime = "";
		_arrivalAsDelta = false;
		_useHours = true;
		_useParameters = true;
		_elevationSource = ElevationSources.Terrain;
		_points = null;
		_pointMPS = null;
		CanMoveToAnotherLevel = false;
		CanRename = false;
		FORCE_POLYGON_SERIAL = false;
		_overwriteOutputFilename = "";
		ContourPolygonValue = 0f;
		CanExportAsPoints = false;
		Results = res;
		MapType = mapType;
	}

	public RASResultsMap(RASResults res, Generic2DMapType mapType)
		: base(mapType.HDF5Dataset)
	{
		_outputMode = OutputModes.DynamicSurface;
		_raster = new RasterM();
		_depthThreshold = float.NaN;
		_arrivalStartMode = ArrivalStartModes.AtBeginning;
		_arrivalSimDays = float.NaN;
		_arrivalSimHours = float.NaN;
		_arrivalSimMinutes = float.NaN;
		_arrivalAbsDateTime = "";
		_arrivalAsDelta = false;
		_useHours = true;
		_useParameters = true;
		_elevationSource = ElevationSources.Terrain;
		_points = null;
		_pointMPS = null;
		CanMoveToAnotherLevel = false;
		CanRename = false;
		FORCE_POLYGON_SERIAL = false;
		_overwriteOutputFilename = "";
		ContourPolygonValue = 0f;
		CanExportAsPoints = false;
		Results = res;
		MapType = mapType;
		_outputBlockName = mapType.OutputBlock;
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.Add(new PlotOption("Plot 2D Hydraulic Connectivity"));
		staticPlotOptions.Add(new PlotOption("Plot 2D Water Surface Gradient", "Plot 2D Water Surface Gradient (Arrow: WSEL High->Low)"));
		staticPlotOptions.Add(new PlotOption("Draw Map Values"));
		staticPlotOptions.Add(new PlotOption("Draw Perpendicular Face Velocities", "Draw Perpendicular Face Velocities"));
		staticPlotOptions.Add(new PlotOption("Draw True Face Velocities (interpolated)"));
		staticPlotOptions.Add(new PlotOption("Face Low-Elevation Centroid"));
		staticPlotOptions.Add(new PlotOption("Display Arrival Times as Dates"));
		staticPlotOptions.Add(new PlotOption("Plot Model Boundary Deficiencies"));
		return staticPlotOptions;
	}

	public override bool LayerHasData(H5Reader hr = null)
	{
		return true;
	}

	public override List<string> GetAllSourceFiles()
	{
		return StoredMapFilenames();
	}

	public List<string> StoredMapFilenames()
	{
		List<string> stringList = new List<string>();
		if (_outputMode.IsRaster)
		{
			if (File.Exists(VRTFilename))
			{
				stringList.AddRange(GDALRaster.GetVRTSources(VRTFilename));
			}
		}
		else if (_outputMode.IsVector && File.Exists(SHPFilename))
		{
			stringList.Add(SHPFilename);
			string changeExtension = System.IO.Path.ChangeExtension(SHPFilename, "dbf");
			if (File.Exists(changeExtension))
			{
				stringList.Add(changeExtension);
			}
			string changeExtension2 = System.IO.Path.ChangeExtension(SHPFilename, "shx");
			if (File.Exists(changeExtension2))
			{
				stringList.Add(changeExtension2);
			}
			string changeExtension3 = System.IO.Path.ChangeExtension(SHPFilename, "prj");
			if (File.Exists(changeExtension3))
			{
				stringList.Add(changeExtension3);
			}
		}
		return stringList;
	}

	public bool ResampleDynamicStateless(RasterM raster, int profileIndex, ref float[] retnValues, ProgressReporter errReporter = null)
	{
		if (errReporter == null)
		{
			errReporter = ProgressReporter.None();
		}
		CacheCollection mapCache = SharedData.GetMapCache(this);
		RASGeometry geometry = Results.Geometry;
		TerrainLayer terrain = Results.Geometry.Terrain;
		if (terrain == null)
		{
			errReporter.ReportError("No terrain association was loaded.");
			return false;
		}
		float[] values = null;
		terrain.Resample(raster, ref values);
		RASGeometryMapPoints rASGeometryMapPoints = geometry.MapPixels(raster);
		if (rASGeometryMapPoints.InputCount == -1)
		{
			errReporter.ReportError("Unknown geometry load error, is " + System.IO.Path.GetFileName(Results.SourceFilename) + " valid?");
			return false;
		}
		if (MapType == MapTypes.Arbitrary2D)
		{
			if (profileIndex >= 0 && profileIndex < int.MaxValue)
			{
				string layerName = LayerName;
				float[] terrain2 = values;
				float[] vx = null;
				float[] vy = null;
				AuxillaryRenderer.Compute(this, layerName, profileIndex, rASGeometryMapPoints, terrain2, ref retnValues, ref vx, ref vy, mapCache);
			}
		}
		else
		{
			Results.ComputeSwitch(this, rASGeometryMapPoints, profileIndex, values, mapCache, ref retnValues);
		}
		if (retnValues.IsNullOrEmpty())
		{
			errReporter.ReportError("Unknown error resampling values.");
			return false;
		}
		return true;
	}

	private byte[] ComputeSourceDataHash()
	{
		Hash hash = new Hash();
		hash.Add((IList<PointM>)new PointM[0]);
		throw new NotImplementedException();
	}

	public bool StoreMap(ProgressReporter reporter = null, bool showFinishedMessage = true)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (_outputMode.IsDynamic)
		{
			return false;
		}
		if (!EnsureDataExists())
		{
			reporter.ReportMessage("Generating stored map failed, do the results exist?");
			return false;
		}
		string outputFolder = Results.OutputFolder;
		if (string.IsNullOrEmpty(outputFolder))
		{
			reporter.ReportMessage("Generating stored map failed, do the results exist?");
			return false;
		}
		if (!Directory.Exists(outputFolder))
		{
			Directory.CreateDirectory(outputFolder);
		}
		ErrorMessage = "";
		RASGeometry geometry = Results.Geometry;
		geometry.D2FlowArea.FeatureTable();
		geometry.XSInterpolationSurface.FeatureTable();
		geometry.StorageArea.FeatureTable();
		reporter.ReportMessage("Generating stored map: '" + Description + "'");
		if (_outputMode.Equals(OutputModes.StoredDefaultTerrain) || _outputMode.Equals(OutputModes.StoredSpecifiedTerrain) || _outputMode.Equals(OutputModes.StoredPolygonSpecifiedDepth))
		{
			TerrainLayer terrain = Terrain;
			if (terrain != null)
			{
				if (_outputMode.Equals(OutputModes.StoredPolygonSpecifiedDepth))
				{
					StoreMapPolygonsParallel(geometry, terrain, reporter, FORCE_POLYGON_SERIAL);
					ClearFeatureTable();
				}
				else
				{
					string storedMapBaseFilename = StoredMapBaseFilename;
					MapProcessingEngine.StoreMap(this, terrain, storedMapBaseFilename, reporter);
				}
			}
			else
			{
				ErrorMessage = "Associated terrain is undefined or unavailable.";
			}
		}
		else if (_outputMode.Equals(OutputModes.StoredPoint))
		{
			StoreMapWritePoints(StoredMapBaseFilename, reporter);
		}
		if (showFinishedMessage)
		{
			if (Operators.CompareString(ErrorMessage, "", TextCompare: false) == 0)
			{
				reporter.ReportMessage("Stored map '" + System.IO.Path.GetFileName(StoredMapBaseFilename) + "' created.");
				MessageBox.Show("Stored map '" + System.IO.Path.GetFileName(StoredMapBaseFilename) + "' created.", "Success", MessageBoxButtons.Ok, MessageBoxIcon.Information);
			}
			else
			{
				reporter.ReportMessage("Error Generating stored map: " + ErrorMessage);
				MessageBox.Show("Error Generating stored map: " + ErrorMessage, "Failure", MessageBoxButtons.Ok, MessageBoxIcon.Error_);
			}
		}
		return Operators.CompareString(ErrorMessage, "", TextCompare: false) == 0;
	}

	private void StoreMapPolygonsParallel(RASGeometry geometry, TerrainLayer terrain, ProgressReporter reporter, bool forceSerial = false)
	{
		_Closure_0024__78_002D0 capturedLocals = default;
		_Closure_0024__78_002D0 capturedLocals2 = new _Closure_0024__78_002D0(capturedLocals);
		capturedLocals2.rasResultsMap = this;
		capturedLocals2.geometry = geometry;
		capturedLocals2.terrain = terrain;
		capturedLocals2.reporter = reporter;
		capturedLocals2.reporter.ReportMessage("Pre-loading Data...");
		capturedLocals2.reporter.ReportProgress(0);
		int pixelRenderingCutoff = MeshFV2D.PixelRenderingCutoff;
		MeshFV2D.PixelRenderingCutoff = 0;
		capturedLocals2.totalProgress = 0;
		capturedLocals2.prevOverallProgress = 0;
		int[] rasterFilesIndividualTileCount = capturedLocals2.terrain.RasterFilesIndividualTileCount;
		checked
		{
			capturedLocals2.fileProgress = new float[rasterFilesIndividualTileCount.Count() - 1 + 1];
			int count = capturedLocals2.fileProgress.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals2.fileProgress[i] = (float)((double)rasterFilesIndividualTileCount[i] / (double)capturedLocals2.terrain.RasterFilesTileCount);
			}
			capturedLocals2.ftrLock = RuntimeHelpers.GetObjectValue(new object());
			capturedLocals2.h5Lock = RuntimeHelpers.GetObjectValue(new object());
			capturedLocals2.bufLock = RuntimeHelpers.GetObjectValue(new object());
			capturedLocals2.geometry.D2FlowArea.FeatureTable();
			capturedLocals2.geometry.XSInterpolationSurface.FeatureTable();
			capturedLocals2.geometry.StorageArea.FeatureTable();
			foreach (MeshFV2D meshe in capturedLocals2.geometry.D2FlowArea.Meshes())
			{
				SharedData.GetMeshCellSpatialIndex(meshe);
				SharedData.GetMeshCellAreas(meshe);
				meshe.EnsureFaceInvLengths();
			}
			ThreadLocal<RASGeometryMapPoints> threadLocal = new ThreadLocal<RASGeometryMapPoints>([SpecialName] () => new RASGeometryMapPoints(capturedLocals2.geometry));
			_ = threadLocal.Value;
			capturedLocals2.tlContours = new ThreadLocal<ContourBandRaster>([SpecialName] () => new ContourBandRaster(SHPFilename, ContourPolygonValue, float.MaxValue));
			capturedLocals2.savedContours = new ConcurrentBag<ContourBandRaster>();
			capturedLocals2.unusedBuffers = new Stack<float[]>();
			Func<float[]> getReusableBuffer = [SpecialName] () =>
			{
				object bufLock = capturedLocals2.bufLock;
				ObjectFlowControl.CheckForSyncLockOnValueType(bufLock);
				bool lockTaken2 = false;
				try
				{
					Monitor.Enter(bufLock, ref lockTaken2);
					if (capturedLocals2.unusedBuffers.Count > 0)
					{
						return capturedLocals2.unusedBuffers.Pop();
					}
					return new float[66049];
				}
				finally
				{
					if (lockTaken2)
					{
						Monitor.Exit(bufLock);
					}
				}
			};
			Action<float[]> returnReusableBuffer = [SpecialName] (float[] buf) =>
			{
				object bufLock2 = capturedLocals2.bufLock;
				ObjectFlowControl.CheckForSyncLockOnValueType(bufLock2);
				bool lockTaken2 = false;
				try
				{
					Monitor.Enter(bufLock2, ref lockTaken2);
					if (capturedLocals2.unusedBuffers.Count <= 256)
					{
						capturedLocals2.unusedBuffers.Push(buf);
					}
				}
				finally
				{
					if (lockTaken2)
					{
						Monitor.Exit(bufLock2);
					}
				}
			};
			capturedLocals2.cache = SharedData.GetMapCache(this);
			if (MapType == MapTypes.Depth)
			{
				capturedLocals2.computer = DepthRenderer.GetComputer(Results, ProfileIndex, capturedLocals2.cache, getReusableBuffer, returnReusableBuffer, AllowNegativeDepths: true);
			}
			else
			{
				capturedLocals2.computer = Renderer.GetSimplifiedComputer(capturedLocals2.cache, ProfileIndex, MapType, Results, getReusableBuffer, returnReusableBuffer);
			}
			capturedLocals2.tin = capturedLocals2.terrain.Level0StitchTIN();
			_Closure_0024__78_002D3 capturedLocals3 = new _Closure_0024__78_002D3(null);
			capturedLocals3.outerClosure = capturedLocals2;
			capturedLocals3.hr = new H5Reader(capturedLocals3.outerClosure.terrain.SourceFilename);
			try
			{
				int count2 = capturedLocals3.outerClosure.terrain.RasterFileCount - 1;
				_Closure_0024__78_002D2 capturedLocals4 = default;
				_Closure_0024__78_002D4 capturedLocals5 = default;
				_Closure_0024__78_002D1 capturedLocals6 = default;
				for (int ordinal = 0; ordinal <= count2; ordinal++)
				{
					capturedLocals4 = new _Closure_0024__78_002D2(capturedLocals4);
					capturedLocals4.outerClosure = capturedLocals3;
					capturedLocals4.rastL = ordinal;
					capturedLocals5 = new _Closure_0024__78_002D4(capturedLocals5);
					capturedLocals5.outerClosure = capturedLocals4;
					capturedLocals5.tr = new FloatTiffReader(capturedLocals5.outerClosure.outerClosure.outerClosure.terrain.get_RasterFilename(ordinal));
					try
					{
						capturedLocals6 = new _Closure_0024__78_002D1(capturedLocals6);
						capturedLocals6.outerClosure = capturedLocals5;
						capturedLocals6.rfi = capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.terrain.RasterFileInfo(ordinal);
						capturedLocals6.nTiles = capturedLocals6.outerClosure.tr.GetNumTiles(0);
						RasterM binRaster = capturedLocals6.rfi.TileRasters[0];
						capturedLocals6.spIdx = new SpatialIndex<int>(binRaster);
						if (capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.tin != null)
						{
							int count3 = capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.tin.PointMCount() - 1;
							for (int ordinal2 = 0; ordinal2 <= count3; ordinal2++)
							{
								capturedLocals6.spIdx.AddInBounds(capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.tin.PointM(ordinal2), ordinal2);
							}
						}
						capturedLocals6.tilesCompleted = 0;
						capturedLocals6.prevProgressWithin = 0;
						capturedLocals6.progressLock = RuntimeHelpers.GetObjectValue(new object());
						capturedLocals6.tileDone = capturedLocals6._Lambda_0024__4;
						capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.reporter.ReportMessage("File " + (capturedLocals6.outerClosure.outerClosure.rastL + 1) + " of " + capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.terrain.RasterFileCount + ": 0% processed");
						capturedLocals6.tlRenderTile = new ThreadLocal<TerrainLayer.RenderTileData>(capturedLocals6._Lambda_0024__5);
						ParallelOptions parallelOptions = new ParallelOptions();
						if (forceSerial)
						{
							parallelOptions.MaxDegreeOfParallelism = 1;
						}
						System.Threading.Tasks.Parallel.For(0, capturedLocals6.outerClosure.tr.GetNumTiles(0), parallelOptions, capturedLocals6._Lambda_0024__6);
						capturedLocals6.tlRenderTile.Dispose();
						capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.totalProgress = capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.totalProgress + (int)System.Math.Round(capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.fileProgress[capturedLocals6.outerClosure.outerClosure.rastL] * 100f);
						capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.reporter.ReportMessage("File " + (capturedLocals6.outerClosure.outerClosure.rastL + 1) + " of " + capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.terrain.RasterFileCount + ": 100% processed");
					}
					finally
					{
						if (capturedLocals5.tr != null)
						{
							((IDisposable)capturedLocals5.tr).Dispose();
						}
					}
				}
			}
			finally
			{
				if (capturedLocals3.hr != null)
				{
					((IDisposable)capturedLocals3.hr).Dispose();
				}
			}
			if (capturedLocals2.tin != null)
			{
				_Closure_0024__78_002D5 capturedLocals7 = default;
				_Closure_0024__78_002D5 capturedLocals8 = new _Closure_0024__78_002D5(capturedLocals7);
				capturedLocals8.outerClosure = capturedLocals2;
				capturedLocals8.outerClosure.reporter.ReportMessage("Processing Terrain Stitch TIN");
				capturedLocals8.outerClosure.reporter.ReportProgress(0);
				RASGeometryMapPoints rASGeometryMapPoints = capturedLocals8.outerClosure.geometry.MapPixels(capturedLocals8.outerClosure.tin);
				capturedLocals8.terrainValues = new float[capturedLocals8.outerClosure.tin.Count - 1 + 1];
				Utility.Parallel.SmartFor(capturedLocals8.outerClosure.tin.Count, [SpecialName] (int start, int finish) =>
				{
					for (int j = start; j <= finish; j++)
					{
						capturedLocals8.terrainValues[j] = (float)capturedLocals8.outerClosure.tin.PointM(j).Z;
					}
				});
				float[] values = null;
				if (capturedLocals8.outerClosure.computer == null)
				{
					object h5Lock = capturedLocals8.outerClosure.h5Lock;
					ObjectFlowControl.CheckForSyncLockOnValueType(h5Lock);
					bool lockTaken = false;
					try
					{
						Monitor.Enter(h5Lock, ref lockTaken);
						Results.ComputeSwitch(this, rASGeometryMapPoints, ProfileIndex, capturedLocals8.terrainValues, capturedLocals8.outerClosure.cache, ref values);
					}
					finally
					{
						if (lockTaken)
						{
							Monitor.Exit(h5Lock);
						}
					}
				}
				else
				{
					values = capturedLocals8.outerClosure.computer(rASGeometryMapPoints, capturedLocals8.terrainValues);
				}
				capturedLocals8.outerClosure.savedContours.First().ContourTINValues(capturedLocals8.outerClosure.tin, values, capturedLocals8.outerClosure.reporter);
				capturedLocals8.outerClosure.reporter.ReportProgress(100);
			}
			MeshFV2D.PixelRenderingCutoff = pixelRenderingCutoff;
			threadLocal.Dispose();
			capturedLocals2.tlContours.Dispose();
			capturedLocals2.reporter.ReportProgress(0);
			capturedLocals2.reporter.ReportMessage("Final Polygon Merge... (Waiting on external process)");
			capturedLocals2.cache = null;
			capturedLocals2.computer = null;
			SharedData.ClearAllCaches();
			ContourBandRaster.MergeContoursWriteShapefile(capturedLocals2.savedContours.ToList());
		}
	}

	private void StoreMapWritePoints(string mapBaseFilename, ProgressReporter reporter)
	{
		reporter.ReportProgress(0);
		ComputePointMapValues(reporter.WithProgressModifier(0, 0.8f), forceCompute: true);
		if (!reporter.CancelRequested)
		{
			reporter.ReportMessage("Saving to disk...");
			SourceFilename = SHPFilename;
			SaveFeatureTable();
			reporter.ReportMessage("Done!");
			reporter.ReportProgress(100);
		}
	}

	private void ComputePointMapValues(ProgressReporter reporter, bool forceCompute = false)
	{
		reporter.ReportMessage("Querying Geometry...");
		reporter.ReportProgress(0);
		EnsurePointFeatureLayer(reporter.WithProgressModifier(0, 0.7f), forceCompute);
		if (_pointMPS == null || reporter.CancelRequested)
		{
			return;
		}
		reporter.ReportMessage("Computing Map Values...");
		CacheCollection cache = new CacheCollection();
		checked
		{
			float[] values = new float[_points.Count - 1 + 1];
			Results.ComputeSwitch(this, _pointMPS, ProfileIndex, _pointsZ, cache, ref values);
			if (reporter.CancelRequested)
			{
				return;
			}
			reporter.ReportProgress(95);
			DataTable dataTable = FeatureTable();
			int columnIndex = dataTable.Columns.IndexOf("Map Value");
			int count = dataTable.Rows.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				DataRow dataRow = FeatureRow(i);
				if (values[i] == -9999f)
				{
					dataRow[columnIndex] = DBNull.Value;
				}
				else
				{
					dataRow[columnIndex] = System.Math.Round(values[i], SharedData.VerticalDecimalPlaces);
				}
			}
			reporter.ReportProgress(100);
		}
	}

	public Stats ComputeSummaryStats(int nLevelsFromTop, ProgressReporter reporter = null)
	{
		_Closure_0024__81_002D0 capturedLocals = default;
		_Closure_0024__81_002D0 capturedLocals2 = new _Closure_0024__81_002D0(capturedLocals);
		capturedLocals2.rasResultsMap = this;
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		capturedLocals2.geometry = Results.Geometry;
		TerrainLayer terrain = Terrain;
		List<Extent> extents = new List<Extent>();
		if (capturedLocals2.geometry.StorageArea != null && capturedLocals2.geometry.StorageArea.Extent != null)
		{
			extents.Add(capturedLocals2.geometry.StorageArea.Extent);
		}
		if (capturedLocals2.geometry.D2FlowArea != null && capturedLocals2.geometry.D2FlowArea.Extent != null)
		{
			extents.Add(capturedLocals2.geometry.D2FlowArea.Extent);
		}
		if (capturedLocals2.geometry.XSInterpolationSurface != null && capturedLocals2.geometry.XSInterpolationSurface.Extent != null)
		{
			extents.Add(capturedLocals2.geometry.XSInterpolationSurface.Extent);
		}
		capturedLocals2.pfCount = 0;
		if (OutputBlock != null)
		{
			capturedLocals2.pfCount = OutputBlock.ProfileCount;
		}
		if (!MapType.CanAnimate)
		{
			capturedLocals2.pfCount = 1;
		}
		capturedLocals2.tileValues = null;
		capturedLocals2.cache = SharedData.GetMapCache(this);
		capturedLocals2.allStats = new List<Stats>();
		if (MapType == MapTypes.ArrivalTime)
		{
			capturedLocals2.cache.GetColumnCache<float>(Results.GetPostProcessor().Filename).CacheSizePerDataset = 50f;
		}
		string tempFileName = System.IO.Path.GetTempFileName();
		tempFileName = System.IO.Path.ChangeExtension(tempFileName, "hdf");
		checked
		{
			using (H5Writer h5Writer = new H5Writer(tempFileName))
			{
				int ordinal = 10;
				int ordinal2 = 65536;
				string datasetName = "/data";
				h5Writer.Create2dExtendibleDataset(datasetName, typeof(float), ordinal, ordinal2);
				_ = new float[ordinal - 1 + 1, ordinal2 - 1 + 1];
				Action<RasterM, float[]> perTileAction = [SpecialName] (RasterM tileRasterM, float[] terrainData) =>
				{
					RASGeometryMapPoints rASGeometryMapPoints = capturedLocals2.geometry.MapPixels(tileRasterM);
					if (rASGeometryMapPoints.MappedPixelCount != 0)
					{
						List<Stats> stats = new List<Stats>();
						if (!capturedLocals2.rasResultsMap.MapType.CanAnimate)
						{
							throw new NotImplementedException();
						}
						float[][] pointTimeSeries = null;
						capturedLocals2.rasResultsMap.Results.ComputeSwitchTimeSeries(capturedLocals2.rasResultsMap, rASGeometryMapPoints, terrainData, capturedLocals2.cache, ref pointTimeSeries, convertToGraphableData: false);
						float[][] pointTimeSeriesArray = pointTimeSeries;
						foreach (float[] pointTimeSery in pointTimeSeriesArray)
						{
							stats.Add(Stats.Compute(pointTimeSery, -9999f, pointTimeSery.Length, 1, pointTimeSery.Length, 1));
						}
						capturedLocals2.allStats.Add(Stats.Combine(stats));
					}
				};
				terrain.ComputeGenericFromTop(nLevelsFromTop, perTileAction, extents, reporter);
			}
			File.Delete(tempFileName);
			return Stats.Combine(capturedLocals2.allStats);
		}
	}

	public bool MinMaxDatasetExists()
	{
		if (MapType == MapTypes.Elevation)
		{
			return true;
		}
		if (MapType is Generic2DMapType)
		{
			return Results.MinMaxDatasetExists(this, ((Generic2DMapType)MapType).HDF5Dataset);
		}
		return false;
	}

	public void GetColorStatsOrReasonableDefaults(ref double min, ref double max)
	{
		min = double.NaN;
		max = double.NaN;
		if (Results == null)
		{
			return;
		}
		MapTypes mapType = MapType;
		if (mapType == MapTypes.Elevation)
		{
			float min2 = (float)min;
			float max2 = (float)max;
			Results.TryGetMinMaxWS(ref min2, ref max2);
			min = min2;
			max = max2;
		}
		else
		{
			if (mapType == MapTypes.Depth || mapType == MapTypes.Velocity || mapType == MapTypes.Flow)
			{
				return;
			}
			if (mapType == MapTypes.FractionInundated)
			{
				min = 0.0;
				max = 1.0;
			}
			else if (!(mapType == MapTypes.ShearStress))
			{
				if (MapType is Generic2DMapType)
				{
					float min3 = (float)min;
					float max3 = (float)max;
					Results.TryGetMinMax(this, ((Generic2DMapType)MapType).HDF5Dataset, ref min3, ref max3);
					min = min3;
					max = max3;
				}
				else
				{
					min = double.NaN;
					max = double.NaN;
				}
			}
		}
	}

	public List<float[]> GetTimeSeriesValues(RASGeometryMapPoints selectedPixels, float[] terrain)
	{
		if (!OutputModeAnimateable)
		{
			return null;
		}
		RASResults results = Results;
		_ = results.Geometry.XSInterpolationSurface;
		float[][] pointTimeSeries = null;
		if (results.ComputeSwitchTimeSeries(this, selectedPixels, terrain, SharedData.GetMapCache(this), ref pointTimeSeries, convertToGraphableData: true, SharedData.RasMapper != null))
		{
			return pointTimeSeries.ToList();
		}
		return null;
	}

	public bool EnsureDataExists()
	{
		if (File.Exists(Results.SourceFilename))
		{
			return true;
		}
		return false;
	}

	private void EnsurePointFeatureLayer(ProgressReporter reporter = null, bool forceCompute = false)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		reporter.ReportProgress(0);
		if (!forceCompute && FeatureTable().Rows.Count > 0 && _points != null && _points.Count == FeatureTable().Rows.Count)
		{
			return;
		}
		if (Operators.CompareString(_pointLayername, "", TextCompare: false) == 0)
		{
			ErrorMessage = "Point layer not specified";
			return;
		}
		FeatureLayer featureLayer = null;
		if (SharedData.RasMapper != null)
		{
			featureLayer = SharedData.RasMapper.MapLayersGroup.FindLayer(_pointLayername) as FeatureLayer;
			if (featureLayer == null)
			{
				featureLayer = SharedData.RasMapper.FeaturesGroup.FindLayer(_pointLayername) as FeatureLayer;
			}
		}
		else
		{
			featureLayer = RasMapperCom.GetMapLayerFromXML(SharedData.RasMapDoc, _pointLayername);
		}
		if (featureLayer == null)
		{
			ErrorMessage = "Unable to locate point layer '" + _pointLayername + "'";
			return;
		}
		if (featureLayer.FeatureCount() == 0)
		{
			ErrorMessage = "Zero points loaded from  layer '" + _pointLayername + "'";
			return;
		}
		ClearFeatureTable();
		List<string> allVirtualizingFiles = GetAllVirtualizingFiles();
		foreach (string allVirtualizingFile in allVirtualizingFiles)
		{
			if (File.Exists(allVirtualizingFile))
			{
				try
				{
					File.Delete(allVirtualizingFile);
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					ErrorMessage = "Need to delete the file " + allVirtualizingFile + " to update this map, but cannot delete it.";
					ProjectData.ClearProjectError();
					return;
				}
			}
		}
		CopyFeaturesAndAttributes(featureLayer);
		if (_points == null)
		{
			_points = new PointMs();
		}
		else
		{
			_points.Clear();
		}
		_points.Capacity = featureLayer.FeatureCount();
		foreach (Feature feature in Features())
		{
			if (feature != null)
			{
				_points.Add(feature.LabelPoint(this));
			}
		}
		checked
		{
			if (_points.Count > 0)
			{
				_pointsZ = new float[_points.Count - 1 + 1];
			}
			DataTable dataTable = FeatureTable();
			if ((_elevationSource == ElevationSources.Terrain) | (_elevationSource == ElevationSources.AttributeColumn))
			{
				if (_elevationSource == ElevationSources.Terrain)
				{
					TerrainLayer terrain = Results.Geometry.Terrain;
					if (terrain == null)
					{
						ErrorMessage = "'Terrain' is specified as the source for these point's z values, but a terrain is not associated with this geometry";
						return;
					}
					terrain.ComputePointElevations(featureLayer.Extent, _points, _pointsZ);
				}
				else if (_elevationSource == ElevationSources.AttributeColumn)
				{
					int indexOf = dataTable.Columns.IndexOf(_elevationColumnName);
					if (indexOf == -1)
					{
						ErrorMessage = "Column named '" + _elevationColumnName + "' for the elevations was not found in the specified point layer";
						return;
					}
					int count = dataTable.Rows.Count - 1;
					for (int i = 0; i <= count; i++)
					{
						_pointsZ[i] = Conversions.ToSingle(dataTable.Rows[i][indexOf]);
					}
				}
			}
			reporter.ReportProgress(10);
			_pointMPS = new RASGeometryMapPoints(Results.Geometry);
			_pointMPS.ComputeForPoints(_points, reportTime: false, reporter.WithProgressModifier(10, 0.9f));
			if (!dataTable.Columns.Contains("Map Value"))
			{
				dataTable.Columns.Add("Map Value", typeof(float));
			}
			reporter.ReportProgress(100);
		}
	}

	public string GetArrivalTimeProfile(float valueInDays)
	{
		double arrivalStartingSimulationTime = ArrivalStartingSimulationTime;
		DateTime dt = Results.SimulationStart.AddDays(arrivalStartingSimulationTime + (double)valueInDays);
		int closestProfileIndex = OutputBlock.ClosestProfileIndex(DSS.ConvertToDSSDateTime(dt));
		if (closestProfileIndex < 0 || closestProfileIndex == int.MaxValue)
		{
			return SharedData.AppropriateDatetimeString(dt, allowMS: false);
		}
		return Results.ProfileNames[closestProfileIndex];
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement layernode)
	{
		base.XMLSaveMetadata(doc, layernode);
		XmlElement xmlElement = doc.CreateElement("MapParameters");
		layernode.AppendChild(xmlElement);
		MapType?.XMLSave(xmlElement);
		if (!string.IsNullOrEmpty(_baseLayerName))
		{
			xmlElement.SetAttribute("LayerName", _baseLayerName);
		}
		if (!_outputMode.Equals(OutputModes.DynamicSurface))
		{
			xmlElement.SetAttribute("OutputMode", _outputMode.Description);
		}
		if (OutputModeIsStoredType)
		{
			xmlElement.SetAttribute("StoredFilename", RasMapperLib.Utilities.Path.MakeRelative(SourceFilename, SharedData.RasMapFilename));
		}
		if (Operators.CompareString(_overwriteTerrainLayerName, "", TextCompare: false) != 0)
		{
			xmlElement.SetAttribute("Terrain", _overwriteTerrainLayerName);
		}
		if (Operators.CompareString(_pointLayername, "", TextCompare: false) != 0)
		{
			xmlElement.SetAttribute("PointLayername", _pointLayername);
		}
		if ((_elevationSource != ElevationSources.Terrain) & (_elevationSource != (ElevationSources)int.MinValue))
		{
			int elevationSource = (int)_elevationSource;
			xmlElement.SetAttribute("ElevationSource", elevationSource.ToString());
		}
		if (Operators.CompareString(_elevationColumnName, "", TextCompare: false) != 0)
		{
			xmlElement.SetAttribute("ElevationColumn", _elevationColumnName);
		}
		if (ContourPolygonValue != 0f)
		{
			xmlElement.SetAttribute("ContourPolygonValue", ContourPolygonValue.ToString());
		}
		xmlElement.SetAttribute("ProfileIndex", _profileIndex.ToString());
		xmlElement.SetAttribute("ProfileName", ProfileName);
		if (!string.IsNullOrEmpty(_outputBlockName))
		{
			xmlElement.SetAttribute("OutputBlock", _outputBlockName);
		}
		if (!float.IsNaN(_depthThreshold))
		{
			xmlElement.SetAttribute("ArrivalDepth", _depthThreshold.ToString());
		}
		if (_arrivalStartMode != ArrivalStartModes.AtBeginning)
		{
			int elevationSource = (int)_arrivalStartMode;
			xmlElement.SetAttribute("ArrivalStartMode", elevationSource.ToString());
		}
		if (!float.IsNaN(_arrivalSimDays))
		{
			xmlElement.SetAttribute("ArrivalSimDays", _arrivalSimDays.ToString());
		}
		if (!float.IsNaN(_arrivalSimHours))
		{
			xmlElement.SetAttribute("ArrivalSimHours", _arrivalSimHours.ToString());
		}
		if (!float.IsNaN(_arrivalSimMinutes))
		{
			xmlElement.SetAttribute("ArrivalSimMinutes", _arrivalSimMinutes.ToString());
		}
		if (Operators.CompareString(_arrivalAbsDateTime, "", TextCompare: false) != 0)
		{
			xmlElement.SetAttribute("ArrivalAbsDateTime", _arrivalAbsDateTime);
		}
		if (!_useHours)
		{
			xmlElement.SetAttribute("Hours", _useHours.ToString());
		}
		if (_arrivalAsDelta)
		{
			xmlElement.SetAttribute("ArrivalAsDelta", _arrivalAsDelta.ToString());
		}
	}

	protected override void XMLLoadMetadata(XmlElement xmllayer)
	{
		base.XMLLoadMetadata(xmllayer);
		XmlNode xmlNode = xmllayer.SelectSingleNode("MapParameters");
		if (xmlNode != null)
		{
			_mapType = MapTypes.LoadFromXML(xmlNode);
			_baseLayerName = XML.StringAttribute(xmlNode, "LayerName");
			_outputMode = OutputModes.LoadFromDescription(XML.StringAttribute(xmlNode, "OutputMode"));
			if (_outputMode.Equals(OutputModes.Unknown))
			{
				_outputMode = OutputModes.DynamicSurface;
			}
			_overwriteTerrainLayerName = XML.StringAttribute(xmlNode, "Terrain");
			_pointLayername = XML.StringAttribute(xmlNode, "PointLayername");
			_elevationSource = (ElevationSources)XML.IntegerAttribute(xmlNode, "ElevationSource", 0);
			_elevationColumnName = XML.StringAttribute(xmlNode, "ElevationColumn");
			ContourPolygonValue = XML.SingleAttribute(xmlNode, "ContourPolygonValue", 0f);
			if (XML.BooleanAttribute(xmlNode, "MaxDepth"))
			{
				SetToMaxProfile();
			}
			else if (XML.BooleanAttribute(xmlNode, "MinDepth"))
			{
				SetToMinProfile();
			}
			else
			{
				int indexFromXML = XML.IntegerAttribute(xmlNode, "ProfileIndex", 0);
				string nameFromXML = XML.StringAttribute(xmlNode, "ProfileName");
				_profileIndex = Results.SelectBestProfileIndex(indexFromXML, nameFromXML);
			}
			_outputBlockName = XML.StringAttribute(xmlNode, "OutputBlock");
			_depthThreshold = XML.SingleAttribute(xmlNode, "ArrivalDepth");
			_arrivalStartMode = (ArrivalStartModes)XML.IntegerAttribute(xmlNode, "ArrivalStartMode", 0);
			_arrivalSimDays = XML.SingleAttribute(xmlNode, "ArrivalSimDays");
			_arrivalSimHours = XML.SingleAttribute(xmlNode, "ArrivalSimHours");
			_arrivalSimMinutes = XML.SingleAttribute(xmlNode, "ArrivalSimMinutes");
			_arrivalAbsDateTime = XML.StringAttribute(xmlNode, "ArrivalAbsDateTime");
			_arrivalAsDelta = XML.BooleanAttribute(xmlNode, "ArrivalAsDelta");
			_useHours = XML.BooleanAttribute(xmlNode, "Hours", defaultValue: true);
		}
	}

	public void SetToMaxProfile()
	{
		if (OutputModeAnimateable && !MapType.SupportsDynamicMinMaxProfile)
		{
			ProfileIndex = -1;
		}
		else
		{
			ProfileIndex = int.MaxValue;
		}
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
		if (OutputModeAnimateable && !MapType.SupportsDynamicMinMaxProfile)
		{
			ProfileIndex = -1;
		}
		else
		{
			ProfileIndex = int.MinValue;
		}
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}

	public void TrySetProfile(string profileName)
	{
		int profileIndex = OutputBlock.ClosestProfileIndex(profileName);
		if (profileIndex == int.MaxValue || profileIndex == int.MinValue)
		{
			ProfileIndex = profileIndex;
		}
		else if (profileIndex >= 0 && profileIndex < OutputBlock.ProfileNames.Count())
		{
			if (string.Equals(OutputBlock.ProfileNames[profileIndex], profileName, StringComparison.InvariantCultureIgnoreCase))
			{
				ProfileIndex = profileIndex;
				return;
			}
			DateTime dt = default;
			bool tryParseTime = TryParseTime(Results.ProfileNames[profileIndex], ref dt);
			DateTime dt2 = default;
			bool tryParseTime2 = TryParseTime(profileName, ref dt2);
			if (tryParseTime && tryParseTime2 && DateTime.Compare(dt, dt2) == 0)
			{
				ProfileIndex = profileIndex;
			}
			else
			{
				ProfileIndex = -1;
			}
		}
		else
		{
			ProfileIndex = -1;
		}
	}

	void IAnimatingLayer.TrySetProfile(string profileName)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profileName);
	}

	private static bool TryParseTime(string pfName, ref DateTime dt)
	{
		if (DateTime.TryParse(pfName, out dt))
		{
			return true;
		}
		try
		{
			if (Time.TryConvertFromHecDateTime(pfName, out dt))
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
		if (DateTime.TryParseExact(pfName, "ddMMMyyyy HH:mm:ss.fff", null, DateTimeStyles.None, out dt))
		{
			return true;
		}
		if (DateTime.TryParseExact(pfName, "ddMMMyyyy HH:mm:ss", null, DateTimeStyles.None, out dt))
		{
			return true;
		}
		if (DateTime.TryParseExact(pfName, "ddMMMyyyy HH:mm", null, DateTimeStyles.None, out dt))
		{
			return true;
		}
		return false;
	}

	public static string GetBaseDescription(MapTypes map, OutputModes mode)
	{
		if ((object)map == null)
		{
			map = MapTypes.Unknown;
		}
		string result = map.DefaultLayerName;
		if (map == MapTypes.Depth && mode == OutputModes.StoredPolygonSpecifiedDepth)
		{
			result = "Inundation Boundary";
		}
		return result;
	}

	public static string GetDescriptionParams(MapTypes map, OutputModes mode, string pfName, float depth, bool useHours, float polyContourBoundaryVal)
	{
		string descriptionParams = "";
		Action<string> action = [SpecialName] (string newParam) =>
		{
			if (descriptionParams.Length > 0)
			{
				descriptionParams += " ";
			}
			descriptionParams += newParam;
		};
		if (map.NeedsProfile)
		{
			action(pfName);
		}
		if (map == MapTypes.Depth)
		{
			if (mode == OutputModes.StoredPolygonSpecifiedDepth)
			{
				action("Value_" + polyContourBoundaryVal);
			}
		}
		else if (!(map == MapTypes.Elevation) && !(map == MapTypes.Flow) && !(map == MapTypes.Velocity) && !(map == MapTypes.DepthTimesVelocity) && (map == MapTypes.ArrivalTime || map == MapTypes.Duration || map == MapTypes.Recession))
		{
			if (!float.IsNaN(depth) && depth != 0f)
			{
				action(Strings.Format(depth) + SharedData.ShortLengthUnits);
			}
			if (useHours)
			{
				action("hrs");
			}
			else
			{
				action("days");
			}
		}
		if (Operators.CompareString(descriptionParams, "", TextCompare: false) != 0)
		{
			descriptionParams = " (" + descriptionParams + ")";
		}
		return descriptionParams;
	}

	public string GetDescriptionParams()
	{
		string descriptionParams = "";
		Action<string> action = [SpecialName] (string newParam) =>
		{
			if (descriptionParams.Length > 0)
			{
				descriptionParams += " ";
			}
			descriptionParams += newParam;
		};
		if (MapType.NeedsProfile)
		{
			action(ProfileName);
		}
		if (_outputMode == OutputModes.StoredPolygonSpecifiedDepth)
		{
			action("Value_" + ContourPolygonValue);
		}
		MapTypes mapType = MapType;
		if (!(mapType == MapTypes.Depth) && !(mapType == MapTypes.Elevation) && !(mapType == MapTypes.Flow) && !(mapType == MapTypes.Velocity) && !(mapType == MapTypes.DepthTimesVelocity))
		{
			if (mapType == MapTypes.ArrivalTime || mapType == MapTypes.Duration || mapType == MapTypes.Recession)
			{
				if (!float.IsNaN(_depthThreshold) && _depthThreshold != 0f)
				{
					action(Strings.Format(_depthThreshold) + SharedData.ShortLengthUnits);
				}
				string arrivalStartDescription = ArrivalStartDescription();
				if (!string.IsNullOrWhiteSpace(arrivalStartDescription))
				{
					action(arrivalStartDescription);
				}
				if (UseHours)
				{
					action("hrs");
				}
				else
				{
					action("days");
				}
				if (ArrivalAsDelta)
				{
					action("Delta");
				}
			}
			else if (mapType == MapTypes.FractionInundated)
			{
				if (!float.IsNaN(_depthThreshold) && _depthThreshold != 0f)
				{
					action(Strings.Format(_depthThreshold) + SharedData.ShortLengthUnits);
				}
				string arrivalStartDescription2 = ArrivalStartDescription();
				if (!string.IsNullOrWhiteSpace(arrivalStartDescription2))
				{
					action(arrivalStartDescription2);
				}
			}
		}
		if (Operators.CompareString(descriptionParams, "", TextCompare: false) != 0)
		{
			descriptionParams = " (" + descriptionParams + ")";
		}
		return descriptionParams;
	}

	private string ArrivalStartDescription()
	{
		if (_arrivalStartMode == ArrivalStartModes.AtBeginning)
		{
			return "";
		}
		if (_arrivalStartMode == ArrivalStartModes.UseSimTime)
		{
			string textValue = "";
			if (!float.IsNaN(_arrivalSimDays))
			{
				textValue = textValue + Strings.Format(_arrivalSimDays) + "d";
			}
			if (!float.IsNaN(_arrivalSimHours))
			{
				textValue = textValue + Strings.Format(_arrivalSimHours) + "h";
			}
			if (!float.IsNaN(_arrivalSimMinutes))
			{
				textValue = textValue + Strings.Format(_arrivalSimMinutes) + "m";
			}
			if (textValue.Length > 0)
			{
				return textValue;
			}
		}
		else if (((_arrivalStartMode == ArrivalStartModes.UseAbsDateTime) & !string.IsNullOrWhiteSpace(_arrivalAbsDateTime)) && DateTime.Compare(DSS.ConvertFromDSSDateTime(_arrivalAbsDateTime), DateTime.MinValue) != 0)
		{
			return _arrivalAbsDateTime;
		}
		return "";
	}
}
