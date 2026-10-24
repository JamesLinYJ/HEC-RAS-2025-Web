using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml;
using Geospatial.GDALAssist;
using H5Assist.Caching.VB;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Units;

namespace RasMapperLib;

public class SharedData
{
	public sealed class MapTypeSurfaceFill
	{
		public class MapTypeSurfaceFillTemporaryAllDefaults : IDisposable
		{
			private Dictionary<string, ISurfaceFill> _mapNameToSurfaceFillTemp;

			public MapTypeSurfaceFillTemporaryAllDefaults()
			{
				_mapNameToSurfaceFillTemp = new Dictionary<string, ISurfaceFill>();
				foreach (KeyValuePair<string, ISurfaceFill> mapNameToSurfaceFill in _mapNameToSurfaceFill)
				{
					_mapNameToSurfaceFillTemp[mapNameToSurfaceFill.Key] = mapNameToSurfaceFill.Value;
				}
				ResetDefaults();
			}

			public void Dispose()
			{
				foreach (KeyValuePair<string, ISurfaceFill> mapNameToSurfaceFillTemp in _mapNameToSurfaceFillTemp)
				{
					_mapNameToSurfaceFill[mapNameToSurfaceFillTemp.Key] = mapNameToSurfaceFillTemp.Value;
				}
			}

			void IDisposable.Dispose()
			{
				//ILSpy generated this explicit interface implementation from .override directive in Dispose
				this.Dispose();
			}
		}

		private static Dictionary<string, ISurfaceFill> _mapNameToSurfaceFill = new Dictionary<string, ISurfaceFill>();

		private MapTypeSurfaceFill()
		{
		}

		public static bool TryGetDefaultSurfaceFillForMapName(MapTypes mapType, ref ISurfaceFill sf)
		{
			string key = MapTypeToMapTypeName(mapType);
			if (_mapNameToSurfaceFill.ContainsKey(key))
			{
				sf = _mapNameToSurfaceFill[key];
				return true;
			}
			return false;
		}

		public static bool TryGetDefaultSurfaceFillForLayer(Type lyrType, ref ISurfaceFill sf)
		{
			if (_mapNameToSurfaceFill.ContainsKey(lyrType.Name))
			{
				sf = _mapNameToSurfaceFill[lyrType.Name];
				return true;
			}
			return false;
		}

		public static void ResetDefaults()
		{
			_mapNameToSurfaceFill.Clear();
		}

		private static string MapTypeToMapTypeName(MapTypes mapType)
		{
			if (mapType is Generic2DMapType)
			{
				return ((Generic2DMapType)mapType).HDF5Dataset;
			}
			return mapType.DefaultLayerName;
		}
	}

	public class PlanStructure
	{
		public string Filename;

		public string Title;

		public string GeometryExtension;

		public string FlowExtension;

		public DateTime StartDate;

		public DateTime EndDate;

		public bool IsActivePlan;

		public PlanStructure(string fn, string title, string geomExt, string flowExt, DateTime startDate, DateTime endDate, bool isActivePlan)
		{
			Filename = fn;
			Title = title;
			GeometryExtension = geomExt;
			FlowExtension = flowExt;
			StartDate = startDate;
			EndDate = endDate;
			IsActivePlan = isActivePlan;
		}

		public PlanStructure(RASResults res)
		{
			Filename = res.SourceFilename;
			Title = res.Name;
			StartDate = res.PlanAttributes.StartTime;
			EndDate = res.PlanAttributes.EndTime;
		}
	}

	public enum CellRenderMethod
	{
		Pretty,
		AsComputed
	}

	public enum FaceWSMethod
	{
		Ben,
		Adjusted
	}

	public enum CellStencilMethod
	{
		JustFacepoints,
		WithFaces
	}

	public enum FacepointAdjustmentMethod
	{
		None,
		DragAverage
	}

	public enum ShallowBehavior
	{
		None,
		ReduceToHorizontal
	}

	public enum EnumRSUnits
	{
		Feet,
		Miles,
		Meters,
		Kilometers
	}

	public enum EnumUnits
	{
		USCustomary,
		SIUnits
	}

	public const double EpsilonDouble = 1E-15;

	public const float EpsilonSingle = 1E-06f;

	public const string TimeFormatMin = "ddMMMyyyy HH:mm";

	public const string TimeFormatSec = "ddMMMyyyy HH:mm:ss";

	public const string TimeFormatMS = "ddMMMyyyy HH:mm:ss.fff";

	public const float GravityUSC = 32.174f;

	public const float GravitySI = 9.80665f;

	public const string sFileType = "File Type";

	public const string sHecLandCover = "HEC Land Cover";

	public const string sHECTerrain = "HEC Terrain";

	public const string sHecRasGeometry = "HEC-RAS Geometry";

	public const string sHecRasResults = "HEC-RAS Results";

	private const string _NoSRSMessage = "Spatial Reference System for the project has not been set in RASMapper. (Goto menu item Tools/Set Project for Project)";

	private const string _SRSNotFound = "Spatial Reference System for the project has been set in RASMapper, but the file does not exist  (check under menu item Tools/Set Project for Project)";

	public const string sStartEditing = "Edit Layer";

	public const string sStopEditing = "Stop Editing";

	public static int OutputDecimalPlaces;

	public static int NearPointTolerance;

	public static int NearSegmentTolerance;

	private static EnumRSUnits _unitsRiverStation;

	private static bool _unitsRiverStationNeedsValue;

	public static int RiverStationDecimalPlaces;

	public static int HorizontalDecimalPlaces;

	public static int VerticalDecimalPlaces;

	public static bool DebugMode;

	public static int HWNDRas;

