using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.Rasters.Classifications;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Names;
using Utility.Extensions;
using Utility.Progress;
using Utility.Systems;

namespace RasMapperLib;

public class RASStorageArea : PolygonFeatureLayer, IGeometryLayer
{
	public const string HDFGroupName = "Geometry/Storage Areas";

	public const string sAverageArea = "Average Area";

	public const string sVolumeElevation = "Volume Elevation";

	public const string sMode = "Mode";

	public const string sMinElev = "Minimum Elevation";

	public const string sVolElevCount = "Elev Vol Count";

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override string DefaultLayerName => "Storage Areas";
string IGeometryLayer.HDFGroupName=> "Geometry/Storage Areas";

	public override bool FeaturesRequireUniqueNames { get; set; }

	public RASStorageArea(RASGeometry geometry)
		: base("Storage Areas", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "Storage Area";
		FeaturesRequireUniqueNames = true;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		DataColumnGroup dataColumnGroup = new DataColumnGroup("Compute Information", isVisible: true);
		AddManagedAttributeColumn("Mode", "Mode", typeof(string), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Average Area", "Avg Area", typeof(float), dataColumnGroup);
		AddManagedAttributeColumn("Minimum Elevation", "Min Elev", typeof(float), dataColumnGroup);
		AddAttributeColumn("Volume Elevation", typeof(IVolumeElevationInfo), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedWriteOnlyAttributeColumn("Elev Vol Count", [SpecialName] (int fid) => GetVolumeElevationCurve(fid)?.Count ?? 0);
		AddAttributeColumn("Track Terrain", typeof(bool));
		AddAttributeColumn("Auto Update", typeof(bool), null, DataColumnViewState.Invisible);
		AddAttributeColumn("IsDefaultName", typeof(bool), null, DataColumnViewState.Invisible);
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		checked
		{
			if (hr.ExistsAsGroup("Geometry/Storage Areas"))
			{
				HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/Storage Areas/Polygon");
				EnsurePositivePolygonOrientations();
				string path = "Geometry/Storage Areas/Attributes";
				if (hr.ExistsAsDataset(path))
				{
					ReadManagedColumns(hr, path);
				}
				else
				{
					HDFLoadFeatureTable_503(hr);
				}
				List<List<Point2D>> pointCollection = HDF5Storage.ReadPointCollection(hr, "Geometry/Storage Areas/Volume Elevation");
				int count = pointCollection.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					SetVolumeElevationCurve(i, pointCollection[i]);
				}
				int count2 = FeatureCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					SetTrackTerrain(j, condition: false);
				}
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	private void HDFLoadFeatureTable_503(H5Reader hr)
	{
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/Storage Areas/Names", "Name");
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/Storage Areas");
			return;
		}
		hw.CreateGroup("Geometry/Storage Areas");
		HDF5Storage.WritePolygonFeatures(this, hw, "Geometry/Storage Areas/Polygon");
		WriteManagedColumns(hw, "Geometry/Storage Areas/Attributes");
		HDF5Storage.WritePointCollection(hw, "Geometry/Storage Areas/Volume Elevation", VolumeElevationCurves(), "Volume", "Elevation");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public void Stream(BinaryWriter bw)
	{
		bw.Write(FeatureCount());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polygon(i).Stream(bw);
				Pipe.WriteString(bw, GetFeatureName(i));
			}
		}
	}

	public static RASStorageArea Stream(BinaryReader br, RASGeometry geom)
	{
		RASStorageArea rASStorageArea = new RASStorageArea(geom);
		rASStorageArea.InitializeEmptyFeatureTable();
		rASStorageArea.SuppressTableReload(boolValue: true);
		rASStorageArea.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polygon poly = RasMapperLib.Polygon.Stream(br);
				rASStorageArea.AddFeature(poly);
				rASStorageArea.SetFeatureName(i, Pipe.ReadString(br));
			}
			return rASStorageArea;
		}
	}

	public bool CompleteForComputations(ProgressReporter prog)
	{
		CleanAssociationPropertyTables();
		if (FeatureCount() == 0)
		{
			return true;
		}
		if (!ProcessAssociationForComputations(Geometry.InfiltrationAssociation, Geometry.InfiltrationResampler, prog))
		{
			return false;
		}
		if (!ProcessAssociationForComputations(Geometry.PercentImperviousAssociation, Geometry.PercentImperviousResampler, prog))
		{
			return false;
		}
		using (H5Writer h5Writer = new H5Writer(SourceFilename))
		{
			h5Writer.WriteArray("Geometry/Storage Areas/Polygon Area", (from pg in Polygons()
				select (!RasMapperLib.Polygon.IsValidPolygon(pg)) ? 0.0 : pg.Area).ToArray());
		}
		return true;
	}

	private void CleanAssociationPropertyTables()
	{
		using H5Writer hw = new H5Writer(SourceFilename);
		CleanAssociationPropertyTables(hw);
	}

	private void CleanAssociationPropertyTables(H5Writer hw)
	{
		if (!hw.ExistsAsGroup("Geometry/Storage Areas"))
		{
			return;
		}
		foreach (string fullyQualifiedGroupName in hw.GetFullyQualifiedGroupNames("Geometry/Storage Areas"))
		{
			hw.Delete(fullyQualifiedGroupName);
		}
	}

	private bool ProcessAssociationForComputations(GeometryAssociation association, ParameterResampler resampler, ProgressReporter reporter)
	{
		using H5Writer hw = new H5Writer(SourceFilename);
		return RASGeometry.ProcessPolygonAssociations(hw, "Geometry/Storage Areas", Polygons().ToList(), association, resampler, [SpecialName] (int idx) => "Fatal resampling error for Storage Area " + GetFeatureName(idx) + " " + association.AssociationName.InParentheses(), reporter);
	}

	public List<TimeSeriesVariable> ReadSA(int fid)
	{
		RASResults results = Geometry.Results;
		if (results == null)
		{
			return null;
		}
		List<TimeSeriesVariable> timeSeriesVariables = new List<TimeSeriesVariable>();
		string storageAreaVariables = ResultsDatasets.Unsteady.TimeSeriesOutput.StorageAreas.StorageAreaVariables(GetFeatureName(fid));
		string[,] values = null;
		using (H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename))
		{
			if (h5Reader.ExistsAsDataset(storageAreaVariables) && h5Reader.ExistsAsAttribute(storageAreaVariables, "Variable_Unit"))
			{
				h5Reader.ReadAttribute(storageAreaVariables, "Variable_Unit", ref values);
			}
		}
		string units = "";
		string units2 = "";
		checked
		{
			int count = values.GetLength(0) - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Operators.CompareString(values[i, 0], "Stage", TextCompare: false) == 0)
				{
					units = values[i, 1];
				}
				if (Operators.CompareString(values[i, 0], "Net Inflow", TextCompare: false) == 0)
				{
					units2 = values[i, 1];
				}
			}
			float[] values2 = null;
			results.ReadUnsteadyTimeSeries("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Water Surface", fid, ref values2);
			timeSeriesVariables.Add(new TimeSeriesVariable("Stage", units, values2));
			float[] values3 = null;
			results.ReadUnsteadyTimeSeries("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Flow", fid, ref values3);
			timeSeriesVariables.Add(new TimeSeriesVariable("Net Inflow", units2, values3));
			return timeSeriesVariables;
		}
	}

	public List<TimeSeriesVariable> ReadSA_DSSHydrograph(int fid)
	{
		RASResults results = Geometry.Results;
		if (results == null)
		{
			return null;
		}
		List<TimeSeriesVariable> timeSeriesVariables = new List<TimeSeriesVariable>();
		string replace = ResultsDatasets.Unsteady.TimeSeriesOutput.StorageAreas.StorageAreaVariables(GetFeatureName(fid)).Replace("Base Output", "DSS Hydrograph Output");
		string[,] values = null;
		using (H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename))
		{
			if (h5Reader.ExistsAsDataset(replace) && h5Reader.ExistsAsAttribute(replace, "Variable_Unit"))
			{
				h5Reader.ReadAttribute(replace, "Variable_Unit", ref values);
			}
		}
		string units = "";
		string units2 = "";
		checked
		{
			int count = values.GetLength(0) - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Operators.CompareString(values[i, 0], "Stage", TextCompare: false) == 0)
				{
					units = values[i, 1];
				}
				if (Operators.CompareString(values[i, 0], "Net Inflow", TextCompare: false) == 0)
				{
					units2 = values[i, 1];
				}
			}
			float[] values2 = null;
			string datasetName = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Water Surface".Replace("Base Output", "DSS Hydrograph Output");
			results.ReadUnsteadyTimeSeries(datasetName, fid, ref values2);
			timeSeriesVariables.Add(new TimeSeriesVariable("Stage", units, values2));
			float[] values3 = null;
			string datasetName2 = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Flow".Replace("Base Output", "DSS Hydrograph Output");
			results.ReadUnsteadyTimeSeries(datasetName2, fid, ref values3);
			timeSeriesVariables.Add(new TimeSeriesVariable("Net Inflow", units2, values3));
			return timeSeriesVariables;
		}
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			if (args.ChangeType == FeatureChangedType.FeatureAdded)
			{
				SetupNewStorageArea(featureIndicesChanged);
			}
			else
			{
				_ = args.ChangeType;
			}
		}
	}

	private void SetupNewStorageArea(int saID)
	{
		SetUniqueFeatureName(saID);
		SetIsDefaultName(saID, isDefaultName: true);
		SetVolumeElevationCurveTrackTerrain(saID);
		SetAutoUpdate(saID, condition: true);
	}

	public bool GetIsDefaultName(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "IsDefaultName");
	}

	public void SetIsDefaultName(int fid, bool isDefaultName)
	{
		FeatureRow(fid)["IsDefaultName"] = isDefaultName;
	}

	public override void SetFeatureName(int fid, string newName)
	{
		FeatureRow(fid)["Name"] = newName;
		SetIsDefaultName(fid, isDefaultName: false);
	}

	public void SetVolumeElevationCurve(int saId, IList<Point2D> volElevCurve)
	{
		FeatureRow(saId)["Volume Elevation"] = new InMemoryVolumeElevationInfo(volElevCurve);
	}

	public void SetVolumeElevationCurveTrackTerrain(int saId)
	{
		SetVolumeElevationCurve(saId, new TerrainQueryVolumeElevationInfo(Polygon(saId), Geometry.Terrain));
		SetTrackTerrain(saId, condition: true);
	}

	public void SetVolumeElevationCurve(int saId, IVolumeElevationInfo curveInfo)
	{
		FeatureRow(saId)["Volume Elevation"] = curveInfo;
	}

	public Point2DCollection GetVolumeElevationCurve(int saId)
	{
		if (FeatureRow(saId)["Volume Elevation"] is IVolumeElevationInfo volumeElevationInfo)
		{
			return volumeElevationInfo.GetTable();
		}
		return null;
	}

	public bool IsVolumeElevationCurveFromTerrain(int saId)
	{
		if (!(FeatureRow(saId)["Volume Elevation"] is IVolumeElevationInfo volumeElevationInfo))
		{
			return false;
		}
		if (!(volumeElevationInfo is TerrainQueryVolumeElevationInfo))
		{
			return false;
		}
		return true;
	}

	public IEnumerable<Point2DCollection> VolumeElevationCurves()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return GetVolumeElevationCurve(i);
			}
		}
	}

	private void SetTrackTerrain(int fid, bool condition)
	{
		FeatureRow(fid)["Track Terrain"] = condition;
	}

	private bool GetTrackTerrain(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Track Terrain");
	}

	private void SetAutoUpdate(int fid, bool condition)
	{
		FeatureRow(fid)["Auto Update"] = condition;
	}

	public bool GetAutoUpdate(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Auto Update");
	}

	public float MinElevation(int saID)
	{
		Point2DCollection volumeElevationCurve = GetVolumeElevationCurve(saID);
		if (volumeElevationCurve == null || volumeElevationCurve.Count == 0)
		{
			return float.MinValue;
		}
		return volumeElevationCurve.First().Y;
	}
}
