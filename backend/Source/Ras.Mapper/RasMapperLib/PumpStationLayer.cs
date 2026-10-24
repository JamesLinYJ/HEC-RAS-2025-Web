using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using RasMapperLib.Names;
using RasMapperLib.Structures;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class PumpStationLayer : PointFeatureLayer, IGeometryLayer, IStructureLayer
{
	public class Cols
	{
		public const string FromRiver = "Inlet River";

		public const string FromReach = "Inlet Reach";

		public const string FromRS = "Inlet RS";

		public const string FromRSDist = "Inlet RS Dist.";

		public const string FromSA2D = "Inlet SA/2D";

		public const string ToRiver = "Outlet River";

		public const string ToReach = "Outlet Reach";

		public const string ToRS = "Outlet RS";

		public const string ToRSDist = "Outlet RS Dist.";

		public const string ToSA2D = "Outlet SA/2D";

		public const string RefRiver = "Ref. River";

		public const string RefReach = "Ref. Reach";

		public const string RefRS = "Ref. RS";

		public const string RefRSDist = "Ref. RS Dist.";

		public const string RefSA2D = "Ref. SA/2D";

		public const string RefPoint = "Ref. Point";

		public const string HighestPumpLineEl = "Highest Pump Line Elevation";
	}

	public const string HDFGroupName = "Geometry/Pump Stations";

	public const string HDFFeatureDatasetName = "Geometry/Pump Stations/Points";

	public const string sPumpStations = "Pump Stations";

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Pump Stations";
string IGeometryLayer.HDFGroupName=> "Geometry/Pump Stations";

	public override bool FeaturesRequireUniqueNames { get; set; }

	public PumpStationLayer(RASGeometry geometry)
		: base("Pump Stations", geometry.SourceFilename)
	{
		SimpleFeatureTypeName = "Pump Station";
		ForwardEditRequestsToParent = true;
		FeaturesRequireUniqueNames = true;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		DataColumnGroup dataColumnGroup = new DataColumnGroup("Connection Info", isVisible: true);
		DataColumnViewState viewState = DataColumnViewState.GrayedOut;
		AddManagedAttributeColumn("Inlet River", "Inlet River", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Inlet Reach", "Inlet Reach", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Inlet RS", "Inlet RS", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Inlet RS Dist.", "Inlet RS Distance", typeof(float), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Inlet SA/2D", "Inlet SA/2D", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Outlet River", "Outlet River", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Outlet Reach", "Outlet Reach", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Outlet RS", "Outlet RS", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Outlet RS Dist.", "Outlet RS Distance", typeof(float), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Outlet SA/2D", "Outlet SA/2D", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Ref. River", "Reference River", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Ref. Reach", "Reference Reach", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Ref. RS", "Reference RS", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Ref. RS Dist.", "Reference RS Distance", typeof(float), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Ref. SA/2D", "Reference SA/2D", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Ref. Point", "Reference Point", typeof(string), dataColumnGroup, viewState);
		AddManagedAttributeColumn("Highest Pump Line Elevation", "Highest Pump Line Elevation", typeof(float), dataColumnGroup, viewState);
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Geometry/Pump Stations"))
		{
			HDF5Storage.ReadPointFeatures(this, hr, "Geometry/Pump Stations/Points");
			string path = "Geometry/Pump Stations/Attributes";
			if (hr.ExistsAsDataset(path))
			{
				ReadManagedColumns(hr, path);
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/Pump Stations");
			return;
		}
		hw.CreateGroup("Geometry/Pump Stations");
		HDF5Storage.WritePointFeatures(this, hw, "Geometry/Pump Stations/Points");
		WriteManagedColumns(hw, "Geometry/Pump Stations/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		List<Tuple<int, int>> oldNewIndexMap = args.GetOldNewIndexMap(FeatureCount());
		foreach (Tuple<int, int> oldNewIndexMap2 in oldNewIndexMap)
		{
			Geometry.PumpGroups.UpdateAssociations(oldNewIndexMap2.Item1, oldNewIndexMap2.Item2);
		}
	}

	public List<TimeSeriesVariable> ReadPS_DSSHydrograph(int fid)
	{
		if (Geometry.Results == null)
		{
			return null;
		}
		new List<TimeSeriesVariable>();
		string dset = ResultsDatasets.Unsteady.TimeSeriesOutput.PumpStations.StructureVariables(GetFeatureName(fid)).Replace("Base Output", "DSS Hydrograph Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadPS_DSSProfile(int fid)
	{
		if (Geometry.Results == null)
		{
			return null;
		}
		new List<TimeSeriesVariable>();
		string dset = ResultsDatasets.Unsteady.TimeSeriesOutput.PumpStations.StructureVariables(GetFeatureName(fid)).Replace("Base Output", "DSS Profile Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public float ReadPSHighElev_DSSHydrograph(int fid)
	{
		float naN = float.NaN;
		if (Geometry.Results == null)
		{
			return 0f;
		}
		string replace = ResultsDatasets.Unsteady.TimeSeriesOutput.PumpStations.GroupName(GetFeatureName(fid)).Replace("Base Output", "DSS Hydrograph Output");
		using (H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename))
		{
			if (h5Reader.ExistsAsGroup(replace) && h5Reader.ExistsAsAttribute(replace, "Highest Elev Pump Line"))
			{
				h5Reader.ReadAttributeScalar(replace, "Highest Elev Pump Line", ref naN);
			}
		}
		return naN;
	}

	public float ReadPSHighElev_DSSProfile(int fid)
	{
		float naN = float.NaN;
		if (Geometry.Results == null)
		{
			return 0f;
		}
		string replace = ResultsDatasets.Unsteady.TimeSeriesOutput.PumpStations.GroupName(GetFeatureName(fid)).Replace("Base Output", "DSS Profile Output");
		using (H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename))
		{
			if (h5Reader.ExistsAsGroup(replace) && h5Reader.ExistsAsAttribute(replace, "Highest Elev Pump Line"))
			{
				h5Reader.ReadAttributeScalar(replace, "Highest Elev Pump Line", ref naN);
			}
		}
		return naN;
	}

	public StructureIdentifier GetStructureIdentifier(int fID)
	{
		return StructureIdentifier.FromPumpStationName(GetFeatureName(fID));
	}

	StructureIdentifier IStructureLayer.GetStructureIdentifier(int fID)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetStructureIdentifier
		return this.GetStructureIdentifier(fID);
	}

	public bool ComputeSetConnectivity(PumpLayer pumps, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		bool condition = false;
		checked
		{
			int count = pumps.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				int structureIndex = pumps.TryGetStructureIndex(i);
				if (structureIndex == -1)
				{
					continue;
				}
				MeshFV2D inletMesh = GetInletMesh(structureIndex);
				MeshFV2D outletMesh = GetOutletMesh(structureIndex);
				List<MeshFV2D.ProfileCellRange> cellsUnderSegment = null;
				if (inletMesh != null)
				{
					SegmentM uSSegment = IStructurePolylineLayerExtensions.GetUSSegment(pumps, i);
					if (uSSegment.IsNull())
					{
						prog.ReportError("Error generating upstream segment for pump " + pumps.GetFeatureName(i).InDoubleQuotes() + ". Invalid polyline or width.");
						condition = true;
					}
					else
					{
						cellsUnderSegment = inletMesh.CellsUnderSegment(uSSegment);
						if (Math.Abs(cellsUnderSegment.Sum([SpecialName] (MeshFV2D.ProfileCellRange a) => a.StationEnd - a.StationStart) - uSSegment.Length) > 0.001)
						{
							prog.ReportError(pumps.GetFeatureName(i) + " on structure " + GetFeatureName(structureIndex) + " does not entirely intersect the upstream area " + GetInletSA2DName(structureIndex));
							condition = true;
						}
					}
				}
				List<MeshFV2D.ProfileCellRange> cellsUnderSegmentList = null;
				if (outletMesh != null)
				{
					SegmentM dSSegment = IStructurePolylineLayerExtensions.GetDSSegment(pumps, i);
					if (dSSegment.IsNull())
					{
						prog.ReportError("Error generating downstream segment for pump " + pumps.GetFeatureName(i).InDoubleQuotes() + ". Invalid polyline or width.");
						condition = true;
					}
					else
					{
						cellsUnderSegmentList = outletMesh.CellsUnderSegment(dSSegment);
						if (Math.Abs(cellsUnderSegmentList.Sum([SpecialName] (MeshFV2D.ProfileCellRange a) => a.StationEnd - a.StationStart) - dSSegment.Length) > 0.001)
						{
							prog.ReportError(pumps.GetFeatureName(i) + " on structure " + GetFeatureName(structureIndex) + " does not entirely intersect the downstream area " + GetOutletSA2DName(structureIndex));
							condition = true;
						}
					}
				}
				IStructurePolylineLayerExtensions.SetUSCellList(pumps, i, cellsUnderSegment);
				IStructurePolylineLayerExtensions.SetDSCellList(pumps, i, cellsUnderSegmentList);
			}
			return !condition;
		}
	}

	public MeshFV2D GetInletMesh(int fid)
	{
		string inletSA2DName = GetInletSA2DName(fid);
		if (string.IsNullOrEmpty(inletSA2DName))
		{
			return null;
		}
		RASD2FlowArea rASD2FlowArea = Geometry?.D2FlowArea;
		if (rASD2FlowArea == null)
		{
			return null;
		}
		int featureByName = rASD2FlowArea.GetFeatureByName(inletSA2DName);
		if (featureByName < 0)
		{
			return null;
		}
		return rASD2FlowArea.Mesh(featureByName);
	}

	public MeshFV2D GetOutletMesh(int fid)
	{
		string outletSA2DName = GetOutletSA2DName(fid);
		if (string.IsNullOrEmpty(outletSA2DName))
		{
			return null;
		}
		RASD2FlowArea rASD2FlowArea = Geometry?.D2FlowArea;
		if (rASD2FlowArea == null)
		{
			return null;
		}
		int featureByName = rASD2FlowArea.GetFeatureByName(outletSA2DName);
		if (featureByName < 0)
		{
			return null;
		}
		return rASD2FlowArea.Mesh(featureByName);
	}

	public string GetInletRiverName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Inlet River");
	}

	public void SetInletRiverName(int fID, string rivName)
	{
		FeatureRow(fID)["Inlet River"] = rivName;
	}

	public string GetOutletRiverName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Outlet River");
	}

	public void SetOutletRiverName(int fID, string rivName)
	{
		FeatureRow(fID)["Outlet River"] = rivName;
	}

	public string GetInletReachName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Inlet Reach");
	}

	public void SetInletReachName(int fID, string rchName)
	{
		FeatureRow(fID)["Inlet Reach"] = rchName;
	}

	public string GetOutletReachName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Outlet Reach");
	}

	public void SetOutletReachName(int fID, string rchName)
	{
		FeatureRow(fID)["Outlet Reach"] = rchName;
	}

	public string GetInletRS(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Inlet RS");
	}

	public void SetInletRS(int fID, string rsName)
	{
		FeatureRow(fID)["Inlet RS"] = rsName;
	}

	public string GetOutletRS(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Outlet RS");
	}

	public void SetOutletRS(int fID, string rsName)
	{
		FeatureRow(fID)["Outlet RS"] = rsName;
	}

	public string GetInletSA2DName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Inlet SA/2D");
	}

	public void SetInletSA2DName(int fID, string sa2DName)
	{
		FeatureRow(fID)["Inlet SA/2D"] = sa2DName;
	}

	public string GetOutletSA2DName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Outlet SA/2D");
	}

	public void SetOutletSA2DName(int fID, string sa2DName)
	{
		FeatureRow(fID)["Outlet SA/2D"] = sa2DName;
	}
}
