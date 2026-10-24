using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using RasMapperLib.Structures;
using Utility.Progress;

namespace RasMapperLib;

public class PumpLayer : PolylineFeatureLayer, IGeometryLayer, IOpeningLayer
{
	public class Cols
	{
		public const string PumpGroupID = "Pump Group ID";

		public const string PumpName = "Name";

		public const string WSOn = "WS On";

		public const string WSOff = "WS Off";

		public const string PointCount = "Point Count";

		public const string EntryCells = "Entry Cells";

		public const string ExitCells = "Exit Cells";

		public const string IsDefaultCenterline = "Default Centerline";
	}

	public const string HDFGroupName = "Geometry/Pump Stations/Pump Groups/Pumps";

	public const string sPumps = "Pumps";

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override bool RefreshOnNodeSelection { get; }

	public override bool CanRename => false;

	public override string DefaultLayerName => "Pumps";
string IGeometryLayer.HDFGroupName=> "Geometry/Pump Stations/Pump Groups/Pumps";

	public PumpLayer(RASGeometry geometry)
		: base("Pumps", geometry.SourceFilename)
	{
		SimpleFeatureTypeName = "Pump";
		ForwardEditRequestsToParent = true;
		RefreshOnNodeSelection = true;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		DataColumnViewState viewState = DataColumnViewState.GrayedOut;
		AddManagedAttributeColumn("Pump Group ID", "Pump Group ID", typeof(int), null, viewState);
		AddManagedAttributeColumn("Name", "Name", typeof(string), null, viewState);
		AddManagedAttributeColumn("WS On", "WS On", typeof(float), null, viewState);
		AddManagedAttributeColumn("WS Off", "WS Off", typeof(float), null, viewState);
		IStructurePolylineLayerExtensions.InitializeCommonStructurePolylineColumns(this);
		AddManagedAttributeColumn("Default Centerline", "Default Centerline", typeof(bool), null, DataColumnViewState.Invisible);
		AddManagedWriteOnlyAttributeColumn("Pump Station ID", [SpecialName] (int fid) => Geometry.PumpGroups.TryGetStructureIndex(TryGetGroupIndex(fid)));
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Geometry/Pump Stations/Pump Groups/Pumps"))
		{
			string path = "Geometry/Pump Stations/Pump Groups/Pumps/Attributes";
			if (hr.ExistsAsDataset(path))
			{
				HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/Pump Stations/Pump Groups/Pumps/Centerline");
				ReadManagedColumns(hr, path);
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	public void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/Pump Stations/Pump Groups/Pumps");
			return;
		}
		hw.CreateGroup("Geometry/Pump Stations/Pump Groups/Pumps");
		HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/Pump Stations/Pump Groups/Pumps/Centerline");
		WriteManagedColumns(hw, "Geometry/Pump Stations/Pump Groups/Pumps/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.Add(new PlotOption("Plot Width"));
		return staticPlotOptions;
	}

	public override List<PlotOption> GetDefaultPlotOptions()
	{
		List<PlotOption> defaultPlotOptions = base.GetDefaultPlotOptions();
		defaultPlotOptions.Add(new PlotOption("Plot Width"));
		return defaultPlotOptions;
	}

	public bool EnsureConnectivity(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		using H5Writer hw = new H5Writer(SourceFilename);
		return EnsureConnectivity(hw, prog);
	}

	public bool EnsureConnectivity(H5Writer hw, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		using (new FeatureTableReloadSuppressor(this))
		{
			if (!Geometry.PumpStations.ComputeSetConnectivity(this, prog))
			{
				return false;
			}
			IOpeningLayerExtensions.WriteCellsConnectivity(this, hw, "Geometry/Pump Stations/Pump Groups/Pumps/Inlet Cells", "Pump Station ID", "Pump Group ID", "Pump ID", [SpecialName] (int fID) => IStructurePolylineLayerExtensions.GetUSCellList(this, fID));
			IOpeningLayerExtensions.WriteCellsConnectivity(this, hw, "Geometry/Pump Stations/Pump Groups/Pumps/Outlet Cells", "Pump Station ID", "Pump Group ID", "Pump ID", [SpecialName] (int fID) => IStructurePolylineLayerExtensions.GetDSCellList(this, fID));
		}
		return true;
	}

	public int AddNewElement(int parentGroupIndex, string elementName = "")
	{
		throw new NotImplementedException();
	}

	int IStructurePolylineLayer.AddNewElement(int parentGroupIndex,string elementName )
	{
		//ILSpy generated this explicit interface implementation from .override directive in AddNewElement
		return this.AddNewElement(parentGroupIndex, elementName);
	}

	public bool ShouldProcessGeospatialHwTwConnectivity(int fID)
	{
		if (!(Feature(fID) is PolylineEditable))
		{
			return !GetIsDefaultCenterline(fID);
		}
		return true;
	}

	bool IStructurePolylineLayer.ShouldProcessGeospatialHwTwConnectivity(int fID)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ShouldProcessGeospatialHwTwConnectivity
		return this.ShouldProcessGeospatialHwTwConnectivity(fID);
	}

	public int TryGetStructureIndex(int fid)
	{
		int groupIndex = TryGetGroupIndex(fid);
		if (groupIndex == -1 || groupIndex >= ((FeatureLayer)GetAssociatedGroupLayer()).FilteredFeatureCount())
		{
			return -1;
		}
		return GetAssociatedGroupLayer().TryGetStructureIndex(groupIndex);
	}

	int IStructureElementLayer.TryGetStructureIndex(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TryGetStructureIndex
		return this.TryGetStructureIndex(fid);
	}

	public int TryGetGroupIndex(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Pump Group ID", -1);
	}

	int IOpeningLayer.TryGetGroupIndex(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TryGetGroupIndex
		return this.TryGetGroupIndex(fid);
	}

	public void SetGroup(int fid, int groupFid)
	{
		FeatureRow(fid)["Pump Group ID"] = groupFid;
	}

	void IOpeningLayer.SetGroup(int fid,int groupFid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetGroup
		this.SetGroup(fid, groupFid);
	}

	public IGroupLayer GetAssociatedGroupLayer()
	{
		return Geometry.PumpGroups;
	}

	IGroupLayer IOpeningLayer.GetAssociatedGroupLayer()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetAssociatedGroupLayer
		return this.GetAssociatedGroupLayer();
	}