	public static int RasMapper64PID;

	public static string RasMapper64GUID;

	public static bool DebugEditingTools;

	public static bool Allow64BitHijack;

	public static bool StandAloneMode;

	public static bool ListenForSourceFileChanges;

	public static bool IsTestingUtility;

	private const int WM_SETTEXT = 12;

	public static int ZTrashValue;

	public static RASMapper RasMapper;

	public static bool RasMapperIsLoading;

	private static string _rasMapFilename;

	private static XmlDocument _rasMapDoc;

	public static List<Feature> CopiedFeatures;

	public static List<DataRow> CopiedFeaturesDataRows;

	public static List<WeakReference<DataRow>> CopiedFromDataRows;

	public static FeatureLayer CopiedFeatureLayer;

	public static int MaxNumberOfPointsOnXS;

	public static int MaxNumberOfPointsOnLS;

	public static string SRSFilename;

	public static bool ToWGS84;

	private static string _projectionFilename;

	private static Projection _projection;

	private static DateTime _projectionFileDate;

	private static EnumUnits _units;

	private static Dictionary<RASResults, CacheCollection> _mapCache;

	private static Dictionary<MeshFV2D, DistinctSpatialIndex> _meshCellSpatialIndexCache;

	private static Dictionary<MeshFV2D, DistinctSpatialIndex> _meshFaceSpatialIndexCache;

	private static Dictionary<MeshFV2D, DistinctSpatialIndex> _meshFacepointSpatialIndexCache;

	private static Dictionary<MeshFV2D, float[]> _meshCellAreaCache;

	private static Dictionary<MeshFV2D, DistinctSpatialIndex> _meshPerimeterCache;

	private static Dictionary<string, Dictionary<string, MeshFV2D>> _saveTheMeshCache;

	private static Dictionary<string, string> _savedFolders;

	public static List<PlanStructure> PlanStructures;

	public static CellRenderMethod CellRenderMode;

	public static FaceWSMethod FaceWSMode;

	public static CellStencilMethod CellStencilMode;

	public static FacepointAdjustmentMethod FacepointAdjustmentMode;

	public static ShallowBehavior ShallowBehaviorMode;

	public static bool UseMarksNewWarpMethod;

	public static readonly Dictionary<EnumRSUnits, string> RSUnitDictionary;

	public static string sSIUnits;

	public static string sUSCustomaryUnits;

	public static EnumRSUnits UnitsRiverStation
	{
		get
		{
			if (_unitsRiverStationNeedsValue)
			{
				if (Units == EnumUnits.USCustomary)
				{
					AssignUnitsRiverStation(EnumRSUnits.Feet);
				}
				else
				{
					if (Units != EnumUnits.SIUnits)
					{
						throw new NotImplementedException();
					}
					AssignUnitsRiverStation(EnumRSUnits.Meters);
				}
			}
			return _unitsRiverStation;
		}
		set
		{
			_unitsRiverStationNeedsValue = false;
			_unitsRiverStation = value;
		}
	}

	public static string ArchitectureString
	{
		get
		{
			if (Environment.Is64BitProcess)
			{
				return "x64";
			}
			return "x86";
		}
	}

	public static string[] GDALResampleNames => GDALSetup.ResamplingMethodNames;

	public static string SRS_GDAL_CMS
	{
		get
		{
			if (Operators.CompareString(SRSFilename, "", TextCompare: false) == 0)
			{
				return "";
			}
			if (SRSIsESRI(SRSFilename))
			{
				return "ESRI::\"" + SRSFilename + "\"";
			}
			return "\"" + SRSFilename + "\"";
		}
	}

	public static EnumUnits Units
	{
		get
		{
			return _units;
		}
		set
		{
			_units = value;
		}
	}

	public static string RasMapFilename
	{
		get
		{
			return _rasMapFilename;
		}
		set
		{
			if (Operators.CompareString(_rasMapFilename.ToLowerInvariant(), value.ToLowerInvariant(), TextCompare: false) != 0)
			{
				RefreshXMLDoc(value);
			}
		}
	}

	public static string BackupRasMapFilename => _rasMapFilename + ".backup";

	public static string RasMapFolder => System.IO.Path.GetDirectoryName(RasMapFilename);

	public static XmlDocument RasMapDoc => _rasMapDoc;

	public static float Gravity
	{
		get
		{
			if (Units == EnumUnits.SIUnits)
			{
				return 9.80665f;
			}
			_ = Units;
			return 32.174f;
		}
	}

