using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mesh;
using RasMapperLib.Names;
using RasMapperLib.Render;
using RasMapperLib.Terrain;
using RasMapperLib.Utilities;
using TiffBinaryReader;
using Utility;
using Utility.Extensions;
using Utility.Progress;
using Utility.Systems;

namespace RasMapperLib;

public class RASD2FlowArea : MeshFeatureLayer, IGeometryLayer
{
	public class ResampleInfoPerRFI
	{
		public List<float[]> PerimeterValues;

		public List<byte[]> MaskValues;

		public List<float[]> TiffValues;

		public int MeshCellIndex;

		public List<TileRow> TileRows;

		public List<TileColumn> TileColumns;

		public RasterM TileRaster;
	}

	public enum SelectedComponent
	{
		None,
		Cell,
		Face,
		FacePoint
	}

	public enum ProgressReportingType
	{
		ConsoleWriteline,
		SharedDataReportMessage,
		PopupWindow,
		None
	}

	private enum MeshExportType
	{
		Point,
		Points,
		Polygon
	}

	[CompilerGenerated]
private sealed class _Closure_0024__70_002D0
	{
		public IList<string> desiredParameters;

		public _Closure_0024__70_002D0(_Closure_0024__70_002D0 other)
		{
			if (other != null)
			{
				desiredParameters = other.desiredParameters;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(KeyValuePair<string, float[]> kvp)
		{
			return !desiredParameters.RoughlyContains(kvp.Key);
		}
	}

	public const bool UseNewCreatePropertyTables = false;

	public const string sManningsN = "Manning's n";

	public const string sCellVolumeTol = "Cell Volume Tolerance";

	public const string sCellMinAreaFraction = "Cell Minimum Area Fraction";

	public const string sFaceProfileTol = "Face Profile Tolerance";

	public const string sFaceElevAreaTol = "Face Area Elevation Tolerance";

	public const string sFaceAreaConveyanceRatioTol = "Face Area Conveyance Ratio";

	public const string sFaceLaminarDepth = "Laminar Depth";

	private const string sPartialMesh = "Partial Mesh";

	public const string sPlotNValues = "Plot n Values";

	public const string sMeshHasBeenRecomputed = "Mesh Recomputed";

	private const string sCurrentComputedMeshUpToDate = "Current Computed Mesh Up to Date";

	public const string HDFGroupName = "Geometry/2D Flow Areas";

	public const float DefaultMannValue = 0.06f;

	public const float DefaultFaceConveyanceTol = 0.02f;

	public const float MinimumFaceConveyanceTol = 0.0001f;

	public const float DefaultCellMinAreaFraction = 0.01f;

	public static bool EnableTestMagnetizationChanges = true;

	public static float BlankingRatio = 0.4f;

	public static float FaceCollapseBreaklineRatio = 0.4f;






	private const bool USE_NEW_LC_COMPUTE_METHOD = true;

	private long _blineCounter;

	private long _rgnCounter;

	private long _strucCounter;

	private Polyline _mergedBline;
string IGeometryLayer.HDFGroupName=> "Geometry/2D Flow Areas";

	public override bool ForwardEditRequestsToParent { get; }

	public RASGeometry Geometry { get; }

	public override bool CanEdit { get; }

	public override string DefaultLayerName => "2D Flow Areas";

	public static float DefaultCellVolFilterTol()
	{
		if (SharedData.Units == SharedData.EnumUnits.USCustomary)
		{
			return 0.01f;
		}
		return 0.003f;
	}

	public static float DefaultFaceProfileFilterTol()
	{
		if (SharedData.Units == SharedData.EnumUnits.USCustomary)
		{
			return 0.01f;
		}
		return 0.003f;
	}

	public static float DefaultFaceAreaElevFilterTol()
	{
		if (SharedData.Units == SharedData.EnumUnits.USCustomary)
		{
			return 0.01f;
		}
		return 0.003f;
	}

	public static float DefaultLaminarDepthTol()
	{
		if (SharedData.Units == SharedData.EnumUnits.USCustomary)
		{
			return 0.2f;
		}
		return 0.06f;
	}

	public RASD2FlowArea(RASGeometry geometry)
		: base("2D Flow Areas", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		CanEdit = false;
		_blineCounter = -1L;
		_rgnCounter = -1L;
		_strucCounter = -1L;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		DataColumnGroup dataColumnGroup = new DataColumnGroup("Node Information", isVisible: true);
		AddManagedAttributeColumn("Name", "Name", typeof(string), dataColumnGroup);
		AddManagedAttributeColumn("Manning's n", "Mann", typeof(float), dataColumnGroup);
		DataColumnGroup dataColumnGroup2 = new DataColumnGroup("Tolerances", isVisible: true);
		AddManagedAttributeColumn("Cell Volume Tolerance", "Cell Vol Tol", typeof(float), dataColumnGroup2);
		AddManagedAttributeColumn("Cell Minimum Area Fraction", "Cell Min Area Fraction", typeof(float), dataColumnGroup2);
		AddManagedAttributeColumn("Face Profile Tolerance", "Face Profile Tol", typeof(float), dataColumnGroup2);
		AddManagedAttributeColumn("Face Area Elevation Tolerance", "Face Area Tol", typeof(float), dataColumnGroup2);
		AddManagedAttributeColumn("Face Area Conveyance Ratio", "Face Conv Ratio", typeof(float), dataColumnGroup2);
		AddManagedAttributeColumn("Laminar Depth", "Laminar Depth", typeof(float), dataColumnGroup2);
		AddManagedWriteOnlyAttributeColumn("Cell Count", [SpecialName] (int fid) =>
		{
			MeshFV2D meshFV2D = Mesh(fid);
			if (meshFV2D == null)
			{
				return 0;
			}
			return meshFV2D.IsComplete() ? meshFV2D.NonVirtualCellCount : 0;
		});
		AddAttributeColumn("Partial Mesh", typeof(PartialMesh), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Current Computed Mesh Up to Date", typeof(bool), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Mesh Recomputed", typeof(bool), null, DataColumnViewState.Invisible);
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		checked
		{
			try
			{
				if (!hr.ExistsAsGroup("Geometry/2D Flow Areas"))
				{
					return;
				}
				string path = "Geometry/2D Flow Areas/Attributes";
				if (hr.ExistsAsDataset(path))
				{
					int datasetDimension = (int)hr.GetDatasetDimensions(path)[0];
					int count = datasetDimension - 1;
					for (int i = 0; i <= count; i++)
					{
						AddFeature(MeshFV2D.NullMesh());
					}
					ReadManagedColumns(hr, path);
					int count2 = datasetDimension - 1;
					for (int j = 0; j <= count2; j++)
					{
						string trim = GetFeatureName(j).Trim();
						MeshFV2D meshFV2D = null;
						meshFV2D = ((!hr.ExistsAsGroup("Geometry/2D Flow Areas/" + trim)) ? MeshFV2D.NullMesh() : new MeshFV2D(hr, trim));
						if (meshFV2D.MeshCompletionState != RasMapperLib.Mesh.MeshStatus.Complete)
						{
							ErrorMessage = ErrorMessage + meshFV2D.StatusMessage + Environment.NewLine;
						}
						SetFeature(j, meshFV2D);
					}
				}
				else
				{
					HDFLoadFeatureTable_503(hr);
				}
				int count3 = FeatureCount() - 1;
				for (int k = 0; k <= count3; k++)
				{
					SetMeshUpToDate(k, meshUpToDate: true);
					SetMeshHasBeenRecomputed(k, meshHasBeenRecomputed: false);
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ErrorMessage = ex2.Message;
				ProjectData.ClearProjectError();
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	protected void HDFLoadFeatureTable_503(H5Reader hr)
	{
		string path = "Geometry/2D Flow Areas/Names";
		if (!hr.ExistsAsDataset(path))
		{
			return;
		}
		string[] values = null;
		hr.ReadDataset(path, ref values);
		checked
		{
			int count = values.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				values[i] = values[i].Trim();
				string textValue = values[i];
				MeshFV2D meshFV2D = null;
				meshFV2D = ((!hr.ExistsAsGroup("Geometry/2D Flow Areas/" + textValue)) ? MeshFV2D.NullMesh() : new MeshFV2D(hr, textValue));
				if (meshFV2D.MeshCompletionState != RasMapperLib.Mesh.MeshStatus.Complete)
				{
					ErrorMessage = ErrorMessage + meshFV2D.StatusMessage + Environment.NewLine;
				}
				AddFeature(meshFV2D);
			}
			HDF5Storage.ReadStringColumn(this, hr, path, "Name");
			HDF5Storage.ReadColumns(this, hr, "Geometry/2D Flow Areas/Manning's n", "Manning's n");
			HDF5Storage.ReadColumns(this, hr, "Geometry/2D Flow Areas/Tolerances", "Cell Volume Tolerance", "Face Profile Tolerance", "Face Area Elevation Tolerance", "Face Area Conveyance Ratio");
		}
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/2D Flow Areas");
			return;
		}
		EnsureAllFeaturesHaveUniqueNames();
		ComputeOutOfDateMeshes();
		if (!hw.ExistsAsGroup("Geometry/2D Flow Areas"))
		{
			hw.CreateGroup("Geometry/2D Flow Areas");
		}
		string dataset = "Geometry/2D Flow Areas/Attributes";
		DatatableManager.Merge(Geometry.MeshPerimeters.DatatableManager).Write(hw, dataset, FeatureTable());
		string meshDate = DSS.ConvertToDSSDateTime(DateTime.Now);
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				MeshFV2D meshFV2D = Mesh(i);
				if (meshFV2D != null && meshFV2D.IsComplete())
				{
					if (GetMeshHasBeenRecomputed(i))
					{
						string.IsNullOrEmpty(GetFeatureName(i));
						meshFV2D.SaveMesh(hw, GetFeatureName(i), meshDate);
					}
				}
				else
				{
					string featureName = GetFeatureName(i);
					if (hw.ExistsAsGroup("Geometry/2D Flow Areas/" + featureName))
					{
						hw.Delete("Geometry/2D Flow Areas/" + featureName);
					}
				}
			}
		}
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureAdded)
		{
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				if (Feature(featureIndicesChanged) == null)
				{
					SetFeature(featureIndicesChanged, MeshFV2D.NullMesh());
				}
				SetManningsN(featureIndicesChanged, 0.06f);
				SetFilterTolCellVol(featureIndicesChanged, DefaultCellVolFilterTol());
				SetFilterCellMinAreaFrac(featureIndicesChanged, 0.01f);
				SetFilterTolFaceProfile(featureIndicesChanged, DefaultFaceProfileFilterTol());
				SetFilterTolFaceElevArea(featureIndicesChanged, DefaultFaceAreaElevFilterTol());
				SetFaceAreaConveyanceRatioTol(featureIndicesChanged, 0.02f);
				SetFaceLaminarDepth(featureIndicesChanged, DefaultLaminarDepthTol());
				SetMeshHasBeenRecomputed(featureIndicesChanged, meshHasBeenRecomputed: true);
			}
		}
		SharedData.ClearAllMeshCaches();
	}