	public void UpdateAssociations(int fromGroupFID, int toGroupFID)
	{
		if (fromGroupFID == -1)
		{
			return;
		}
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (TryGetGroupIndex(i) == fromGroupFID)
				{
					SetGroup(i, toGroupFID);
				}
			}
		}
	}

	void IOpeningLayer.UpdateAssociations(int fromGroupFID,int toGroupFID)
	{
		//ILSpy generated this explicit interface implementation from .override directive in UpdateAssociations
		this.UpdateAssociations(fromGroupFID, toGroupFID);
	}

	private float GetWidth(int fID)
	{
		return IOpeningLayerExtensions.GetWidthFromGroup(this, fID);
	}

	float IStructurePolylineLayer.GetWidth(int fID)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetWidth
		return this.GetWidth(fID);
	}

	public IStructureLayer GetAssociatedStructureLayer()
	{
		return Geometry.PumpStations;
	}

	IStructureLayer IStructureElementLayer.GetAssociatedStructureLayer()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetAssociatedStructureLayer
		return this.GetAssociatedStructureLayer();
	}

	public override string GetFeatureName(int fid)
	{
		return GetPumpName(fid);
	}

	public string GetPumpName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Name");
	}

	public void SetPumpName(int fID, string name)
	{
		FeatureRow(fID)["Name"] = name;
	}

	public override void SetFeatureName(int fid, string newName)
	{
		SetPumpName(fid, newName);
	}

	public int GetPumpGroupID(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Pump Group ID");
	}

	public bool GetIsDefaultCenterline(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Default Centerline");
	}

	public void SetIsDefaultCenterline(int fid, bool isDefaultCenterline)
	{
		FeatureRow(fid)["Default Centerline"] = isDefaultCenterline;
	}
}