	public static string LengthUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return "feet";
			}
			if (Units == EnumUnits.SIUnits)
			{
				return "meters";
			}
			return "feet";
		}
	}

	public static string ShortLengthUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return "ft";
			}
			if (Units == EnumUnits.SIUnits)
			{
				return "m";
			}
			return "ft";
		}
	}

	public static Length DefaultLengthUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return Length.Feet;
			}
			if (Units == EnumUnits.SIUnits)
			{
				return Length.Meters;
			}
			return Length.Feet;
		}
	}

	public static string SmallLengthUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return "in";
			}
			if (Units == EnumUnits.SIUnits)
			{
				return "mm";
			}
			return "in";
		}
	}

	public static Length PrecipLengthUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return Length.Inches;
			}
			if (Units == EnumUnits.SIUnits)
			{
				return Length.Millimeters;
			}
			return Length.Inches;
		}
	}

	public static PeriodAveragePrecipitation PrecipRateUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return PeriodAveragePrecipitation.InchesPerHour;
			}
			if (Units == EnumUnits.SIUnits)
			{
				return PeriodAveragePrecipitation.MillimeterPerHour;
			}
			return PeriodAveragePrecipitation.InchesPerHour;
		}
	}

	public static PeriodCumulativePrecipitation PrecipPeriodCumulativeUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return PeriodCumulativePrecipitation.Inches;
			}
			if (Units == EnumUnits.SIUnits)
			{
				return PeriodCumulativePrecipitation.Millimeters;
			}
			return PeriodCumulativePrecipitation.Inches;
		}
	}

	public static WindSpeed WindSpeedUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return WindSpeed.FeetPerSecond;
			}
			if (Units == EnumUnits.SIUnits)
			{
				return WindSpeed.MetersPerSecond;
			}
			return WindSpeed.FeetPerSecond;
		}
	}

	public static string ShortAreaUnits => ShortLengthUnits + "²";

	public static string ShortVolumeUnits => ShortLengthUnits + "³";

	public static string ShortVolumeUnits_SA
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return "ac-ft";
			}
			if (Units == EnumUnits.SIUnits)
			{
				return "1000 m³";
			}
			return "ac-ft";
		}
	}

	public static string FlowUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return "CFS";
			}
			return "CMS";
		}
	}

	public static string VelocityUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return "feet/sec";
			}
			if (Units == EnumUnits.SIUnits)
			{
				return "meters/sec";
			}
			return "feet/sec";
		}
	}

	public static string ShortVelocityUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return "ft/s";
			}
			if (Units == EnumUnits.SIUnits)
			{
				return "m/s";
			}
			return "ft/s";
		}
	}

	public static string ShearUnits
	{
		get
		{
			if (Units == EnumUnits.USCustomary)
			{
				return "lbs/sq ft";
			}
			if (Units == EnumUnits.SIUnits)
			{
				return "pascal";
			}
			return "lbs/sq ft";
		}
	}

	public static Projection SRSProjection
	{
		get
		{
			if (_projection == null || Operators.CompareString(_projectionFilename, SRSFilename, TextCompare: false) != 0 || !File.Exists(_projectionFilename) || DateTime.Compare(_projectionFileDate, File.GetLastWriteTime(_projectionFilename)) != 0)
			{
				if (File.Exists(SRSFilename))
				{
					try
					{
						if (SRSIsESRI(SRSFilename))
						{
							_projection = new ESRIProjection(SRSFilename);
						}
						else if (SRSIsWKT(SRSFilename))
						{
							_projection = new WKTProjection(File.ReadAllText(SRSFilename));
						}
						else if (SRSIsProj4(SRSFilename))
						{
							_projection = new Proj4Projection(File.ReadAllText(SRSFilename));
						}
						else if (SRSIsEPSG(SRSFilename))
						{
							_projection = new EPSGProjection(Conversions.ToInteger(File.ReadAllText(SRSFilename)));
						}
						_projectionFilename = SRSFilename;
						_projectionFileDate = File.GetLastWriteTime(SRSFilename);
					}
					catch (Exception ex)
					{
						ProjectData.SetProjectError(ex);
						Exception ex2 = ex;
						_projection = null;
						_projectionFilename = "";
						ProjectData.ClearProjectError();
					}
				}
				else
				{
					_projection = null;
					_projectionFilename = "";
				}
			}
			return _projection;
		}
	}

	public static string TerrainSourceFolder
	{
		get
		{
			string textValue = "";
			_savedFolders.TryGetValue("TerrainSourceFolder", out textValue);
			return textValue;
		}
		set
		{
			_savedFolders["TerrainSourceFolder"] = value;
		}
	}

	public static string TerrainDestinationFolder
	{
		get
		{
			string textValue = "";
			_savedFolders.TryGetValue("TerrainDestinationFolder", out textValue);
			return textValue;
		}
		set
		{
			_savedFolders["TerrainDestinationFolder"] = value;
		}
	}

	public static string LandCoverSourceFolder
	{
		get
		{
			string textValue = "";
			_savedFolders.TryGetValue("LandCoverSourceFolder", out textValue);
			return textValue;
		}
		set
		{
			_savedFolders["LandCoverSourceFolder"] = value;
		}
	}

	public static string LandCoverDestinationFolder
	{
		get
		{
			string textValue = "";
			_savedFolders.TryGetValue("LandCoverDestinationFolder", out textValue);
			return textValue;
		}
		set
		{
			_savedFolders["LandCoverDestinationFolder"] = value;
		}
	}

	public static string SaveAsFolder
	{
		get
		{
			string textValue = "";
			_savedFolders.TryGetValue("SaveAsFolder", out textValue);
			return textValue;
		}
		set
		{
			_savedFolders["SaveAsFolder"] = value;
		}
	}

	public static string AddDataFolder
	{
		get
		{
			string textValue = "";
			_savedFolders.TryGetValue("AddDataFolder", out textValue);
			return textValue;
		}
		set
		{
			_savedFolders["AddDataFolder"] = value;
		}
	}

	private static void AssignUnitsRiverStation(EnumRSUnits units)
	{
		UnitsRiverStation = units;
	}


	private static bool SRSIsESRI(string filename)
	{
		if (!File.Exists(filename))
		{
			return false;
		}
		if (Operators.CompareString(System.IO.Path.GetExtension(filename).ToLower(), ".prj", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	private static bool SRSIsWKT(string filename)
	{
		if (!File.Exists(filename))
		{
			return false;
		}
		if (Operators.CompareString(System.IO.Path.GetExtension(filename).ToLower(), ".wkt", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	private static bool SRSIsProj4(string filename)
	{
		if (!File.Exists(filename))
		{
			return false;
		}
		if (Operators.CompareString(System.IO.Path.GetExtension(filename).ToLower(), ".proj4", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	private static bool SRSIsEPSG(string filename)
	{
		if (!File.Exists(filename))
		{
			return false;
		}
		if (Operators.CompareString(System.IO.Path.GetExtension(filename).ToLower(), ".epsg", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public static void SetHorizontalRenderingMode()
	{
		CellRenderMode = CellRenderMethod.AsComputed;
		FacepointAdjustmentMode = FacepointAdjustmentMethod.None;
		ShallowBehaviorMode = ShallowBehavior.None;
		CellStencilMode = CellStencilMethod.JustFacepoints;
	}

	public static bool IsHorizontalRenderingMode()
	{
		return CellRenderMode == CellRenderMethod.AsComputed;
	}

	public static void SetSlopingRenderingMode()
	{
		CellRenderMode = CellRenderMethod.Pretty;
		FacepointAdjustmentMode = FacepointAdjustmentMethod.None;
		ShallowBehaviorMode = ShallowBehavior.None;
		CellStencilMode = CellStencilMethod.JustFacepoints;
	}

	public static bool IsSlopingRenderingMode()
	{
		return FacepointAdjustmentMode == FacepointAdjustmentMethod.None;
	}

	public static void SetHybridRenderingMode()
	{
		CellRenderMode = CellRenderMethod.Pretty;
		FacepointAdjustmentMode = FacepointAdjustmentMethod.DragAverage;
		ShallowBehaviorMode = ShallowBehavior.ReduceToHorizontal;
		CellStencilMode = CellStencilMethod.WithFaces;
	}

	public static bool IsHybridRenderingMode()
	{
		return FacepointAdjustmentMode == FacepointAdjustmentMethod.DragAverage;
	}

	static SharedData()
	{
		OutputDecimalPlaces = 2;
		NearPointTolerance = 5;
		NearSegmentTolerance = 15;
		_unitsRiverStation = EnumRSUnits.Feet;
		_unitsRiverStationNeedsValue = true;
		RiverStationDecimalPlaces = 0;
		HorizontalDecimalPlaces = 1;
		VerticalDecimalPlaces = 2;
		DebugMode = false;
		DebugEditingTools = false;
		Allow64BitHijack = true;
		StandAloneMode = true;
		ListenForSourceFileChanges = true;
		IsTestingUtility = false;
		ZTrashValue = 0;
		RasMapper = null;
		RasMapperIsLoading = false;
		_rasMapFilename = "";
		_rasMapDoc = null;
		CopiedFeatures = new List<Feature>();
		CopiedFeaturesDataRows = new List<DataRow>();
		CopiedFromDataRows = new List<WeakReference<DataRow>>();
		MaxNumberOfPointsOnXS = 450;
		MaxNumberOfPointsOnLS = 1000;
		SRSFilename = "";
		ToWGS84 = false;
		_projectionFilename = "";
		_projection = null;
		_units = EnumUnits.USCustomary;
		_mapCache = new Dictionary<RASResults, CacheCollection>();
		_meshCellSpatialIndexCache = new Dictionary<MeshFV2D, DistinctSpatialIndex>();
		_meshFaceSpatialIndexCache = new Dictionary<MeshFV2D, DistinctSpatialIndex>();
		_meshFacepointSpatialIndexCache = new Dictionary<MeshFV2D, DistinctSpatialIndex>();
		_meshCellAreaCache = new Dictionary<MeshFV2D, float[]>();
		_meshPerimeterCache = new Dictionary<MeshFV2D, DistinctSpatialIndex>();
		_saveTheMeshCache = new Dictionary<string, Dictionary<string, MeshFV2D>>();
		_savedFolders = new Dictionary<string, string>();
		PlanStructures = new List<PlanStructure>();
		CellRenderMode = CellRenderMethod.Pretty;
		FaceWSMode = FaceWSMethod.Adjusted;
		CellStencilMode = CellStencilMethod.WithFaces;
		FacepointAdjustmentMode = FacepointAdjustmentMethod.DragAverage;
		ShallowBehaviorMode = ShallowBehavior.ReduceToHorizontal;
		UseMarksNewWarpMethod = false;
		RSUnitDictionary = new Dictionary<EnumRSUnits, string>
		{
			{
				EnumRSUnits.Feet,
				"Feet"
			},
			{
				EnumRSUnits.Miles,
				"Miles"
			},
			{
				EnumRSUnits.Meters,
				"Meters"
			},
			{
				EnumRSUnits.Kilometers,
				"Kilometers"
			}
		};
		sSIUnits = "SI Units";
		sUSCustomaryUnits = "US Customary";
		if (ShortCircuit.AllowGDALInitialization)
		{
			SetupGDAL();
		}
	}

	private static void SetupGDAL()
	{
		if (GDALSetup.IsInitialized) return;
		GDALSetup.RAS_VERSION_NUMBER = "5.1";
		GDALSetup.InitializeMultiplatform();
	}

	public static void ResetParameters()
	{
		_savedFolders.Clear();
		ClearAllCaches();
		SRSFilename = "";
		ToWGS84 = false;
	}

	public static bool IsHeadless()
	{
		if (RasMapper == null)
		{
			return true;
		}
		return false;
	}

	public static void EnsureLoadGDAL()
	{
		ZTrashValue = 1;
	}

	public static void RefreshXMLDoc(string rasmapFilename)
	{
		_rasMapFilename = rasmapFilename;
		if (File.Exists(_rasMapFilename))
		{
			_rasMapDoc = new XmlDocument();
			try
			{
				_rasMapDoc.Load(_rasMapFilename);
				return;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				_rasMapDoc = null;
				ProjectData.ClearProjectError();
				return;
			}
		}
		_rasMapDoc = null;
	}

	public static void ReportMessage(string stringValue, bool replacePreviousIfPossible = false, bool errorMessage = false)
	{
		string[] parts = stringValue.Split('\r');
		foreach (string part in parts)
		{
			if (RasMapper != null)
			{
				RasMapper.StatusMessageAdd(part, replacePreviousIfPossible);
			}
			else if (errorMessage)
			{
				General.ConsoleWriteLineToStdErr(part);
			}
			else
			{
				Console.WriteLine(part);
			}
		}
	}

	public static void ReportMessageGreen(string stringValue, bool replacePreviousIfPossible = false, bool errorMessage = false)
	{
		string[] parts = stringValue.Split('\r');
		foreach (string part in parts)
		{
			if (RasMapper != null)
			{
				RasMapper.StatusMessageAdd(part, replacePreviousIfPossible);
				continue;
			}
			if (errorMessage)
			{
				General.ConsoleWriteLineToStdErr(part);
				continue;
			}
			Console.WriteLine("FONT=GREEN");
			Console.WriteLine(part);
			Console.WriteLine("FONT=BLACK");
		}
	}

	public static Action<int> GetProgressReporter(string stringValue)
	{
		return [SpecialName] (int ordinal) =>
		{
			ReportMessage(stringValue + "[ " + $"{ordinal,3}" + "%]", replacePreviousIfPossible: true);
		};
	}

	public static void RASMapperZoom<T>(T iExt, int ratio = 1) where T : IExtent
	{
	}

	public static void RASMapperZoom<T>(List<T> iExts, int ratio = 1) where T : IExtent
	{
	}

	public static void RASMapperReplot(Extent desiredExt = null)
	{
	}

	public static string GetUniqueFilename(string basename, string ext, string rootFolderBeneathRM = "")
	{
		if (ext.StartsWith("."))
		{
			ext = ext.Substring(1);
		}
		string path = basename + "." + ext;
		string path2 = System.IO.Path.Combine(RasMapFolder, rootFolderBeneathRM);
		string combine = System.IO.Path.Combine(path2, path);
		if (!File.Exists(combine))
		{
			return combine;
		}
		int ordinal = 1;
		while (true)
		{
			path = basename + ordinal.ToString().InParentheses() + "." + ext;
			combine = System.IO.Path.Combine(path2, path);
			if (!File.Exists(combine))
			{
				break;
			}
			ordinal = checked(ordinal + 1);
		}
		return combine;
	}

	public static string AppropriateDatetimeString(DateTime dt, bool allowMS = true)
	{
		if (allowMS && dt.Millisecond != 0)
		{
			return dt.ToString("ddMMMyyyy HHmm:ss.fff");
		}
		if (dt.Second != 0)
		{
			return dt.ToString("ddMMMyyyy HHmm:ss");
		}
		if (dt.Hour != 0 || dt.Minute != 0)
		{
			return dt.ToString("ddMMMyyyy HHmm");
		}
		return dt.ToString("ddMMMyyyy");
	}

	public static string GetOutputDecimalPlacesStringFormat()
	{
		string result = "0.000";
		switch (OutputDecimalPlaces)
		{
		case 0:
			result = "0";
			break;
		case 1:
			result = "0.0";
			break;
		case 2:
			result = "0.00";
			break;
		case 3:
			result = "0.000";
			break;
		case 4:
			result = "0.0000";
			break;
		case 5:
			result = "0.00000";
			break;
		}
		return result;
	}

	public static EnumRSUnits RSUnitsFromString(string s)
	{
		foreach (KeyValuePair<EnumRSUnits, string> rsUnitDictionary in RSUnitDictionary)
		{
			if (Operators.CompareString(rsUnitDictionary.Value, s, TextCompare: false) == 0)
			{
				return rsUnitDictionary.Key;
			}
		}
		throw new NotSupportedException("'" + s + "' not found.");
	}

	public static void TrySetRSUnitsFromString(string s)
	{
		try
		{
			UnitsRiverStation = RSUnitsFromString(s);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			UnitsRiverStation = BasicRSUnitEquivalent(Units);
			ProjectData.ClearProjectError();
		}
	}

	public static string RSUnitsToString(EnumRSUnits rsEnum)
	{
		foreach (KeyValuePair<EnumRSUnits, string> rsUnitDictionary in RSUnitDictionary)
		{
			if (rsUnitDictionary.Key == rsEnum)
			{
				return rsUnitDictionary.Value;
			}
		}
		throw new NotSupportedException(rsEnum.ToString());
	}

	public static EnumRSUnits BasicRSUnitEquivalent(EnumUnits unitSystem)
	{
		return unitSystem switch
		{
			EnumUnits.SIUnits => EnumRSUnits.Meters, 
			EnumUnits.USCustomary => EnumRSUnits.Feet, 
			_ => throw new NotSupportedException("Bad unit system value"), 
		};
	}

	public static double RSConversionFactor()
	{
		return RSConversionFactor(Units, UnitsRiverStation);
	}

	public static double RSConversionFactor(EnumUnits un, EnumRSUnits rsUn)
	{
		return un switch
		{
			EnumUnits.SIUnits => rsUn switch
			{
				EnumRSUnits.Feet => 3.280839895013123, 
				EnumRSUnits.Miles => 0.0006213711922373339, 
				EnumRSUnits.Kilometers => 0.001, 
				_ => 1.0, 
			}, 
			EnumUnits.USCustomary => rsUn switch
			{
				EnumRSUnits.Meters => 0.3048, 
				EnumRSUnits.Miles => 0.0001893939393939394, 
				EnumRSUnits.Kilometers => 0.00030480000000000004, 
				_ => 1.0, 
			}, 
			_ => 1.0, 
		};
	}

	public static void SetUnitsSystem(string s)
	{
		if (s.RoughlyEquals(sSIUnits))
		{
			Units = EnumUnits.SIUnits;
		}
		else
		{
			Units = EnumUnits.USCustomary;
		}
	}

	public static void SetUnitsSystem(EnumUnits unit)
	{
		Units = unit;
	}

	public static string UnitsSystem()
	{
		return GetUnitSystemName(Units);
	}

	public static string GetUnitSystemName(EnumUnits eunits)
	{
		if (eunits == EnumUnits.SIUnits)
		{
			return sSIUnits;
		}
		return sUSCustomaryUnits;
	}

	public static void HDFWriteSRS(H5Writer hw)
	{
		if (Operators.CompareString(SRSFilename, "", TextCompare: false) != 0 && File.Exists(SRSFilename))
		{
			string allText = File.ReadAllText(SRSFilename);
			hw.WriteAttribute("/", "Projection", allText);
		}
	}

	public static void HDFWriteFileType(H5Writer hw, string filetype)
	{
		hw.WriteAttribute("/", "File Type", filetype);
	}

	public static string HDFReadFileType(string filename)
	{
		string result = "";
		if (!File.Exists(filename))
		{
			return result;
		}
		using (H5Reader hr = new H5Reader(filename))
		{
			result = HDFReadFileType(hr);
		}
		return result;
	}

	public static string HDFReadFileType(H5Reader hr)
	{
		if (hr.GetAttributeNames("/").Contains("File Type"))
		{
			string textValue = "";
			hr.ReadAttributeScalar("/", "File Type", ref textValue);
			return textValue;
		}
		return "";
	}

	public static void ClearAllCaches()
	{
		ClearMapCaches();
		ClearAllMeshCaches();
	}

	public static void ClearAllMeshCaches()
	{
		ClearMeshCellSpatialIndex();
		ClearMeshFacePointSpatialIndex();
		ClearMeshFaceSpatialIndex();
		ClearMeshCellAreas();
		ClearCachedMeshes();
	}

	public static DistinctSpatialIndex GetMeshCellSpatialIndex(MeshFV2D mesh)
	{
		lock (_meshCellSpatialIndexCache)
		{
			if (!_meshCellSpatialIndexCache.ContainsKey(mesh))
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				_meshCellSpatialIndexCache.Add(mesh, mesh.ComputeCellSpatialIndex(skipVirtualCells: true));
				stopwatch.Stop();
			}
			return _meshCellSpatialIndexCache[mesh];
		}
	}

	public static void ClearMeshCellSpatialIndex(MeshFV2D mesh)
	{
		lock (_meshCellSpatialIndexCache)
		{
			if (_meshCellSpatialIndexCache.ContainsKey(mesh))
			{
				_meshCellSpatialIndexCache.Remove(mesh);
			}
		}
	}

	public static void ClearMeshCellSpatialIndex()
	{
		lock (_meshCellSpatialIndexCache)
		{
			_meshCellSpatialIndexCache.Clear();
		}
	}

	public static DistinctSpatialIndex GetMeshFaceSpatialIndex(MeshFV2D mesh)
	{
		lock (_meshFaceSpatialIndexCache)
		{
			if (!_meshFaceSpatialIndexCache.ContainsKey(mesh))
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				_meshFaceSpatialIndexCache.Add(mesh, mesh.ComputeFaceSpatialIndex());
				stopwatch.Stop();
			}
		}
		return _meshFaceSpatialIndexCache[mesh];
	}

	public static void ClearMeshFaceSpatialIndex(MeshFV2D mesh)
	{
		lock (_meshFaceSpatialIndexCache)
		{
			if (_meshFaceSpatialIndexCache.ContainsKey(mesh))
			{
				_meshFaceSpatialIndexCache.Remove(mesh);
			}
		}
	}

	public static void ClearMeshFaceSpatialIndex()
	{
		lock (_meshFaceSpatialIndexCache)
		{
			_meshFaceSpatialIndexCache.Clear();
		}
	}

	public static DistinctSpatialIndex GetMeshFacePointSpatialIndex(MeshFV2D mesh)
	{
		lock (_meshFacepointSpatialIndexCache)
		{
			if (!_meshFacepointSpatialIndexCache.ContainsKey(mesh))
			{
				_meshFacepointSpatialIndexCache.Add(mesh, mesh.ComputeFacepointSpatialIndex());
			}
		}
		return _meshFacepointSpatialIndexCache[mesh];
	}

	public static void ClearMeshFacePointSpatialIndex(MeshFV2D mesh)
	{
		lock (_meshFacepointSpatialIndexCache)
		{
			if (_meshFacepointSpatialIndexCache.ContainsKey(mesh))
			{
				_meshFacepointSpatialIndexCache.Remove(mesh);
			}
		}
	}

	public static void ClearMeshFacePointSpatialIndex()
	{
		lock (_meshFacepointSpatialIndexCache)
		{
			_meshFacepointSpatialIndexCache.Clear();
		}
	}

	public static void SetCachedMesh(string srcFilename, string meshName, MeshFV2D mesh)
	{
		if (_saveTheMeshCache.ContainsKey(srcFilename))
		{
			_saveTheMeshCache[srcFilename][meshName] = mesh;
			return;
		}
		Dictionary<string, MeshFV2D> meshFV2DByString = new Dictionary<string, MeshFV2D>();
		meshFV2DByString.Add(meshName, mesh);
		_saveTheMeshCache.Add(srcFilename, meshFV2DByString);
	}

	public static MeshFV2D TryFindCachedMesh(string srcFilename, string meshName)
	{
		if (_saveTheMeshCache.ContainsKey(srcFilename))
		{
			Dictionary<string, MeshFV2D> saveTheMeshCache = _saveTheMeshCache[srcFilename];
			if (saveTheMeshCache.ContainsKey(meshName))
			{
				return saveTheMeshCache[meshName];
			}
		}
		return null;
	}

	public static void ClearCachedMeshes()
	{
		_saveTheMeshCache.Clear();
	}

	public static float[] GetMeshCellAreas(MeshFV2D mesh)
	{
		lock (_meshCellAreaCache)
		{
			if (!_meshCellAreaCache.ContainsKey(mesh))
			{
				_meshCellAreaCache.Add(mesh, mesh.ComputeAllCellAreas());
			}
		}
		return _meshCellAreaCache[mesh];
	}

	public static void ClearMeshCellAreas(MeshFV2D mesh)
	{
		lock (_meshCellAreaCache)
		{
			if (_meshCellAreaCache.ContainsKey(mesh))
			{
				_meshCellAreaCache.Remove(mesh);
			}
		}
	}

	public static void ClearMeshCellAreas()
	{
		lock (_meshCellAreaCache)
		{
			_meshCellAreaCache.Clear();
		}
	}

	public static CacheCollection GetMapCache(RASResultsMap map)
	{
		return GetMapCache(map.Results);
	}

	public static CacheCollection GetMapCache(RASResults res)
	{
		CacheCollection cacheCollection = null;
		if (!_mapCache.TryGetValue(res, out cacheCollection))
		{
			cacheCollection = new CacheCollection();
			_mapCache.Add(res, cacheCollection);
		}
		else
		{
			cacheCollection.VerifyFileDate();
		}
		return cacheCollection;
	}

	public static void ClearMapCaches()
	{
		foreach (RASResults key in _mapCache.Keys)
		{
			_mapCache[key].Clear();
		}
		_mapCache.Clear();
	}

	public static void ClearMapCache(RASResultsMap map)
	{
		ClearMapCache(map.Results);
	}

	public static void ClearMapCache(RASResults res)
	{
		CacheCollection cacheCollection = null;
		if (_mapCache.TryGetValue(res, out cacheCollection))
		{
			cacheCollection.Clear();
			_mapCache.Remove(res);
		}
	}

	public static bool SRSFilenameShowStatus(bool showMessageDialog = false)
	{
		if (string.IsNullOrEmpty(SRSFilename))
		{
			if (showMessageDialog)
			{
				MessageBox.Show("Spatial Reference System for the project has not been set in RASMapper. (Goto menu item Tools/Set Project for Project)", "RasMapper", MessageBoxButtons.Ok, MessageBoxIcon.Exclamation);
			}
			else
			{
				Console.WriteLine("Spatial Reference System for the project has not been set in RASMapper. (Goto menu item Tools/Set Project for Project)");
			}
			return false;
		}
		if (!File.Exists(SRSFilename))
		{
			if (showMessageDialog)
			{
				MessageBox.Show("Spatial Reference System for the project has been set in RASMapper, but the file does not exist  (check under menu item Tools/Set Project for Project)", "RasMapper", MessageBoxButtons.Ok, MessageBoxIcon.Exclamation);
			}
			else
			{
				Console.WriteLine("Spatial Reference System for the project has been set in RASMapper, but the file does not exist  (check under menu item Tools/Set Project for Project)");
			}
			return false;
		}
		return true;
	}

	public static bool IsSRSFilenameOK()
	{
		if (string.IsNullOrEmpty(SRSFilename))
		{
			return false;
		}
		if (!File.Exists(SRSFilename))
		{
			return false;
		}
		return true;
	}

	public static void XMLSave_GeneralSettings(XmlDocument doc, XmlNode node)
	{
		XmlNode xmlNode = doc.CreateElement("GeneralSettings");
		node.AppendChild(xmlNode);
		XML.AddTextElement(doc, xmlNode, "OutputDecimalPlaces", OutputDecimalPlaces.ToString());
	}

	public static void XMLLoad_RenderModeSettings(XmlNode node)
	{
		if (Operators.CompareString(XML.StringTextElement(node, "GraphicsEngine"), "D2DHybrid", TextCompare: false) == 0)
		{
			RasMapper.UseDirect2D = true;
		}
		string.IsNullOrEmpty(XML.StringTextElement(node, "GPAA"));
		float minWSPlotTolerance = XML.SingleTextElement(node, "PlotTolerance");
		if (!float.IsNaN(minWSPlotTolerance))
		{
			RASResults.MinWSPlotTolerance = minWSPlotTolerance;
		}
	}

	public static void XMLLoad_GlobalGeneralSettings(XmlNode node)
	{
		XmlNode xmlNode = node.SelectSingleNode("GeneralSettings");
		if (xmlNode != null)
		{
			int outputDecimalPlaces = XML.IntegerTextElement(xmlNode, "OutputDecimalPlaces");
			if (outputDecimalPlaces >= 0)
			{
				OutputDecimalPlaces = outputDecimalPlaces;
			}
		}
	}

	public static void XMLSave_RenderModeSettings(XmlDocument doc, XmlNode node)
	{
		if (RasMapper.UseDirect2D)
		{
			string textValue = "D2DHybrid";
			XML.AddTextElement(doc, node, "GraphicsEngine", textValue);
		}
		XML.AddTextElement(doc, node, "PlotTolerance", RASResults.MinWSPlotTolerance.ToString());
	}

	public static void XMLSave_RASLayerSettings(XmlDocument doc, XmlNode node)
	{
		XmlNode newChild = doc.CreateElement("RASLayers");
		node.AppendChild(newChild);
	}

	public static void XMLLoad_RASLayerSettings(XmlNode node)
	{
		node.SelectSingleNode("RASLayers");
	}

	public static void XMLSave_EditorSettings(XmlDocument doc, XmlNode node)
	{
		XmlNode xmlNode = doc.CreateElement("EditorSettings");
		node.AppendChild(xmlNode);
		XML.AddTextElement(doc, xmlNode, "NearPointTolerance", NearPointTolerance.ToString());
		XML.AddTextElement(doc, xmlNode, "NearLineTolerance", NearSegmentTolerance.ToString());
	}

	public static void XMLLoad_EditorSettings(XmlNode node)
	{
		XmlNode xmlNode = node.SelectSingleNode("EditorSettings");
		if (xmlNode != null)
		{
			int nearPointTolerance = XML.IntegerTextElement(xmlNode, "NearPointTolerance");
			if (nearPointTolerance > 0)
			{
				NearPointTolerance = nearPointTolerance;
			}
			int nearSegmentTolerance = XML.IntegerTextElement(xmlNode, "NearLineTolerance");
			if (nearSegmentTolerance > 0)
			{
				NearSegmentTolerance = nearSegmentTolerance;
			}
		}
	}

	public static void XMLSave_ProjectSettings(XmlDocument doc, XmlNode node)
	{
		XmlNode xmlNode = doc.CreateElement("ProjectSettings");
		node.AppendChild(xmlNode);
		XML.AddTextElement(doc, xmlNode, "RiverStationUnits", RSUnitsToString(UnitsRiverStation));
		XML.AddTextElement(doc, xmlNode, "RiverStationDecimalPlaces", RiverStationDecimalPlaces.ToString());
		XML.AddTextElement(doc, xmlNode, "HorizontalDecimalPlaces", HorizontalDecimalPlaces.ToString());
		XML.AddTextElement(doc, xmlNode, "VerticalDecimalPlaces", VerticalDecimalPlaces.ToString());
		XML.AddTextElement(doc, xmlNode, "XSMaxPoints", MaxNumberOfPointsOnXS.ToString());
		XML.AddTextElement(doc, xmlNode, "LSMaxPoints", MaxNumberOfPointsOnLS.ToString());
		XML.AddTextElement(doc, xmlNode, "MeshTolerance_MinFaceLength", MeshFV2D.ActualMinFaceLengthRatio.ToString());
		XML.AddTextElement(doc, xmlNode, "MeshTolerance_EnsureCellPoint", MeshFV2D.ActualValidatePolygonContainsCell.ToString());
	}

	public static void XMLLoad_ProjectSettings(XmlNode node)
	{
		XmlNode xmlNode = node.SelectSingleNode("ProjectSettings");
		if (xmlNode != null)
		{
			int riverStationDecimalPlaces = XML.IntegerTextElement(xmlNode, "RiverStationDecimalPlaces");
			if (riverStationDecimalPlaces >= 0)
			{
				RiverStationDecimalPlaces = riverStationDecimalPlaces;
			}
			TrySetRSUnitsFromString(XML.StringTextElement(xmlNode, "RiverStationUnits"));
			int horizontalDecimalPlaces = XML.IntegerTextElement(xmlNode, "HorizontalDecimalPlaces");
			if (horizontalDecimalPlaces >= 0)
			{
				HorizontalDecimalPlaces = horizontalDecimalPlaces;
			}
			else
			{
				HorizontalDecimalPlaces = 1;
			}
			int verticalDecimalPlaces = XML.IntegerTextElement(xmlNode, "VerticalDecimalPlaces");
			if (verticalDecimalPlaces >= 0)
			{
				VerticalDecimalPlaces = verticalDecimalPlaces;
			}
			else
			{
				VerticalDecimalPlaces = 2;
			}
			int maxNumberOfPointsOnXS = XML.IntegerTextElement(xmlNode, "XSMaxPoints");
			if (maxNumberOfPointsOnXS > 2)
			{
				MaxNumberOfPointsOnXS = maxNumberOfPointsOnXS;
			}
			else
			{
				MaxNumberOfPointsOnXS = 450;
			}
			int maxNumberOfPointsOnLS = XML.IntegerTextElement(xmlNode, "LSMaxPoints");
			if (maxNumberOfPointsOnLS > 2)
			{
				MaxNumberOfPointsOnLS = maxNumberOfPointsOnLS;
			}
			else
			{
				MaxNumberOfPointsOnLS = 1000;
			}
			if (!MeshFV2D.TrySetMinFaceLengthRatio(XML.DoubleTextElement(xmlNode, "MeshTolerance_MinFaceLength")))
			{
				MeshFV2D.ResetMinFaceLengthRatio();
			}
			if (xmlNode.SelectSingleNode("MeshTolerance_EnsureCellPoint") != null)
			{
				MeshFV2D.ActualValidatePolygonContainsCell = XML.BooleanTextElement(xmlNode, "MeshTolerance_EnsureCellPoint");
			}
			else
			{
				MeshFV2D.ActualValidatePolygonContainsCell = false;
			}
		}
	}

	public static void SaveCurrentSettingsToXML(XmlDocument doc, XmlNode node)
	{
		XmlNode xmlNode = doc.CreateElement("CurrentSettings");
		XMLSave_ProjectSettings(doc, xmlNode);
		XmlNode xmlNode2 = doc.CreateElement("Folders");
		xmlNode.AppendChild(xmlNode2);
		node.AppendChild(xmlNode);
		foreach (KeyValuePair<string, string> savedFolder in _savedFolders)
		{
			XML.AddTextElement(doc, xmlNode2, savedFolder.Key, RasMapperLib.Utilities.Path.MakeRelative(savedFolder.Value, RasMapFilename));
		}
	}
}