	public void Stream(BinaryWriter bw)
	{
		bw.Write(FeatureCount());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Mesh(i).Stream(bw);
				Pipe.WriteString(bw, GetFeatureName(i));
				bw.Write(GetManningsN(i));
				bw.Write(GetFilterTolCellVol(i));
				bw.Write(GetFilterCellMinAreaFrac(i));
				bw.Write(GetFilterTolFaceProfile(i));
				bw.Write(GetFilterTolFaceElevArea(i));
				bw.Write(GetFaceAreaConveyanceRatioTol(i));
				bw.Write(GetFaceLaminarDepth(i));
			}
		}
	}

	public static RASD2FlowArea Stream(BinaryReader br, RASGeometry geom)
	{
		RASD2FlowArea rASD2FlowArea = new RASD2FlowArea(geom);
		rASD2FlowArea.InitializeEmptyFeatureTable();
		rASD2FlowArea.SuppressTableReload(boolValue: true);
		rASD2FlowArea.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				MeshFV2D feature = MeshFV2D.Stream(br);
				rASD2FlowArea.AddFeature(feature);
				rASD2FlowArea.SetFeatureName(i, Pipe.ReadString(br));
				rASD2FlowArea.SetManningsN(i, br.ReadSingle());
				rASD2FlowArea.SetFilterTolCellVol(i, br.ReadSingle());
				rASD2FlowArea.SetFilterCellMinAreaFrac(i, br.ReadSingle());
				rASD2FlowArea.SetFilterTolFaceProfile(i, br.ReadSingle());
				rASD2FlowArea.SetFilterTolFaceElevArea(i, br.ReadSingle());
				rASD2FlowArea.SetFaceAreaConveyanceRatioTol(i, br.ReadSingle());
				rASD2FlowArea.SetFaceLaminarDepth(i, br.ReadSingle());
			}
			return rASD2FlowArea;
		}
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.Add(new PlotOption("Plot n Values"));
		return staticPlotOptions;
	}

	public void SetPinnedPropertyTables(bool pinnedPropertyTables)
	{
		using H5Writer h5Writer = new H5Writer(SourceFilename);
		if (!h5Writer.ExistsAsGroup("Geometry/2D Flow Areas"))
		{
			h5Writer.CreateGroup("Geometry/2D Flow Areas");
		}
		int ordinal = 0;
		ordinal = (pinnedPropertyTables ? 1 : 0);
		h5Writer.WriteAttribute("Geometry/2D Flow Areas", "Pin Property Tables", ordinal);
	}

	public bool GetPinnedPropertyTables()
	{
		bool result;
		if (!File.Exists(SourceFilename))
		{
			result = false;
		}
		else
		{
			try
			{
				using H5Reader h5Reader = new H5Reader(SourceFilename);
				if (!h5Reader.ExistsAsGroup("Geometry/2D Flow Areas"))
				{
					result = false;
				}
				else
				{
					int ordinal = 0;
					result = h5Reader.TryReadAttributeScalar("Geometry/2D Flow Areas", "Pin Property Tables", ref ordinal) && ordinal != 0;
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = false;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public bool EnsurePropertyTables(bool forceRecompute = false, bool verboseMessages = true, bool simpleTables = false, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (FeatureCount() == 0)
		{
			return true;
		}
		if (GetPinnedPropertyTables())
		{
			SharedData.ReportMessage("Did not recompute property tables - Tables are pinned. You may unpin via RasMapper or deleting this geometry .hdf file.");
			return true;
		}
		TerrainLayer terrain = Geometry.Terrain;
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Mesh(i);
				string featureName = GetFeatureName(i);
				PropertyTablesState propertyTablesState = ContainsCurrentTables(i);
				if (propertyTablesState.Status == TablesStatus.FatalError || propertyTablesState.Status == TablesStatus.NotApplicable)
				{
					SharedData.ReportMessage(featureName + ": " + propertyTablesState.Message, replacePreviousIfPossible: false, errorMessage: true);
					return false;
				}
				bool condition = false;
				if (forceRecompute)
				{
					switch (propertyTablesState.Status)
					{
					case TablesStatus.Good:
					case TablesStatus.OutOfDate:
						condition = true;
						break;
					case TablesStatus.CantRecomputeUseOldTables:
						SharedData.ReportMessageGreen("NOTE! " + featureName + ": " + propertyTablesState.Message, replacePreviousIfPossible: false, errorMessage: true);
						condition = false;
						break;
					}
				}
				else if (propertyTablesState.Status == TablesStatus.OutOfDate)
				{
					condition = true;
				}
				else if (propertyTablesState.Status == TablesStatus.CantRecomputeUseOldTables)
				{
					SharedData.ReportMessageGreen("NOTE! " + featureName + ": " + propertyTablesState.Message);
					condition = false;
				}
				else
				{
					if (verboseMessages)
					{
						SharedData.ReportMessage(featureName + ": " + propertyTablesState.Message);
					}
					condition = false;
				}
				if (!condition)
				{
					continue;
				}
				if (terrain == null)
				{
					throw new Exception("The geometry needs to be associated with a Terrain layer to compute tables. Go to the GIS Tools\\RAS Mapper window and right click on this geometry in the leftpanel and select 'Manage Geometry Associations ...' ");
				}
				SharedData.ReportMessage("Computing 2D Flow Area '" + featureName + "' tables: " + propertyTablesState.Message);
				Stopwatch stopwatch = Stopwatch.StartNew();
				using (new WaitCursor(SharedData.RasMapper))
				{
					if (SharedData.RasMapper == null)
					{
						if (!CreatePropertyTables(i, ProgressReportingType.ConsoleWriteline, simpleTables))
						{
							return false;
						}
					}
					else if (!CreatePropertyTables(i, ProgressReportingType.SharedDataReportMessage, simpleTables))
					{
						return false;
					}
				}
				stopwatch.Stop();
				SharedData.ReportMessage("2D Flow Area '" + featureName + "' tables complete " + Strings.Format((double)stopwatch.ElapsedMilliseconds / 1000.0, "0.00") + " sec");
			}
			return true;
		}
	}

	public PropertyTablesState ContainsCurrentTables(int meshIdx)
	{
		PropertyTablesState status = null;
		if (HasFatalPropertyTableStates(meshIdx, ref status))
		{
			return status;
		}
		string whyCantWeCompute = "";
		bool canComputePropertyTables = CanComputePropertyTables(ref whyCantWeCompute);
		PropertyTablesState propertyTablesState = CheckPropertyTablesVerifiableStates(meshIdx, Geometry.Terrain);
		if (canComputePropertyTables)
		{
			return propertyTablesState;
		}
		if (propertyTablesState.Status == TablesStatus.Good)
		{
			return new PropertyTablesState(TablesStatus.CantRecomputeUseOldTables, "Cannot recompute 2D property tables because: " + whyCantWeCompute + "Existing tables appear to match this geometry, so we'll use those.");
		}
		if (propertyTablesState.Status == TablesStatus.OutOfDate)
		{
			return new PropertyTablesState(TablesStatus.FatalError, "Cannot recompute 2D property tables because: " + whyCantWeCompute + "Existing tables DO NOT match this geometry because: " + propertyTablesState.Message);
		}
		return new PropertyTablesState(TablesStatus.FatalError, "Cannot recompute 2D property tables because: " + whyCantWeCompute);
	}

	private bool HasFatalPropertyTableStates(int meshIdx, ref PropertyTablesState status)
	{
		if (Geometry.IsResultFile)
		{
			status = new PropertyTablesState(TablesStatus.NotApplicable, "Cannot recompute 2D property tables - This is a result, not a geometry. N/A.");
			return true;
		}
		if (Geometry.Terrain == null || string.IsNullOrEmpty(Geometry.TerrainFilename))
		{
			status = new PropertyTablesState(TablesStatus.FatalError, "Cannot recompute 2D property tables - No terrain is associated with this geometry.\r\n                                         Go to the GIS Tools\\RAS Mapper window and right click on this geometry in the left \r\n                                         panel and select 'Manage Geometry Associations ...' ");
			return true;
		}
		if (!Mesh(meshIdx).IsComplete())
		{
			status = new PropertyTablesState(TablesStatus.FatalError, "Mesh " + GetFeatureName(meshIdx).InSingleQuotes() + " was not created successfully.");
			return true;
		}
		float filterTolCellVol = GetFilterTolCellVol(meshIdx);
		float filterCellMinAreaFrac = GetFilterCellMinAreaFrac(meshIdx);
		float filterTolFaceProfile = GetFilterTolFaceProfile(meshIdx);
		float filterTolFaceElevArea = GetFilterTolFaceElevArea(meshIdx);
		float faceAreaConveyanceRatioTol = GetFaceAreaConveyanceRatioTol(meshIdx);
		float faceLaminarDepth = GetFaceLaminarDepth(meshIdx);
		StringBuilder stringBuilder = new StringBuilder();
		bool condition = true;
		if (filterTolCellVol < 0f || float.IsNaN(filterTolCellVol))
		{
			stringBuilder.AppendLine("Cell Volume Tolerance must be >= 0. (0=Off, Default=" + DefaultCellVolFilterTol() + ")");
			condition = false;
		}
		if (filterTolFaceProfile < 0f || float.IsNaN(filterTolFaceProfile))
		{
			stringBuilder.AppendLine("Face Profile Tolerance must be >= 0. (0=Off, Default=" + DefaultFaceProfileFilterTol() + ")");
			condition = false;
		}
		if (filterTolFaceElevArea < 0f || float.IsNaN(filterTolFaceElevArea))
		{
			stringBuilder.AppendLine("Face Area Elevation Tolerance must be >= 0. (0=Off, Default=" + DefaultFaceAreaElevFilterTol() + ")");
			condition = false;
		}
		if (filterCellMinAreaFrac < 0f || filterCellMinAreaFrac > 1f || float.IsNaN(filterCellMinAreaFrac))
		{
			stringBuilder.AppendLine("Cell Minimum Area Fraction must be between 0 and 1. (0=Off, Default=" + 0.01f + ")");
			condition = false;
		}
		if (faceAreaConveyanceRatioTol < 0.0001f || faceAreaConveyanceRatioTol > 1f || float.IsNaN(faceAreaConveyanceRatioTol))
		{
			stringBuilder.AppendLine("Face Area Conveyance Ratio must be between " + 0.0001f + " and 1. (Default=" + 0.02f + ")");
			condition = false;
		}
		if (faceLaminarDepth < 0f || float.IsNaN(faceLaminarDepth))
		{
			stringBuilder.AppendLine("Laminar Depth must be >=0. (0=Off, Default=" + DefaultLaminarDepthTol() + ")");
			condition = false;
		}
		if (!condition)
		{
			status = new PropertyTablesState(TablesStatus.FatalError, stringBuilder.ToString());
			return true;
		}
		return false;
	}

	private bool CanComputePropertyTables(ref string whyCantWeCompute)
	{
		TerrainLayer terrain = Geometry.Terrain;
		bool condition = true;
		StringBuilder stringBuilder = new StringBuilder();
		if (!terrain.AllSourceFilesExist())
		{
			condition = false;
			stringBuilder.AppendLine("Terrain file(s) are missing.");
		}
		Action<GeometryAssociation> action = [SpecialName] (GeometryAssociation ga) =>
		{
			if (ga.HasAssociation)
			{
				if (ga.Layer == null)
				{
					condition = false;
					stringBuilder.AppendLine("Error initializing " + ga.AssociationName + " layer.");
				}
				else if (!ga.Layer.AllSourceFilesExist())
				{
					condition = false;
					stringBuilder.AppendLine(ga.AssociationName + " file(s) are missing.");
				}
			}
		};
		action(Geometry.NValueAssociation);
		action(Geometry.InfiltrationAssociation);
		action(Geometry.PercentImperviousAssociation);
		action(Geometry.SedimentSoilsAssociation);
		if (!condition)
		{
			whyCantWeCompute = stringBuilder.ToString();
		}
		return condition;
	}

	private bool ShouldRecomputePropertiesFromFilterToleranceChanges(H5Reader hr, int meshIdx, ref string msg)
	{
		string featureName = GetFeatureName(meshIdx);
		string setOrGroupName = MeshFV2D.MakeMeshGroupName(featureName);
		bool condition = true;
		string msg2 = "Tolerances have changed.";
		Action<string, float> action = [SpecialName] (string tolName, float cur) =>
		{
			float tryReadSingleAttributeScalar = hr.TryReadSingleAttributeScalar(setOrGroupName, tolName);
			if (tryReadSingleAttributeScalar != cur)
			{
				msg2 = msg2 + Environment.NewLine + tolName + " changed from " + tryReadSingleAttributeScalar.ToString().InSingleQuotes() + " to " + cur.ToString().InSingleQuotes();
				condition = false;
			}
		};
		action("Cell Volume Tolerance", GetFilterTolCellVol(meshIdx));
		action("Face Profile Tolerance", GetFilterTolFaceProfile(meshIdx));
		action("Face Area Elevation Tolerance", GetFilterTolFaceElevArea(meshIdx));
		action("Face Area Conveyance Ratio", GetFaceAreaConveyanceRatioTol(meshIdx));
		action("Cell Minimum Area Fraction", GetFilterCellMinAreaFrac(meshIdx));
		action("Laminar Depth", GetFaceLaminarDepth(meshIdx));
		action("Manning's n", GetManningsN(meshIdx));
		if (condition)
		{
			msg = msg2;
		}
		else
		{
			msg = "";
		}
		return condition;
	}

	private PropertyTablesState CheckPropertyTablesVerifiableStates(int meshIdx, TerrainLayer terrain)
	{
		_ = Geometry.NValueLayer;
		string featureName = GetFeatureName(meshIdx);
		string terrainFilename = Geometry.TerrainFilename;
		if (!File.Exists(SourceFilename))
		{
			return new PropertyTablesState(TablesStatus.OutOfDate, "HDF geometry file does not exist.");
		}
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		string meshGroupName = MeshFV2D.MakeMeshGroupName(featureName);
		if (!h5Reader.ExistsAsGroup(meshGroupName) || !h5Reader.ExistsAsDataset(meshGroupName + "/Cells Volume Elevation Info"))
		{
			return new PropertyTablesState(TablesStatus.OutOfDate, "Property tables do not exist.");
		}
		string msg = "";
		if (ShouldRecomputePropertiesFromFilterToleranceChanges(h5Reader, meshIdx, ref msg))
		{
			return new PropertyTablesState(TablesStatus.OutOfDate, msg);
		}
		string tryReadStringAttributeScalar = h5Reader.TryReadStringAttributeScalar(meshGroupName, "Terrain Filename");
		string tryReadStringAttributeScalar2 = h5Reader.TryReadStringAttributeScalar(meshGroupName, "Terrain File Date");
		if (string.IsNullOrEmpty(tryReadStringAttributeScalar) || string.IsNullOrEmpty(tryReadStringAttributeScalar2))
		{
			return new PropertyTablesState(TablesStatus.OutOfDate, "Property tables don't have associated terrain information.");
		}
		if (Operators.CompareString(tryReadStringAttributeScalar, RasMapperLib.Utilities.Path.MakeRelative(terrain.SourceFilename, SourceFilename), TextCompare: false) != 0 && Operators.CompareString(tryReadStringAttributeScalar, System.IO.Path.GetFileName(terrainFilename), TextCompare: false) != 0)
		{
			return new PropertyTablesState(TablesStatus.OutOfDate, "Associated terrain has been changed.");
		}
		if (File.Exists(terrain.SourceFilename) && Operators.CompareString(DSS.ConvertToDSSDateTime(FileSystem.FileDateTime(terrain.SourceFilename)), tryReadStringAttributeScalar2, TextCompare: false) != 0)
		{
			return new PropertyTablesState(TablesStatus.OutOfDate, "Associated terrain has been updated.");
		}
		GeometryAssociation geometryAssociation = new GeometryAssociation("Land Cover", Geometry.SourceFilename, [SpecialName] (GeometryAssociation ga) => Geometry.NValueLayer);
		geometryAssociation.LoadAssociation(h5Reader, "Geometry");
		PropertyTablesState propertyTablesState = CheckGeometryAssociation(h5Reader, meshGroupName, geometryAssociation, "Manning's n layer");
		if (propertyTablesState.Status != TablesStatus.Good)
		{
			return propertyTablesState;
		}
		propertyTablesState = CheckGeometryAssociation(h5Reader, meshGroupName, Geometry.InfiltrationAssociation, "Infiltration layer");
		if (propertyTablesState.Status != TablesStatus.Good)
		{
			return propertyTablesState;
		}
		propertyTablesState = CheckGeometryAssociation(h5Reader, meshGroupName, Geometry.PercentImperviousAssociation, "Percent Impervious layer");
		if (propertyTablesState.Status != TablesStatus.Good)
		{
			return propertyTablesState;
		}
		propertyTablesState = CheckGeometryAssociation(h5Reader, meshGroupName, Geometry.SedimentSoilsAssociation, "Sediment Bed Material layer");
		if (propertyTablesState.Status != TablesStatus.Good)
		{
			return propertyTablesState;
		}
		if (Geometry.NValueLayer != null)
		{
			Hash hash = new Hash();
			Geometry.LandCoverRegions.MD5HashAppend(hash);
			byte[] hash2 = hash.HashBytes();
			byte[] values = null;
			h5Reader.TryReadAttribute(meshGroupName, "Property Tables LC Hash", ref values);
			if (values.IsNullOrEmpty())
			{
				return new PropertyTablesState(TablesStatus.OutOfDate, "Manning's n calibration regions have been added or changed.");
			}
			if (!Hash.HashEquals(values, hash2))
			{
				return new PropertyTablesState(TablesStatus.OutOfDate, "Manning's n calibration regions have changed.");
			}
		}
		if (Geometry.InfiltrationLayer != null)
		{
			Hash hash3 = new Hash();
			Geometry.InfiltrationOverrideRegions.MD5HashAppend(hash3);
			byte[] hash4 = hash3.HashBytes();
			byte[] values2 = null;
			h5Reader.TryReadAttribute(meshGroupName, "Infiltration Override Table Hash", ref values2);
			if (values2.IsNullOrEmpty())
			{
				return new PropertyTablesState(TablesStatus.OutOfDate, "Infiltration calibration regions have been added or changed.");
			}
			if (!Hash.HashEquals(values2, hash4))
			{
				return new PropertyTablesState(TablesStatus.OutOfDate, "Infiltration calibration regions have changed.");
			}
		}
		return new PropertyTablesState(TablesStatus.Good, "Mesh property tables are current.");
	}

	private static string FileNameAttribute(string prefix)
	{
		return prefix + " Filename";
	}

	private static string FileDateModifiedAttribute(string prefix)
	{
		return prefix + " File Date";
	}

	private PropertyTablesState CheckGeometryAssociation(H5Reader hr, string meshgroup, GeometryAssociation geomAssociation, string friendlyLayerName)
	{
		GeometryAssociation geometryAssociation = new GeometryAssociation(geomAssociation.AssociationName, hr.Filename, null);
		geometryAssociation.LoadAssociation(hr, meshgroup);
		if (geomAssociation.HasAssociation)
		{
			Layer layer = geomAssociation.Layer;
			if (layer == null)
			{
				return new PropertyTablesState(TablesStatus.FatalError, "Association for " + friendlyLayerName + " failed to resolve to a layer.");
			}
			if (!geomAssociation.AbsoluteFilename.RoughlyEquals(geometryAssociation.AbsoluteFilename))
			{
				return new PropertyTablesState(TablesStatus.OutOfDate, "Associated " + friendlyLayerName + " has changed from " + geometryAssociation.RelativeFilename.InDoubleQuotes() + " to " + geomAssociation.RelativeFilename.InDoubleQuotes());
			}
			if (layer.AllSourceFilesExist())
			{
				string dateLastModifiedString = layer.DateLastModifiedString();
				string savedLayerDateModifiedString = geometryAssociation.SavedLayerDateModifiedString;
				if (Operators.CompareString(dateLastModifiedString, savedLayerDateModifiedString, TextCompare: false) != 0)
				{
					return new PropertyTablesState(TablesStatus.OutOfDate, "Associated " + friendlyLayerName + " layer has been updated (" + savedLayerDateModifiedString + " -> " + dateLastModifiedString + ")");
				}
				return new PropertyTablesState(TablesStatus.Good, "Mesh property tables are current.");
			}
			return new PropertyTablesState(TablesStatus.Good, "Some " + friendlyLayerName + " files are missing. Current property tables are not verifiably out of date, continuing.");
		}
		if (geometryAssociation.HasAssociation)
		{
			return new PropertyTablesState(TablesStatus.OutOfDate, "Property tables were previously computed with a " + friendlyLayerName + ", but the current geometry isn't associated with one.");
		}
		return new PropertyTablesState(TablesStatus.Good, "Mesh property tables do not have an association for a " + friendlyLayerName);
	}

	private bool CreatePropertyTables(int meshIdx, ProgressReportingType progressType, bool simpleTables = false)
	{
		GetFeatureName(meshIdx);
		ProgressReporter reporter = ProgressReporter.None();
		switch (progressType)
		{
		case ProgressReportingType.ConsoleWriteline:
			reporter = ProgressReporter.ConsoleWrite();
			break;
		case ProgressReportingType.SharedDataReportMessage:
			reporter = ProgressReporter.None();
			break;
		case ProgressReportingType.PopupWindow:
			throw new NotImplementedException();
		}
		return CreatePropertyTables(meshIdx, reporter, simpleTables);
	}

	public bool CreatePropertyTables(int meshIdx, ProgressReporter reporter = null, bool simpleTables = false)
	{
		string featureName = GetFeatureName(meshIdx);
		MeshFV2D meshFV2D = Mesh(meshIdx);
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		TerrainLayer terrain = Geometry.Terrain;
		_ = Geometry.NValueLayer;
		_ = Geometry.InfiltrationLayer;
		_ = Geometry.SedimentSoilsLayer;
		H5Reader.SetLogging(log: false);
		float filterTolCellVol = GetFilterTolCellVol(meshIdx);
		float filterCellMinAreaFrac = GetFilterCellMinAreaFrac(meshIdx);
		float filterTolFaceProfile = GetFilterTolFaceProfile(meshIdx);
		float filterTolFaceElevArea = GetFilterTolFaceElevArea(meshIdx);
		float faceAreaConveyanceRatioTol = GetFaceAreaConveyanceRatioTol(meshIdx);
		float faceLaminarDepth = GetFaceLaminarDepth(meshIdx);
		bool result;
		checked
		{
			using (H5Writer h5Writer = new H5Writer(SourceFilename))
			{
				string meshGroupName = MeshFV2D.MakeMeshGroupName(featureName);
				h5Writer.DeleteAttribute(meshGroupName, "Land Cover Filename");
				h5Writer.DeleteAttribute(meshGroupName, "Land Cover File Date");
				h5Writer.DeleteAttribute(meshGroupName, "Property Tables LC Hash");
				h5Writer.DeleteAttribute(meshGroupName, "Infiltration Filename");
				h5Writer.DeleteAttribute(meshGroupName, "Infiltration File Date");
				float manningsN = GetManningsN(meshIdx);
				if (float.IsNaN(manningsN))
				{
					manningsN = 0.06f;
				}
				Stopwatch stopwatch = new Stopwatch();
				Stopwatch stopwatch2 = new Stopwatch();
				Stopwatch stopwatch3 = new Stopwatch();
				Stopwatch stopwatch4 = new Stopwatch();
				Stopwatch stopwatch5 = Stopwatch.StartNew();
				stopwatch.Start();
				reporter.ReportMessage("Processing 2D Face Profiles (1/4)");
				reporter.ReportProgress(0);
				Profile[] faceProfiles = meshFV2D.ComputeFaceProfiles(terrain, filterTolFaceProfile, reporter);
				stopwatch.Stop();
				reporter.ReportProgress(100);
				float[] floatValues = new float[meshFV2D.Faces.Count - 1 + 1];
				Buffers.ArraySetAllVB(floatValues, manningsN);
				float[] floatValuesArray = new float[meshFV2D.CellCount - 1 + 1];
				Buffers.ArraySetAllVB(floatValuesArray, manningsN);
				reporter.ReportMessage("Processing 2D Manning's n layer (2/4)");
				if (!ProcessLandCoverPropertyTables(h5Writer, meshFV2D, meshGroupName, floatValues, floatValuesArray, manningsN, Geometry.NValueAssociation, reporter))
				{
					result = false;
				}
				else if (!ProcessClassificationPropertyTables(h5Writer, Geometry.InfiltrationAssociation, meshFV2D, meshGroupName, Geometry.InfiltrationResampler, reporter))
				{
					result = false;
				}
				else if (!ProcessClassificationPropertyTables(h5Writer, Geometry.PercentImperviousAssociation, meshFV2D, meshGroupName, Geometry.PercentImperviousResampler, reporter, new string[1] { "Percent Impervious" }))
				{
					result = false;
				}
				else
				{
					if (ProcessClassificationPropertyTables(h5Writer, Geometry.SedimentSoilsAssociation, meshFV2D, meshGroupName, Geometry.SedimentSoilsResampler, reporter))
					{
						Hash hash = new Hash();
						Geometry.InfiltrationOverrideRegions.MD5HashAppend(hash);
						h5Writer.WriteAttribute(meshGroupName, "Infiltration Override Table Hash", hash.HashBytes());
						stopwatch2.Start();
						reporter.ReportMessage("Processing 2D Face Tables (3/4)");
						reporter.ReportProgress(0);
						using (HDFBufferedWriter<int> hDFBufferedWriter = new HDFBufferedWriter<int>(h5Writer, meshGroupName + "/Faces Area Elevation Info", meshFV2D.Faces.Count, 2, "Face", "Starting Index", "Count"))
						{
							using HDFBufferedWriter<float> hDFBufferedWriter2 = new HDFBufferedWriter<float>(h5Writer, meshGroupName + "/Faces Area Elevation Values", 0, 4, "row", "Z", "Area", "Wetted Perimeter", "Manning's n");
							using HDFBufferedWriter<float> hDFBufferedWriter3 = new HDFBufferedWriter<float>(h5Writer, meshGroupName + "/Faces Minimum Elevation", meshFV2D.Faces.Count, 1, "Face", "Minimum Elevation");
							using HDFBufferedWriter<float> hDFBufferedWriter4 = new HDFBufferedWriter<float>(h5Writer, meshGroupName + "/Faces Low Elevation Centroid", meshFV2D.Faces.Count, 1, "Face", "Centroid station for bottom 5% of the face area");
							int progressMod = RasMapperLib.Utilities.Math.ProgressMod(meshFV2D.Faces.Count);
							int countOffset = meshFV2D.Faces.Count - 1;
							int ordinal = 0;
							while (ordinal <= countOffset)
							{
								if (unchecked(ordinal % progressMod) == 0)
								{
									reporter.ReportProgress((int)System.Math.Round(100f * (float)ordinal / (float)meshFV2D.Faces.Count));
								}
								RasMapperLib.Mesh.Face face = meshFV2D.Faces[ordinal];
								Profile profile = faceProfiles[ordinal];
								faceProfiles[ordinal] = null;
								if (profile.Count != 0)
								{
									ProfileHydraulicPropertiesTable profileHydraulicPropertiesTable = new ProfileHydraulicPropertiesTable(profile, filterTolFaceElevArea, faceAreaConveyanceRatioTol, faceLaminarDepth);
									float col = profileHydraulicPropertiesTable.ComputeCentroidAtArea(0.05f);
									hDFBufferedWriter4.AddRow(col);
									face.MinimumElevation = profileHydraulicPropertiesTable.HydrProp.First().Elevation;
									hDFBufferedWriter3.AddRow(face.MinimumElevation);
									hDFBufferedWriter.AddRow(hDFBufferedWriter2.RowsWritten, profileHydraulicPropertiesTable.HydrProp.Count());
									ProfileHydraulicPropertiesTable.HydraulicProperties[] hydrProp = profileHydraulicPropertiesTable.HydrProp;
									for (int i = 0; i < hydrProp.Length; i++)
									{
										ProfileHydraulicPropertiesTable.HydraulicProperties hydraulicProperties = hydrProp[i];
										hDFBufferedWriter2.AddRow(hydraulicProperties.Elevation, hydraulicProperties.Area, hydraulicProperties.WettedPerimeter, floatValues[ordinal]);
									}
									meshFV2D.SetFaceMinElevation(ordinal, face.MinimumElevation);
									ordinal++;
									continue;
								}
								StringBuilder stringBuilder = new StringBuilder();
								stringBuilder.Append(ordinal.ToString());
								int ordinal2 = ordinal + 1;
								int count = meshFV2D.Faces.Count - 1;
								for (int j = ordinal2; j <= count; j++)
								{
									if (faceProfiles[j].Count == 0)
									{
										stringBuilder.Append(", ");
										stringBuilder.Append(j.ToString());
									}
								}
								reporter.ReportError("Unable to compute face tables. The terrain associated with this geometry does not completely cover the 2D Flow Area '" + featureName + "'. See face(s) #(" + stringBuilder.ToString() + ")");
								result = false;
								goto end_IL_0093;
							}
						}
						faceProfiles = null;
						GC.Collect();
						stopwatch2.Stop();
						reporter.ReportProgress(100);
						reporter.ReportMessage("Processing 2D Cells (4/4)");
						reporter.ReportProgress(0);
						float[] values = new float[10000];
						bool[] boolValues = new bool[10000];
						List<SegmentM> stitches = null;
						Histogram histogram = new Histogram(256);
						Point2DCollection point2DCollection = new Point2DCollection();
						Point2DCollection point2DCollection2 = new Point2DCollection();
						using (H5Reader h5Reader = new H5Reader(terrain.SourceFilename))
						{
							Dictionary<string, TiffFile> tiffFileByString = new Dictionary<string, TiffFile>();
							int count2 = terrain.RasterFileCount - 1;
							for (int k = 0; k <= count2; k++)
							{
								string get_RasterFilename = terrain.get_RasterFilename(k);
								if (!File.Exists(get_RasterFilename))
								{
									DictionaryExtensionMethods.DisposeAll(tiffFileByString);
									throw new Exception("Error resampling terrain. File '" + get_RasterFilename + "' does not exist.");
								}
								tiffFileByString.Add(get_RasterFilename, new TiffFile(get_RasterFilename));
							}
							try
							{
								using (HDFBufferedWriter<int> hDFBufferedWriter5 = new HDFBufferedWriter<int>(h5Writer, meshGroupName + "/Cells Volume Elevation Info", meshFV2D.CellCount, 2, "Cell", "Starting Index", "Count"))
								{
									using HDFBufferedWriter<float> hDFBufferedWriter6 = new HDFBufferedWriter<float>(h5Writer, meshGroupName + "/Cells Volume Elevation Values", 0, 2, "row", "Elevation", "Volume");
									using HDFBufferedWriter<float> hDFBufferedWriter7 = new HDFBufferedWriter<float>(h5Writer, meshGroupName + "/Cells Surface Area", meshFV2D.CellCount, 1, "Cell", "Surface Area");
									using HDFBufferedWriter<float> hDFBufferedWriter8 = new HDFBufferedWriter<float>(h5Writer, meshGroupName + "/Cells Center Manning's n", meshFV2D.CellCount, 1, "Cell", "Index");
									Stopwatch stopwatch6 = new Stopwatch();
									int progressMod2 = RasMapperLib.Utilities.Math.ProgressMod(meshFV2D.CellCount);
									int count3 = meshFV2D.CellCount - 1;
									double x = default;
									double y = default;
									for (int l = 0; l <= count3; l++)
									{
										if (unchecked(l % progressMod2) == 0)
										{
											reporter.ReportProgress((int)System.Math.Round(100f * (float)l / (float)meshFV2D.CellCount));
										}
										RasMapperLib.Mesh.Cell cell = meshFV2D.Cell(l);
										Polygon polygon = meshFV2D.CellPolygon(l);
										double area = polygon.Area;
										point2DCollection2.Clear();
										if (cell.Faces.Count > 1)
										{
											stopwatch4.Start();
											Array.Clear(boolValues, 0, boolValues.Count());
											RasterM rasterM = RasterM.ComputeRasterM(10000, polygon.MaxX, polygon.MinX, polygon.MaxY, polygon.MinY);
											stopwatch4.Stop();
											stopwatch3.Start();
											terrain.Resample(rasterM, ref values, ref stitches, h5Reader.Filename, tiffFileByString);
											stopwatch3.Stop();
											stopwatch4.Start();
											stopwatch6.Start();
											polygon.Rasterize(rasterM, boolValues);
											stopwatch6.Stop();
											float minValue = float.MinValue;
											float maxValue = float.MaxValue;
											int count4 = rasterM.Cols * rasterM.Rows - 1;
											for (int m = 0; m <= count4; m++)
											{
												if (!boolValues[m])
												{
													continue;
												}
												float computed = values[m];
												if (computed != -9999f)
												{
													if (computed > minValue)
													{
														minValue = computed;
													}
													if (computed < maxValue)
													{
														maxValue = computed;
													}
												}
											}
											if (minValue > maxValue)
											{
												histogram.Reset(maxValue, minValue);
												double cellSizePolynomial = rasterM.CellSize * rasterM.CellSize;
												float minValue2 = float.MinValue;
												int unity = 0;
												int count5 = rasterM.Cols * rasterM.Rows - 1;
												for (int n = 0; n <= count5; n++)
												{
													float minValue22 = values[n];
													if (boolValues[n] && minValue22 != -9999f)
													{
														if (minValue22 == minValue2)
														{
															unity++;
															continue;
														}
														histogram.AddArea(minValue2, cellSizePolynomial * (double)unity);
														minValue2 = minValue22;
														unity = 1;
													}
												}
												histogram.AddArea(minValue2, cellSizePolynomial * (double)unity);
												histogram.ComputeCumulativeValues(point2DCollection);
												float ratio = (float)(area / (double)point2DCollection.Last().X);
												point2DCollection.RatioX(ratio);
												point2DCollection2.Clear();
												point2DCollection2.Add(0f, maxValue);
												float computed2 = 0f;
												int count6 = point2DCollection.Count - 1;
												for (int unity2 = 1; unity2 <= count6; unity2++)
												{
													float point2DCollectionTerm = (float)(0.5 * (double)(point2DCollection[unity2 - 1].X + point2DCollection[unity2].X));
													float point2DCollectionTerm2 = point2DCollection[unity2].Y - point2DCollection[unity2 - 1].Y;
													computed2 += point2DCollectionTerm2 * point2DCollectionTerm;
													point2DCollection2.Add(computed2, point2DCollection[unity2].Y);
												}
												if (filterTolCellVol > 0f)
												{
													point2DCollection2 = point2DCollection2.Filter(filterTolCellVol);
												}
												if (filterCellMinAreaFrac > 0f)
												{
													double areaTimesFilterCellMinAreaFrac = area * (double)filterCellMinAreaFrac;
													int count7 = point2DCollection2.Count - 1;
													for (int unity3 = 1; unity3 <= count7; unity3++)
													{
														if (!((double)(point2DCollection2[unity3].X / (point2DCollection2[unity3].Y - point2DCollection2[0].Y)) > areaTimesFilterCellMinAreaFrac))
														{
															continue;
														}
														if (unity3 == 1)
														{
															break;
														}
														SegmentM segmentM = new SegmentM(point2DCollection2[unity3 - 1].X, point2DCollection2[unity3 - 1].Y, point2DCollection2[unity3].X, point2DCollection2[unity3].Y);
														SegmentM seg = new SegmentM(0.0, point2DCollection2[0].Y, (double)(point2DCollection2[unity3].Y - point2DCollection2[0].Y) * areaTimesFilterCellMinAreaFrac, point2DCollection2[unity3].Y);
														if (segmentM.Intersects(seg, ref x, ref y))
														{
															if (unity3 > 2)
															{
																point2DCollection2.RemoveRange(1, unity3 - 2);
															}
															if ((double)point2DCollection2[2].Y - y > (double)filterTolCellVol)
															{
																point2DCollection2[1] = new Point2D((float)x, (float)y);
															}
															else
															{
																point2DCollection2.RemoveAt(1);
															}
														}
														break;
													}
												}
											}
											else
											{
												point2DCollection2.Clear();
												point2DCollection2.Add(0f, maxValue);
											}
											if (point2DCollection2.Count > 0)
											{
												float maxValue2 = float.MaxValue;
												foreach (int face3 in cell.Faces)
												{
													maxValue2 = System.Math.Min(maxValue2, meshFV2D.Faces[face3].MinimumElevation);
												}
												if (maxValue2 < point2DCollection2[0].Y)
												{
													point2DCollection2.AssignY(0, maxValue2);
												}
												if (simpleTables)
												{
													double computed3 = 0.0;
													int ordinal3 = 0;
													int count8 = rasterM.Cols * rasterM.Rows - 1;
													for (int intervalIndex = 0; intervalIndex <= count8; intervalIndex++)
													{
														if (boolValues[intervalIndex] && values[intervalIndex] != -9999f)
														{
															computed3 += (double)values[intervalIndex];
															ordinal3++;
														}
													}
													double y2 = computed3 / (double)ordinal3;
													point2DCollection2.Clear();
													point2DCollection2.Add(0.0, y2);
												}
											}
											if (point2DCollection2.Count > 0)
											{
												cell.MinimumElevation = point2DCollection2.First().Y;
											}
											else
											{
												cell.MinimumElevation = float.NaN;
											}
											stopwatch4.Stop();
										}
										else
										{
											cell.MinimumElevation = float.NaN;
											point2DCollection2.Clear();
										}
										hDFBufferedWriter5.AddRow(hDFBufferedWriter6.RowsWritten, point2DCollection2.Count);
										foreach (Point2D point2DCollection22 in point2DCollection2)
										{
											hDFBufferedWriter6.AddRow(point2DCollection22.Y, point2DCollection22.X);
										}
										hDFBufferedWriter7.AddRow((float)area);
										hDFBufferedWriter8.AddRow(floatValuesArray[l]);
										meshFV2D.SetCellMinElevation(l, cell.MinimumElevation);
									}
								}
								if (simpleTables)
								{
									using HDFBufferedWriter<int> hDFBufferedWriter9 = new HDFBufferedWriter<int>(h5Writer, meshGroupName + "/Faces Area Elevation Info", meshFV2D.Faces.Count, 2, "Face", "Starting Index", "Count");
									using HDFBufferedWriter<float> hDFBufferedWriter10 = new HDFBufferedWriter<float>(h5Writer, meshGroupName + "/Faces Area Elevation Values", 0, 4, "row", "Z", "Area", "Wetted Perimeter", "Manning's n");
									using HDFBufferedWriter<float> hDFBufferedWriter11 = new HDFBufferedWriter<float>(h5Writer, meshGroupName + "/Faces Minimum Elevation", meshFV2D.Faces.Count, 1, "Face", "Minimum Elevation");
									int count9 = meshFV2D.Faces.Count - 1;
									for (int intervalIndex2 = 0; intervalIndex2 <= count9; intervalIndex2++)
									{
										RasMapperLib.Mesh.Face face2 = meshFV2D.Faces[intervalIndex2];
										float minimumElevation = face2.MinimumElevation;
										if (!float.IsNaN(meshFV2D.Cell(face2.cellA).MinimumElevation))
										{
											minimumElevation = System.Math.Max(minimumElevation, meshFV2D.Cell(face2.cellA).MinimumElevation);
										}
										if (!float.IsNaN(meshFV2D.Cell(face2.cellB).MinimumElevation))
										{
											minimumElevation = System.Math.Max(minimumElevation, meshFV2D.Cell(face2.cellB).MinimumElevation);
										}
										hDFBufferedWriter11.AddRow(minimumElevation);
										hDFBufferedWriter9.AddRow(hDFBufferedWriter10.RowsWritten, 1);
										hDFBufferedWriter10.AddRow(minimumElevation, 0f, (float)meshFV2D.FacePolylineLength(intervalIndex2), floatValues[intervalIndex2]);
									}
								}
							}
							catch (Exception ex)
							{
								ProjectData.SetProjectError(ex);
								Exception ex2 = ex;
								reporter.ReportError("Property Tables error: " + ex2.Message);
								result = false;
								ProjectData.ClearProjectError();
								goto end_IL_0093;
							}
							finally
							{
								DictionaryExtensionMethods.DisposeAll(tiffFileByString);
							}
						}
						reporter.ReportProgress(100);
						meshFV2D.CellMinimumElevationSet(cellMinimumElevationSet: true);
						using (HDFBufferedWriter<float> hDFBufferedWriter12 = new HDFBufferedWriter<float>(h5Writer, CellMinimumDataset(meshGroupName), meshFV2D.CellCount, 1, "Cell", "Minimum Elevation"))
						{
							int count10 = meshFV2D.CellCount - 1;
							for (int ordinal4 = 0; ordinal4 <= count10; ordinal4++)
							{
								hDFBufferedWriter12.AddRow(meshFV2D.Cell(ordinal4).MinimumElevation);
							}
						}
						stopwatch5.Stop();
						h5Writer.WriteAttribute(meshGroupName, "Terrain Filename", RasMapperLib.Utilities.Path.MakeRelative(terrain.SourceFilename, SourceFilename));
						h5Writer.WriteAttribute(meshGroupName, "Terrain File Date", terrain.SourceFileDateString);
						h5Writer.WriteAttribute(meshGroupName, "Cell Volume Tolerance", filterTolCellVol);
						h5Writer.WriteAttribute(meshGroupName, "Face Profile Tolerance", filterTolFaceProfile);
						h5Writer.WriteAttribute(meshGroupName, "Face Area Elevation Tolerance", filterTolFaceElevArea);
						h5Writer.WriteAttribute(meshGroupName, "Face Area Conveyance Ratio", faceAreaConveyanceRatioTol);
						h5Writer.WriteAttribute(meshGroupName, "Manning's n", manningsN);
						string convertToDSSDateTime = DSS.ConvertToDSSDateTime(DateTime.Now);
						h5Writer.WriteAttribute(meshGroupName, "Property Tables Last Computed", convertToDSSDateTime);
						goto IL_10d3;
					}
					result = false;
				}
				end_IL_0093:;
			}
			goto IL_10d5;
		}
		IL_10d5:
		return result;
		IL_10d3:
		result = true;
		goto IL_10d5;
	}

	private bool ProcessLandCoverPropertyTables(H5Writer hw, MeshFV2D mesh, string meshgroup, float[] faceMannN, float[] cellCenterMannN, float defaultMannN, GeometryAssociation lcAssociation, ProgressReporter reporter)
	{
		if (!(lcAssociation.Layer is LandCoverLayer landCoverLayer))
		{
			if (lcAssociation.HasAssociation)
			{
				reporter.ReportError("Error loading n-value associated layer.");
				return false;
			}
			reporter.ReportProgress(100);
			return true;
		}
		if (!landCoverLayer.AllSourceFilesExist())
		{
			reporter.ReportError("Land Cover (n-value) source files missing.");
			reporter.ReportProgress(100);
			return false;
		}
		reporter.ReportProgress(0);
		RasLandCoverRegions landCoverRegions = Geometry.LandCoverRegions;
		FinalNValueLayer landCoverManningsN = Geometry.LandCoverManningsN;
		landCoverManningsN.EnsureCurrentData();
		ParameterResampler nValueResampler = Geometry.NValueResampler;
		if (nValueResampler == null)
		{
			reporter.ReportError("Could not load land-cover resampler for associated layer " + landCoverLayer.SourceFilename.InDoubleQuotes());
			return false;
		}
		int indexOf = ListExtensionMethods.IndexOf(nValueResampler.BaseVariables, [SpecialName] (Parameter v) => Classification.NameMatches(v.VariableName, "ManningsN"));
		if (indexOf == -1)
		{
			reporter.ReportError("Could not load Manning's n information for associated layer " + landCoverLayer.SourceFilename.InDoubleQuotes());
			return false;
		}
		checked
		{
			Geospatial.Vectors.Point[] points = new Geospatial.Vectors.Point[mesh.Faces.Count - 1 + 1];
			int count = mesh.Faces.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				points[intervalIndex] = Converter.Convert(mesh.FaceSegment(intervalIndex).MidPoint());
			}
			float[] floatValues = new float[points.Length - 1 + 1];
			nValueResampler.SamplePoints(points, indexOf, floatValues);
			int count2 = faceMannN.Length - 1;
			for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
			{
				float faceMannN2 = floatValues[intervalIndex2];
				if (faceMannN2 == -9999f || float.IsNaN(faceMannN2) || faceMannN2 == float.MaxValue)
				{
					faceMannN[intervalIndex2] = defaultMannN;
				}
				else
				{
					faceMannN[intervalIndex2] = faceMannN2;
				}
			}
			Geospatial.Vectors.Point[] pointsArray = new Geospatial.Vectors.Point[mesh.CellCount - 1 + 1];
			int count3 = mesh.CellCount - 1;
			for (int intervalIndex3 = 0; intervalIndex3 <= count3; intervalIndex3++)
			{
				pointsArray[intervalIndex3] = Converter.Convert(mesh.Cell(intervalIndex3).Point);
			}
			nValueResampler.SamplePoints(pointsArray, indexOf, floatValues);
			int count4 = cellCenterMannN.Length - 1;
			for (int intervalIndex4 = 0; intervalIndex4 <= count4; intervalIndex4++)
			{
				float cellCenterMannN2 = floatValues[intervalIndex4];
				if (cellCenterMannN2 == -9999f || float.IsNaN(cellCenterMannN2) || cellCenterMannN2 == float.MaxValue)
				{
					cellCenterMannN[intervalIndex4] = defaultMannN;
				}
				else
				{
					cellCenterMannN[intervalIndex4] = cellCenterMannN2;
				}
			}
			lcAssociation.SaveAssociation(hw, meshgroup);
			Hash hash = new Hash();
			landCoverRegions.MD5HashAppend(hash);
			hw.WriteAttribute(meshgroup, "Property Tables LC Hash", hash.HashBytes());
			reporter.ReportProgress(100);
			return true;
		}
	}

	private static bool ProcessClassificationPropertyTables(H5Writer hw, GeometryAssociation association, MeshFV2D mesh, string meshgroup, ParameterResampler resampler, ProgressReporter reporter, IList<string> desiredParameters = null)
	{
		_Closure_0024__70_002D0 capturedLocals = default;
		_Closure_0024__70_002D0 capturedLocals2 = new _Closure_0024__70_002D0(capturedLocals);
		capturedLocals2.desiredParameters = desiredParameters;
		if (association == null)
		{
			return true;
		}
		string meshgroupAssociationNameTerm = meshgroup + "/" + association.AssociationName;
		if (hw.ExistsAsGroup(meshgroupAssociationNameTerm))
		{
			hw.Delete(meshgroupAssociationNameTerm);
		}
		if (!association.HasAssociation)
		{
			return true;
		}
		LandCoverLayer landCoverLayer = (LandCoverLayer)association.Layer;
		if (landCoverLayer == null)
		{
			return true;
		}
		List<string> except = null;
		if (landCoverLayer.LCType == LandCoverLayer.LandCoverType.InfiltrationSCSCurveNumber)
		{
			except = ParameterNames.InfiltrationVariables.SCSCurveNumber.AllVariables.Except(ParameterNames.InfiltrationVariables.SCSCurveNumber.MandatoryVariables).ToList();
		}
		else if (landCoverLayer.LCType == LandCoverLayer.LandCoverType.InfiltrationSCSCurveNumber)
		{
			except = ParameterNames.InfiltrationVariables.GreenAmpt.AllVariables.Except(ParameterNames.InfiltrationVariables.GreenAmpt.MandatoryVariablesBase).ToList();
		}
		else if (landCoverLayer.LCType == LandCoverLayer.LandCoverType.InfiltrationDeficitConstantLoss)
		{
			except = ParameterNames.InfiltrationVariables.DeficitConstantLoss.AllVariables.Except(ParameterNames.InfiltrationVariables.DeficitConstantLoss.MandatoryVariables).ToList();
		}
		checked
		{
			if (landCoverLayer.AllSourceFilesExist())
			{
				reporter.ReportMessage("Processing " + association.AssociationName + " Data (2/4)");
				hw.CreateGroup(meshgroupAssociationNameTerm);
				if (resampler == null)
				{
					reporter.ReportError("Could not load data for associated layer " + landCoverLayer.SourceFilename.InDoubleQuotes() + ". " + association.AssociationName + " parameters not written.");
					return false;
				}
				Geospatial.Vectors.Point[] points = new Geospatial.Vectors.Point[mesh.NonVirtualCellCount - 1 + 1];
				int count = mesh.NonVirtualCellCount - 1;
				for (int i = 0; i <= count; i++)
				{
					points[i] = Converter.Convert(mesh.Cell(i).Point);
				}
				Geospatial.Vectors.Point[] pointsArray = new Geospatial.Vectors.Point[mesh.FaceCount - 1 + 1];
				int count2 = mesh.FaceCount - 1;
				for (int j = 0; j <= count2; j++)
				{
					pointsArray[j] = Converter.Convert(mesh.FaceSegment(j).MidPointXY());
				}
				int[] classifications = null;
				int[] classifications2 = null;
				Dictionary<string, float[]> samplePointsMapped = resampler.SamplePointsMapped(points, out classifications);
				resampler.SamplePointsMapped(pointsArray, out classifications2);
				if (capturedLocals2.desiredParameters != null)
				{
					samplePointsMapped.RemoveWhere([SpecialName] (KeyValuePair<string, float[]> kvp) => !capturedLocals2.desiredParameters.RoughlyContains(kvp.Key));
				}
				foreach (KeyValuePair<string, float[]> samplePointsMappedArray in samplePointsMapped)
				{
					hw.WriteArray(meshgroupAssociationNameTerm + "/" + samplePointsMappedArray.Key, samplePointsMappedArray.Value);
				}
				hw.WriteArray(meshgroupAssociationNameTerm + "/Cell Center Classifications", classifications);
				hw.WriteArray(meshgroupAssociationNameTerm + "/Face Center Classifications", classifications2);
				bool condition = false;
				foreach (KeyValuePair<string, float[]> samplePointsMapped2 in samplePointsMapped)
				{
					List<int> intList = new List<int>();
					int count3 = mesh.NonVirtualCellCount - 1;
					for (int intervalIndex = 0; intervalIndex <= count3; intervalIndex++)
					{
						if (samplePointsMapped2.Value[intervalIndex] == -9999f)
						{
							intList.Add(intervalIndex);
						}
						if (intList.Count == 10)
						{
							break;
						}
					}
					if (intList.Count > 0 && (except == null || !except.Contains(samplePointsMapped2.Key)))
					{
						string textValue = string.Join(",", intList);
						if (intList.Count == 10)
						{
							textValue += "...";
						}
						reporter.ReportError("Parameter " + samplePointsMapped2.Key + " has no value for these cells: " + textValue.InParentheses());
						condition = true;
					}
				}
				if (condition)
				{
					return false;
				}
				landCoverLayer.WritePropertiesIfExist(hw, meshgroupAssociationNameTerm + "/");
				association.SaveAssociation(hw, meshgroup);
				association.SaveAssociation(hw, meshgroupAssociationNameTerm);
				return true;
			}
			reporter.ReportError("Associated layer missing necessary files: " + association.AbsoluteFilename);
			return false;
		}
	}

	public string MakeMeshGroupName(int meshIdx)
	{
		return MakeMeshGroupName(GetFeatureName(meshIdx));
	}

	public static string MakeMeshGroupName(string meshname)
	{
		return "Geometry/2D Flow Areas/" + meshname;
	}

	public float[] ReadAverageCellElevation(int meshIdx)
	{
		return ReadAverageCellElevation(GetFeatureName(meshIdx));
	}

	public float[] ReadAverageCellElevation(string meshname)
	{
		List<Point2DCollection> cellVolumeTables = ReadCellVolumeTables(meshname);
		checked
		{
			float[] averageCellElevation = new float[cellVolumeTables.Count - 1 + 1];
			int count = cellVolumeTables.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2DCollection point2DCollection = cellVolumeTables[i];
				if (point2DCollection.Count == 0)
				{
					averageCellElevation[i] = float.NaN;
					continue;
				}
				if (point2DCollection.Count == 1)
				{
					averageCellElevation[i] = point2DCollection.First().Y;
					continue;
				}
				averageCellElevation[i] = point2DCollection.Average([SpecialName] (Point2D p2D) => p2D.Y);
			}
			return averageCellElevation;
		}
	}

	public float[] ReadCellSurfaceArea(H5Reader h5r, string meshName, int[] cellIndexes = null)
	{
		float[] values = null;
		string concat = string.Concat("Geometry/2D Flow Areas/" + meshName, "/Cells Surface Area");
		if (!h5r.ExistsAsDataset(concat))
		{
			return null;
		}
		h5r.ReadDataset(concat, ref values);
		checked
		{
			if (cellIndexes != null)
			{
				float[] floatValues = new float[cellIndexes.Length - 1 + 1];
				int ordinal = 0;
				int count = cellIndexes.Length - 1;
				for (int i = 0; i <= count; i++)
				{
					floatValues[ordinal] = values[cellIndexes[i]];
					ordinal++;
				}
			}
			return values;
		}
	}

	public List<Point2DCollection> ReadCellVolumeTables(int meshIdx, int[] cellIndexes = null)
	{
		return ReadCellVolumeTables(GetFeatureName(meshIdx), cellIndexes);
	}

	public List<Point2DCollection> ReadCellVolumeTables(string meshName, int[] cellIndexes = null)
	{
		return ReadTable(meshName, "Cells Volume Elevation", 1, 0, cellIndexes);
	}

	public List<Point2DCollection> ReadCellVolumeTables(H5Reader h5r, int meshIdx, int[] cellIndexes = null)
	{
		return ReadCellVolumeTables(h5r, GetFeatureName(meshIdx), cellIndexes);
	}

	public List<Point2DCollection> ReadCellVolumeTables(H5Reader h5r, string meshName, int[] cellIndexes = null)
	{
		return ReadTable(h5r, meshName, "Cells Volume Elevation", 1, 0, cellIndexes);
	}

	public List<Point2DCollection> ReadFaceAreaTables(H5Reader h5r, int meshIdx, int[] faceIndexes = null)
	{
		return ReadTable(h5r, GetFeatureName(meshIdx), "Faces Area Elevation", 1, 0, faceIndexes);
	}

	public List<Point2DCollection> ReadFaceAreaTables(int meshIdx, int[] faceIndexes = null)
	{
		return ReadFaceAreaTables(GetFeatureName(meshIdx), faceIndexes);
	}

	public List<Point2DCollection> ReadFaceAreaTables(string meshName, int[] faceIndexes = null)
	{
		return ReadTable(meshName, "Faces Area Elevation", 1, 0, faceIndexes);
	}

	public List<Point2DCollection> ReadFacePerimeterTables(H5Reader h5r, int meshIdx, int[] faceIndexes = null)
	{
		return ReadTable(h5r, GetFeatureName(meshIdx), "Faces Area Elevation", 2, 0, faceIndexes);
	}

	public List<Point2DCollection> ReadFacePerimeterTables(int meshIdx, int[] faceIndexes = null)
	{
		return ReadFacePerimeterTables(GetFeatureName(meshIdx), faceIndexes);
	}

	public List<Point2DCollection> ReadFacePerimeterTables(string meshName, int[] faceIndexes = null)
	{
		return ReadTable(meshName, "Faces Area Elevation", 2, 0, faceIndexes);
	}

	public float[] ReadFaceMannValues(H5Reader h5r, int meshIdx, int[] faceIndexes = null)
	{
		return (from col in ReadTable(h5r, GetFeatureName(meshIdx), "Faces Area Elevation", 3, 0, faceIndexes)
			select col.First().X).ToArray();
	}

	public float[] ReadFaceMannValues(int meshIdx, int[] faceIndexes = null)
	{
		return ReadFaceMannValues(GetFeatureName(meshIdx), faceIndexes);
	}

	public float[] ReadFaceMannValues(string meshName, int[] faceIndexes = null)
	{
		return (from col in ReadFaceManningsNTables(meshName, faceIndexes)
			select col.First().X).ToArray();
	}

	public List<Point2DCollection> ReadFaceManningsNTables(int meshIdx, int[] faceIndexes = null)
	{
		return ReadFaceManningsNTables(GetFeatureName(meshIdx), faceIndexes);
	}

	public List<Point2DCollection> ReadFaceManningsNTables(string meshName, int[] faceIndexes = null)
	{
		return ReadTable(meshName, "Faces Area Elevation", 3, 0, faceIndexes);
	}

	public List<Point2DCollection> ReadFaceConveyanceTables(int meshIdx, int[] faceIndexes = null)
	{
		return ReadFaceConveyanceTables(GetFeatureName(meshIdx), faceIndexes);
	}

	public List<Point2DCollection> ReadFaceConveyanceTables(string meshName, int[] faceIndexes = null)
	{
		if (string.IsNullOrEmpty(meshName))
		{
			return null;
		}
		List<Point2DCollection> faceAreaTables = ReadFaceAreaTables(meshName, faceIndexes);
		List<Point2DCollection> facePerimeterTables = ReadFacePerimeterTables(meshName, faceIndexes);
		List<Point2DCollection> faceManningsNTables = ReadFaceManningsNTables(meshName, faceIndexes);
		List<Point2DCollection> point2DCollections = new List<Point2DCollection>(faceAreaTables.Count);
		checked
		{
			int count = faceAreaTables.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				point2DCollections.Add(new Point2DCollection());
				int count2 = faceAreaTables[i].Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					float x3X2Term = ((SharedData.Units != SharedData.EnumUnits.USCustomary) ? 1f : 1.486f);
					float y = faceManningsNTables[i][j].Y;
					float x = faceManningsNTables[i][j].X;
					float x2 = faceAreaTables[i][j].X;
					float x3 = facePerimeterTables[i][j].X;
					_ = x2 / x3;
					x3X2Term = ((!((double)x3 > 0.0)) ? 0f : ((float)((double)(x3X2Term / x * x2) * System.Math.Pow(x2 / x3, 2.0 / 3.0))));
					point2DCollections[i].Add(new Point2D(x3X2Term, y));
				}
			}
			return point2DCollections;
		}
	}

	private List<Point2DCollection> ReadTable(string meshName, string groupPrefix, int xcol, int ycol, int[] cellIndexes = null)
	{
		if (!File.Exists(SourceFilename))
		{
			return null;
		}
		using H5Reader hr = new H5Reader(SourceFilename);
		return ReadTable(hr, meshName, groupPrefix, xcol, ycol, cellIndexes);
	}

	private List<Point2DCollection> ReadTable(H5Reader hr, string meshName, string groupPrefix, int xcol, int ycol, int[] cellIndexes = null)
	{
		string meshNameTerm = "Geometry/2D Flow Areas/" + meshName;
		string groupPrefixTerm = meshNameTerm + "/" + groupPrefix + " Info";
		string groupPrefixTerm2 = meshNameTerm + "/" + groupPrefix + " Values";
		if (!hr.ExistsAsGroup(meshNameTerm))
		{
			return null;
		}
		if (!hr.ExistsAsDataset(groupPrefixTerm))
		{
			return null;
		}
		if (!hr.ExistsAsDataset(groupPrefixTerm2))
		{
			return null;
		}
		List<Point2DCollection> point2DCollections = new List<Point2DCollection>();
		int[,] values = new int[1, 1];
		hr.ReadDataset(groupPrefixTerm, ref values);
		checked
		{
			if (cellIndexes == null)
			{
				float[,] values2 = null;
				hr.ReadDataset(groupPrefixTerm2, ref values2);
				int length = values2.GetLength(1);
				point2DCollections.Capacity = values.GetLength(0);
				int count = values.GetLength(0) - 1;
				for (int i = 0; i <= count; i++)
				{
					Point2DCollection point2DCollection = null;
					int ordinal = values[i, 0];
					int ordinal2 = values[i, 1];
					if (ordinal2 > 0)
					{
						float[,] floatValues = new float[ordinal2 - 1 + 1, length - 1 + 1];
						Buffer.BlockCopy(values2, ordinal * length * 4, floatValues, 0, floatValues.Length * 4);
						point2DCollection = new Point2DCollection(floatValues, xcol, ycol);
					}
					point2DCollections.Add(point2DCollection);
				}
			}
			else
			{
				point2DCollections.Capacity = cellIndexes.Count();
				foreach (int cellIndex in cellIndexes)
				{
					Point2DCollection point2DCollection2 = null;
					if (values[cellIndex, 1] > 0)
					{
						float[,] values3 = new float[1, 1];
						hr.ReadRows(meshNameTerm + "/" + groupPrefix + " Values", values[cellIndex, 0], values[cellIndex, 1], ref values3);
						point2DCollection2 = new Point2DCollection(values3, xcol, ycol);
					}
					point2DCollections.Add(point2DCollection2);
				}
			}
			return point2DCollections;
		}
	}

	private static List<Point2DCollection> ReadTableV2(RASD2FlowArea d2FlowArea, string d2FlowNameGroup, string groupPrefix, int xcol, int ycol, int[] cellIndexes = null)
	{
		throw new NotImplementedException();
	}

	public float[] GetMeshComputationTimestepSeconds(int meshIdx)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		using (H5Reader h5Reader = new H5Reader(SourceFilename))
		{
			try
			{
				string featureName = GetFeatureName(meshIdx);
				float[] values = null;
				string timeStep = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.ComputeTimeStep(featureName);
				if (h5Reader.ExistsAsDataset(timeStep))
				{
					h5Reader.ReadDataset(timeStep, ref values);
					return values;
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
		return null;
	}

	public string CellMinimumDataset(int meshIdx)
	{
		return CellMinimumDataset(MakeMeshGroupName(meshIdx));
	}

	public static string CellMinimumDataset(string meshgroup)
	{
		return meshgroup + "/Cells Minimum Elevation";
	}

	public static string CellLandCoverDataset(string meshgroup)
	{
		return meshgroup + "/Cells Land Cover and Region";
	}

	public static string CellSoilNameDataset(string meshgroup)
	{
		return meshgroup + "/Cells Soil Name";
	}

	public static string CellSoilIndexDataset(string meshgroup)
	{
		return meshgroup + "/Cells Soil Index";
	}

	private PointFeatureLayer ExportMeshPoints()
	{
		string columnName = "Cell Index";
		string columnName2 = "Mesh Name";
		string columnName3 = "Min Elev";
		PointFeatureLayer pointFeatureLayer = new PointFeatureLayer("Mesh Points");
		pointFeatureLayer.AddAttributeColumn(columnName2, typeof(string));
		pointFeatureLayer.AddAttributeColumn(columnName, typeof(int));
		pointFeatureLayer.AddAttributeColumn(columnName3, typeof(float));
		checked
		{
			using (new WaitCursor(SharedData.RasMapper))
			{
				int count = FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					string featureName = GetFeatureName(i);
					MeshFV2D meshFV2D = Mesh(i);
					int count2 = meshFV2D.NonVirtualCellCount - 1;
					for (int j = 0; j <= count2; j++)
					{
						RasMapperLib.Mesh.Cell cell = meshFV2D.Cell(j);
						DataRow dataRow = pointFeatureLayer.AddFeature(new Point(cell.Point.X, cell.Point.Y));
						dataRow[columnName2] = featureName;
						dataRow[columnName] = j;
						dataRow[columnName3] = cell.MinimumElevation;
					}
				}
				return pointFeatureLayer;
			}
		}
	}

	private MultiPointFeatureLayer ExportMeshMultipoint()
	{
		string columnName = "Mesh Name";
		MultiPointFeatureLayer multiPointFeatureLayer = new MultiPointFeatureLayer("Mesh Points");
		multiPointFeatureLayer.AddAttributeColumn(columnName, typeof(string));
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string featureName = GetFeatureName(i);
				MeshFV2D meshFV2D = Mesh(i);
				PointMs pointMs = new PointMs();
				pointMs.Capacity = meshFV2D.NonVirtualCellCount;
				int count2 = meshFV2D.NonVirtualCellCount - 1;
				for (int j = 0; j <= count2; j++)
				{
					RasMapperLib.Mesh.Cell cell = meshFV2D.Cell(j);
					pointMs.Add(cell.Point.X, cell.Point.Y);
				}
				multiPointFeatureLayer.AddFeature(new MultiPoint(pointMs))[columnName] = featureName;
			}
			return multiPointFeatureLayer;
		}
	}

	private PolygonFeatureLayer ExportMeshPolygons()
	{
		string columnName = "Cell Index";
		string columnName2 = "Mesh Name";
		PolygonFeatureLayer polygonFeatureLayer = new PolygonFeatureLayer("Mesh Polygons");
		polygonFeatureLayer.AddAttributeColumn(columnName, typeof(int));
		polygonFeatureLayer.AddAttributeColumn(columnName2, typeof(string));
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string featureName = GetFeatureName(i);
				MeshFV2D meshFV2D = Mesh(i);
				int count2 = meshFV2D.NonVirtualCellCount - 1;
				for (int j = 0; j <= count2; j++)
				{
					meshFV2D.Cell(j);
					DataRow dataRow = polygonFeatureLayer.AddFeature(meshFV2D.CellPolygon(j));
					dataRow[columnName2] = featureName;
					dataRow[columnName] = j;
				}
			}
			return polygonFeatureLayer;
		}
	}

	public string GetFaceVelocityDatasetName(int fID)
	{
		return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + GetFeatureName(fID) + "/Face Velocity";
	}

	public List<int> GetOutOfDateMeshes()
	{
		List<int> outOfDateMeshes = new List<int>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!GetMeshUpToDate(i))
				{
					outOfDateMeshes.Add(i);
				}
			}
			return outOfDateMeshes;
		}
	}

	public void ComputeOutOfDateMeshes(bool showProgress = true)
	{
		List<int> outOfDateMeshes = GetOutOfDateMeshes();
		ComputeMeshesFromInputs(outOfDateMeshes, showProgress);
	}

	public void ComputeOutOfDateMesh(int idx, bool showProgress = true)
	{
		if (GetOutOfDateMeshes().Contains(idx))
		{
			ComputeMeshesFromInputs(new int[1] { idx }, showProgress);
		}
	}

	public void ComputeMeshesFromInputs(IEnumerable<int> meshIdxs = null, bool showProgress = true)
	{
		SharedData.ClearAllMeshCaches();
		if (meshIdxs == null)
		{
			meshIdxs = Enumerable.Range(0, FeatureCount());
		}
		if (meshIdxs.Count() == 0)
		{
			return;
		}
		PointMs pointMs = new PointMs();
		foreach (MultiPoint point in Geometry.MeshPoints.Points())
		{
			point.CopyTo(pointMs);
		}
		if (pointMs.Count <= 1)
		{
			return;
		}
		MeshPerimeterLayer meshPerimeters = Geometry.MeshPerimeters;
		checked
		{
			foreach (int meshIdx in meshIdxs)
			{
				SetMeshHasBeenRecomputed(meshIdx, meshHasBeenRecomputed: true);
				SetPartialMesh(meshIdx, null);
				PointMs pointMs2 = new PointMs();
				Polygon polygon = meshPerimeters.Polygon(meshIdx);
				if (!Polygon.IsValidPolygon(polygon))
				{
					SetFeature(meshIdx, null);
					continue;
				}
				DevelopmentTin developmentTin = meshPerimeters.GetDevelopmentTin(meshIdx);
				if (developmentTin == null)
				{
					SetFeature(meshIdx, null);
					continue;
				}
				int count = pointMs.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					PointM pointM = pointMs[i];
					if (developmentTin.ContainsPoint(pointM))
					{
						pointMs2.Add(pointM);
					}
				}
				Polyline enforceableBreaklines = GetEnforceableBreaklines();
				MeshFV2D meshFV2D = null;
				meshFV2D = new MeshFV2D(polygon, pointMs2, enforceableBreaklines);
				SetFeature(meshIdx, meshFV2D);
				SetMeshUpToDate(meshIdx, meshUpToDate: true);
			}
			SharedData.RASMapperReplot();
		}
	}

	private Polyline GetEnforceableBreaklines()
	{
		if (_mergedBline != null && Geometry.BreakLines.FeatureChangedCount() == _blineCounter && Geometry.MeshRegions.FeatureChangedCount() == _rgnCounter && Geometry.Structures.FeatureChangedCount() == _strucCounter)
		{
			return _mergedBline;
		}
		PolylineFeatureLayer polylineFeatureLayer = new PolylineFeatureLayer("bl");
		foreach (Polyline polyline in Geometry.BreakLines.Polylines())
		{
			if (Polyline.IsValidPolyline(polyline))
			{
				polylineFeatureLayer.AddFeature(polyline);
			}
		}
		foreach (Polygon polygon in Geometry.MeshRegions.Polygons())
		{
			if (Polyline.IsValidPolyline(polygon))
			{
				polylineFeatureLayer.AddFeature(polygon);
			}
		}
		foreach (Polyline polyline2 in Geometry.Structures.Polylines())
		{
			if (Polyline.IsValidPolyline(polyline2))
			{
				polylineFeatureLayer.AddFeature(polyline2);
			}
		}
		_blineCounter = Geometry.BreakLines.FeatureChangedCount();
		_rgnCounter = Geometry.MeshRegions.FeatureChangedCount();
		_strucCounter = Geometry.Structures.FeatureChangedCount();
		_mergedBline = polylineFeatureLayer.CopyToMultiPartPolyline();
		return _mergedBline;
	}

	private void RecomputePartialMeshes(Extent viewBox)
	{
		double scaledWidth = viewBox.Width * 0.5;
		double scaledHeight = viewBox.Height * 0.5;
		Extent extent = viewBox.Extend(scaledWidth * 0.5, scaledWidth * 0.5, scaledHeight * 0.5, scaledHeight * 0.5);
		MeshPerimeterLayer meshPerimeters = Geometry.MeshPerimeters;
		if (meshPerimeters == null)
		{
			return;
		}
		MeshPointLayer meshPoints = Geometry.MeshPoints;
		if (meshPoints == null)
		{
			return;
		}
		MultiPoint multiPoint = ((meshPoints.FeatureCount() != 0) ? meshPoints.Points(0) : new MultiPoint(new PointM[0]));
		Polyline enforceableBreaklines = GetEnforceableBreaklines();
		PointMs pointMs = null;
		Polygon clippedPgon = extent.ToPolygon();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polygon polygon = meshPerimeters.Polygon(i);
				if (polygon == null || !Polygon.IsValidPolygon(polygon))
				{
					SetPartialMesh(i, null);
				}
				else
				{
					if (!polygon.AreaIntersects(viewBox))
					{
						continue;
					}
					PartialMesh partialMesh = GetPartialMesh(i);
					if (partialMesh != null && partialMesh.ValidExtent.Contains(viewBox))
					{
						continue;
					}
					SetPartialMesh(i, null);
					Polygon polygon2 = GeoProcessing.IntersectPolygon(polygon, clippedPgon);
					if (polygon2 == null)
					{
						continue;
					}
					DevelopmentTin developmentTin = PolygonFeatureLayer.ComputeDevelopmentTIN(polygon2);
					if (developmentTin == null)
					{
						continue;
					}
					if (pointMs == null)
					{
						pointMs = new PointMs();
						int count2 = multiPoint.Count - 1;
						for (int j = 0; j <= count2; j++)
						{
							PointM pointM = multiPoint.PointM(j);
							if (extent.Contains(pointM))
							{
								pointMs.Add(pointM);
							}
						}
					}
					if (pointMs.Count > 10000)
					{
						continue;
					}
					PointMs pointMs2 = new PointMs();
					int count3 = pointMs.Count - 1;
					for (int k = 0; k <= count3; k++)
					{
						PointM pointM2 = pointMs[k];
						if (developmentTin.ContainsPoint(pointM2))
						{
							pointMs2.Add(pointM2);
						}
					}
					MeshFV2D mesh = new MeshFV2D(polygon2, pointMs2, enforceableBreaklines);
					SetPartialMesh(i, new PartialMesh(mesh, extent, DateTime.Now));
				}
			}
		}
	}

	public override void OnStartEditSession()
	{
		base.OnStartEditSession();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!Mesh(i).IsComplete())
				{
					SetMeshUpToDate(i, meshUpToDate: false);
				}
			}
		}
	}

	private void ProcessFaceFlowDifferences(int meshIdx)
	{
		MeshFV2D meshFV2D = Meshes().ElementAtOrDefault(meshIdx);
		checked
		{
			int count = meshFV2D.FaceCount - 1;
			for (int i = 0; i <= count; i++)
			{
				ProcessFaceFlowDifferences(meshIdx, meshFV2D, i);
			}
		}
	}

	public void ProcessFaceFlowDifferences(int meshIdx, int fIdx)
	{
		ProcessFaceFlowDifferences(meshIdx, Mesh(meshIdx), fIdx);
	}

	private void ProcessFaceFlowDifferences(int meshIdx, MeshFV2D mesh, int fIdx, float pctErrThreshold = 0.01f)
	{
		if (mesh.FaceIsPerimeter(fIdx))
		{
			return;
		}
		float[] summedFaceFlowTimeSeries_ComputeEngine = GetSummedFaceFlowTimeSeries_ComputeEngine(meshIdx, fIdx);
		float[] summedFaceFlowTimeSeries_PostProcessed = GetSummedFaceFlowTimeSeries_PostProcessed(meshIdx, fIdx);
		float computed = 0f;
		float summedFaceFlowTimeSeries_ComputeEngine2 = 0f;
		float systemTerm = 0f;
		float computed2 = 0f;
		checked
		{
			int count = summedFaceFlowTimeSeries_ComputeEngine.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				float summedFaceFlowTimeSeries_ComputeEngine3 = summedFaceFlowTimeSeries_ComputeEngine[i];
				float summedFaceFlowTimeSeries_PostProcessed2 = summedFaceFlowTimeSeries_PostProcessed[i];
				if ((summedFaceFlowTimeSeries_ComputeEngine3 != 0f || summedFaceFlowTimeSeries_PostProcessed2 == 0f) && (summedFaceFlowTimeSeries_ComputeEngine3 == 0f || summedFaceFlowTimeSeries_PostProcessed2 != 0f) && (summedFaceFlowTimeSeries_ComputeEngine3 != 0f || summedFaceFlowTimeSeries_PostProcessed2 != 0f))
				{
					float systemTerm2 = System.Math.Abs(summedFaceFlowTimeSeries_ComputeEngine3 - summedFaceFlowTimeSeries_PostProcessed2);
					systemTerm = System.Math.Max(systemTerm, systemTerm2);
					computed2 += systemTerm2;
					float systemTerm3 = System.Math.Abs(systemTerm2 / summedFaceFlowTimeSeries_ComputeEngine3);
					if (systemTerm3 > computed)
					{
						computed = systemTerm3;
						summedFaceFlowTimeSeries_ComputeEngine2 = summedFaceFlowTimeSeries_ComputeEngine3;
					}
				}
			}
			if (computed > pctErrThreshold)
			{
				_ = fIdx + ": " + System.Math.Round(computed * 100f, 2) + "% error @ " + summedFaceFlowTimeSeries_ComputeEngine2 + " " + SharedData.FlowUnits + ". (MaxABSErr=" + systemTerm + ", Sum=" + computed2 + ")";
			}
		}
	}

	public float[] GetSummedFaceFlowTimeSeries_ComputeEngine(int meshIdx, int faceIdx)
	{
		List<int> uniqueFaces = new int[1] { faceIdx }.ToList();
		List<bool> shouldNegateFaceFlows = new bool[1].ToList();
		float[] volume_aggregation = null;
		return GetSummedFaceFlowTimeSeries_ComputeEngine(meshIdx, uniqueFaces, shouldNegateFaceFlows, ref volume_aggregation);
	}

	public float[] GetSummedFaceFlowTimeSeries_ComputeEngine(int meshIdx, List<int> uniqueFaces, List<bool> shouldNegateFaceFlows, ref float[] volume_aggregation)
	{
		Meshes().ElementAtOrDefault(meshIdx);
		RASResults results = Geometry.Results;
		checked
		{
			float[] floatValues = new float[results.ProfileCount - 1 + 1];
			volume_aggregation = new float[results.ProfileCount - 1 + 1];
			float[,] values = null;
			using (H5Reader h5Reader = new H5Reader(results.SourceFilename))
			{
				string faceFlow = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FaceFlow(GetFeatureName(meshIdx));
				if (!h5Reader.ExistsAsDataset(faceFlow))
				{
					return null;
				}
				h5Reader.ReadDataset(faceFlow, ref values);
			}
			Dictionary<int, float> floatByInt = new Dictionary<int, float>();
			float mappingDTSeconds = results.MappingDTSeconds;
			int count = results.ProfileCount - 1;
			for (int i = 0; i <= count; i++)
			{
				if (i > 0)
				{
					volume_aggregation[i] = volume_aggregation[i - 1];
				}
				int count2 = uniqueFaces.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					int uniqueFaceIndex = uniqueFaces[j];
					bool shouldNegateFaceFlow = shouldNegateFaceFlows[j];
					float uniqueFaceIndexTerm = values[i, uniqueFaceIndex];
					if (shouldNegateFaceFlow)
					{
						uniqueFaceIndexTerm *= -1f;
					}
					floatValues[i] += uniqueFaceIndexTerm;
					if (i > 0)
					{
						float uniqueFaceIndexTerm2 = floatByInt[uniqueFaceIndex];
						float computed;
						float computed2;
						if (uniqueFaceIndexTerm2 > uniqueFaceIndexTerm)
						{
							computed = uniqueFaceIndexTerm;
							computed2 = uniqueFaceIndexTerm2;
						}
						else
						{
							computed = uniqueFaceIndexTerm2;
							computed2 = uniqueFaceIndexTerm;
						}
						volume_aggregation[i] += (float)((double)(computed * mappingDTSeconds) + 0.5 * (double)mappingDTSeconds * (double)(computed2 - computed));
					}
					floatByInt[uniqueFaceIndex] = uniqueFaceIndexTerm;
				}
			}
			return floatValues;
		}
	}

	public float[] GetSummedFaceFlowTimeSeries_PostProcessed(int meshIdx, int fidx)
	{
		List<int> uniqueFaces = new int[1] { fidx }.ToList();
		List<bool> shouldNegateFaceFlows = new bool[1].ToList();
		float[] volume_aggregation = null;
		float[] faceWSAvg = null;
		return GetSummedFaceFlowTimeSeries_PostProcessed(meshIdx, uniqueFaces, shouldNegateFaceFlows, ref volume_aggregation, ref faceWSAvg);
	}

	public float[] GetSummedFaceFlowTimeSeries_PostProcessed(int meshIdx, List<int> uniqueFaces, List<bool> shouldNegateFaceFlows, [Optional][DefaultParameterValue(null)] ref float[] volume_aggregation, [Optional][DefaultParameterValue(null)] ref float[] faceWSAvg)
	{
		float minWSPlotTolerance = RASResults.MinWSPlotTolerance;
		RASResults.MinWSPlotTolerance = 0f;
		SharedData.FaceWSMethod faceWSMode = SharedData.FaceWSMode;
		SharedData.FaceWSMode = SharedData.FaceWSMethod.Ben;
		MeshFV2D meshFV2D = Meshes().ElementAtOrDefault(meshIdx);
		RASResults results = Geometry.Results;
		checked
		{
			float[] summedFaceFlowTimeSeries_PostProcessed = new float[results.ProfileCount - 1 + 1];
			volume_aggregation = new float[results.ProfileCount - 1 + 1];
			faceWSAvg = new float[results.ProfileCount - 1 + 1];
			float[] floatValues = new float[results.ProfileCount - 1 + 1];
			int[] intValues = new int[results.ProfileCount - 1 + 1];
			List<Point2DCollection> faceAreaTables = ReadFaceAreaTables(meshIdx);
			foreach (Point2DCollection faceAreaTable in faceAreaTables)
			{
				faceAreaTable.InvertXY();
			}
			Dictionary<int, RasMapperLib.Mesh.FaceValues[]> faceWSELTimeSeries = GetFaceWSELTimeSeries(meshIdx, uniqueFaces);
			Dictionary<int, float[]> faceVelocityTimeSeries = GetFaceVelocityTimeSeries(meshIdx, uniqueFaces);
			float mappingDTSeconds = results.MappingDTSeconds;
			Dictionary<int, float> floatByInt = new Dictionary<int, float>();
			int count = results.ProfileCount - 1;
			bool beforeStart = default;
			bool exceededFinal = default;
			for (int i = 0; i <= count; i++)
			{
				if (i > 0)
				{
					volume_aggregation[i] = volume_aggregation[i - 1];
				}
				int count2 = uniqueFaces.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					int uniqueFaceIndex = uniqueFaces[j];
					bool shouldNegateFaceFlow = shouldNegateFaceFlows[j];
					RasMapperLib.Mesh.FaceValues faceValues = faceWSELTimeSeries[uniqueFaceIndex][i];
					bool faceIsPerimeter = meshFV2D.FaceIsPerimeter(uniqueFaceIndex);
					float queryFaceVelocityTimeSeriesTerm = 0f;
					if (faceValues.HydraulicConnection == RasMapperLib.Mesh.HydraulicConnection.None && !faceIsPerimeter)
					{
						queryFaceVelocityTimeSeriesTerm = 0f;
					}
					else
					{
						float valueBFaceValuesTerm;
						if (faceValues.ValueA == -9999f)
						{
							valueBFaceValuesTerm = faceValues.ValueB;
						}
						else
						{
							valueBFaceValuesTerm = ((faceValues.ValueB != -9999f) ? System.Math.Max(faceValues.ValueA, faceValues.ValueB) : faceValues.ValueA);
						}
						if (valueBFaceValuesTerm != -9999f)
						{
							float query = faceAreaTables[uniqueFaceIndex].Query(valueBFaceValuesTerm, ref beforeStart, ref exceededFinal);
							if (exceededFinal)
							{
								float x = faceAreaTables[uniqueFaceIndex].Last().X;
								float computed = valueBFaceValuesTerm - x;
								float facePolylineLength = (float)meshFV2D.FacePolylineLength(uniqueFaceIndex);
								query += computed * facePolylineLength;
							}
							floatValues[i] += valueBFaceValuesTerm;
							intValues[i]++;
							queryFaceVelocityTimeSeriesTerm = query * faceVelocityTimeSeries[uniqueFaceIndex][i];
							if (shouldNegateFaceFlow)
							{
								queryFaceVelocityTimeSeriesTerm *= -1f;
							}
						}
						summedFaceFlowTimeSeries_PostProcessed[i] += queryFaceVelocityTimeSeriesTerm;
						if (i > 0)
						{
							float uniqueFaceIndexTerm = floatByInt[uniqueFaceIndex];
							float computed2;
							float computed3;
							if (uniqueFaceIndexTerm > queryFaceVelocityTimeSeriesTerm)
							{
								computed2 = queryFaceVelocityTimeSeriesTerm;
								computed3 = uniqueFaceIndexTerm;
							}
							else
							{
								computed2 = uniqueFaceIndexTerm;
								computed3 = queryFaceVelocityTimeSeriesTerm;
							}
							volume_aggregation[i] += (float)((double)(computed2 * mappingDTSeconds) + 0.5 * (double)mappingDTSeconds * (double)(computed3 - computed2));
						}
					}
					floatByInt[uniqueFaceIndex] = queryFaceVelocityTimeSeriesTerm;
				}
			}
			int count3 = results.ProfileCount - 1;
			for (int k = 0; k <= count3; k++)
			{
				int ordinal = intValues[k];
				float computed4 = floatValues[k];
				if (ordinal == 0)
				{
					faceWSAvg[k] = -9999f;
				}
				else
				{
					faceWSAvg[k] = computed4 / (float)ordinal;
				}
			}
			SharedData.FaceWSMode = faceWSMode;
			RASResults.MinWSPlotTolerance = minWSPlotTolerance;
			return summedFaceFlowTimeSeries_PostProcessed;
		}
	}

	private Dictionary<int, RasMapperLib.Mesh.FaceValues[]> GetFaceWSELTimeSeries(int meshIdx, List<int> uniqueFaces)
	{
		MeshFV2D meshFV2D = Meshes().ElementAtOrDefault(meshIdx);
		RASResults results = Geometry.Results;
		List<int> cells = meshFV2D.UniqueCellsAroundFaces(uniqueFaces);
		Dictionary<int, RasMapperLib.Mesh.FaceValues[]> faceWSELTimeSeries = new Dictionary<int, RasMapperLib.Mesh.FaceValues[]>();
		Dictionary<int, float[]> read2DCellTimeseries = new WaterSurfaceTimeSeries(results).Read2DCellTimeseries(meshIdx, cells);
		Dictionary<int, float> floatByInt = new Dictionary<int, float>();
		checked
		{
			foreach (int uniqueFace in uniqueFaces)
			{
				RasMapperLib.Mesh.FaceValues[] faceValues = new RasMapperLib.Mesh.FaceValues[results.ProfileCount - 1 + 1];
				faceWSELTimeSeries[uniqueFace] = faceValues;
			}
			int count = results.ProfileCount - 1;
			for (int i = 0; i <= count; i++)
			{
				foreach (KeyValuePair<int, float[]> read2DCellTimesery in read2DCellTimeseries)
				{
					floatByInt[read2DCellTimesery.Key] = read2DCellTimesery.Value[i];
				}
				List<RasMapperLib.Mesh.FaceValues> slopingWSFaceFactors = SlopingFactors.ComputeSlopingWSFaceFactors(meshFV2D, floatByInt, uniqueFaces);
				int count2 = uniqueFaces.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					int key = uniqueFaces[j];
					faceWSELTimeSeries[key][i] = slopingWSFaceFactors[j];
				}
			}
			return faceWSELTimeSeries;
		}
	}

	private Dictionary<int, float[]> GetFaceVelocityTimeSeries(int meshIdx, List<int> uniqueFaces)
	{
		Meshes().ElementAtOrDefault(meshIdx);
		RASResults results = Geometry.Results;
		return new VelocityTimeSeries(results).Read2DFaceTimeseries(meshIdx, uniqueFaces);
	}

	public Func<int, Point2Double> GetFaceLocationFunc(int meshIdx)
	{
		MeshFV2D meshFV2D = Meshes().ElementAtOrDefault(meshIdx);
		string featureName = GetFeatureName(meshIdx);
		if (RASResults.UseFaceCentroidAdjustment && meshFV2D.EnsureFaceCentroidCached(this, featureName))
		{
			return [SpecialName] (int fidx) => new Point2Double(meshFV2D.FaceCentroidPoint(fidx));
		}
		meshFV2D.EnsureFaceMidsideCached();
		return [SpecialName] (int fIdx) => meshFV2D.GetFaceMidSide(fIdx).Point;
	}

	public float GetFilterTolCellVol(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Cell Volume Tolerance");
	}

	public void SetFilterTolCellVol(int fid, float filterTolCellVol)
	{
		FeatureRow(fid)["Cell Volume Tolerance"] = filterTolCellVol;
	}

	public float GetFilterCellMinAreaFrac(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Cell Minimum Area Fraction");
	}

	public void SetFilterCellMinAreaFrac(int fid, float filterCellMinAreaFrac)
	{
		FeatureRow(fid)["Cell Minimum Area Fraction"] = filterCellMinAreaFrac;
	}

	public float GetFilterTolFaceProfile(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Face Profile Tolerance");
	}

	public void SetFilterTolFaceProfile(int fid, float filterTolFaceProfile)
	{
		FeatureRow(fid)["Face Profile Tolerance"] = filterTolFaceProfile;
	}

	public float GetFilterTolFaceElevArea(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Face Area Elevation Tolerance");
	}

	public void SetFilterTolFaceElevArea(int fid, float filterTolFaceElevArea)
	{
		FeatureRow(fid)["Face Area Elevation Tolerance"] = filterTolFaceElevArea;
	}

	public float GetFaceAreaConveyanceRatioTol(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Face Area Conveyance Ratio");
	}

	public void SetFaceAreaConveyanceRatioTol(int fid, float faceAreaConveyanceRatioTol)
	{
		FeatureRow(fid)["Face Area Conveyance Ratio"] = faceAreaConveyanceRatioTol;
	}

	public float GetFaceLaminarDepth(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Laminar Depth");
	}

	public void SetFaceLaminarDepth(int fid, float faceLaminarDepth)
	{
		FeatureRow(fid)["Laminar Depth"] = faceLaminarDepth;
	}

	public float GetManningsN(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Manning's n");
	}

	public void SetManningsN(int fid, float manningsN)
	{
		FeatureRow(fid)["Manning's n"] = manningsN;
	}

	public void NotifyMeshInputsChanged()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				NotifyMeshInputsChanged(i);
			}
		}
	}

	public void NotifyMeshInputsChanged(int fid)
	{
		if (IsEditingData)
		{
			GetMeshUpToDate(fid);
			SetMeshUpToDate(fid, meshUpToDate: false);
			SetPartialMesh(fid, null);
		}
	}

	public void NotifyMeshInputsChanged(Polygon pgon)
	{
		NotifyMeshInputsChanged((Polyline)pgon);
	}

	public void NotifyMeshInputsChanged(Polyline pline)
	{
		if (!IsEditingData)
		{
			_ = SharedData.IsTestingUtility;
			return;
		}
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!GetMeshUpToDate(i))
				{
					NotifyMeshInputsChanged(i);
					continue;
				}
				Polygon perimeter = Mesh(i).Perimeter;
				if (Polygon.IsValidPolygon(perimeter) && Polyline.IsValidPolyline(pline) && perimeter.ContainsOrIntersects(pline))
				{
					NotifyMeshInputsChanged(i);
				}
			}
		}
	}

	public void NotifyMeshInputsChanged(PointM pt)
	{
		if (!IsEditingData)
		{
			_ = SharedData.IsTestingUtility;
			return;
		}
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!GetMeshUpToDate(i))
				{
					NotifyMeshInputsChanged(i);
					continue;
				}
				Polygon perimeter = Mesh(i).Perimeter;
				if (Polygon.IsValidPolygon(perimeter) && perimeter.Contains(pt))
				{
					NotifyMeshInputsChanged(i);
				}
			}
		}
	}

	public void NotifyMeshInputsChanged(IEnumerable<PointM> pts)
	{
		if (!IsEditingData)
		{
			_ = SharedData.IsTestingUtility;
			return;
		}
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!GetMeshUpToDate(i))
				{
					NotifyMeshInputsChanged(i);
					continue;
				}
				Mesh(i);
				if (!Polygon.IsValidPolygon(Geometry.MeshPerimeters.Polygon(i)))
				{
					continue;
				}
				DevelopmentTin developmentTin = Geometry.MeshPerimeters.GetDevelopmentTin(i);
				foreach (PointM pt in pts)
				{
					if (developmentTin.ContainsPoint(pt))
					{
						NotifyMeshInputsChanged(i);
						break;
					}
				}
			}
		}
	}

	public PartialMesh GetPartialMesh(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Partial Mesh"]);
		if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)) || objectValue == null)
		{
			return null;
		}
		return objectValue as PartialMesh;
	}

	public void SetPartialMesh(int fid, PartialMesh partialMesh)
	{
		FeatureRow(fid)["Partial Mesh"] = partialMesh;
	}

	public bool GetMeshUpToDate(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Current Computed Mesh Up to Date");
	}

	public void SetMeshUpToDate(int fid, bool meshUpToDate)
	{
		FeatureRow(fid)["Current Computed Mesh Up to Date"] = meshUpToDate;
	}

	public bool GetMeshHasBeenRecomputed(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Mesh Recomputed");
	}

	public void SetMeshHasBeenRecomputed(int fid, bool meshHasBeenRecomputed)
	{
		FeatureRow(fid)["Mesh Recomputed"] = meshHasBeenRecomputed;
	}
}
