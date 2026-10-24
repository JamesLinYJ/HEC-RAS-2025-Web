using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using RasMapperLib.Structures;
using Utility.Progress;

namespace RasMapperLib;

public class CulvertBarrelLayer : PolylineFeatureLayer, IGeometryLayer, IOpeningLayer
{
	public class Cols
	{
		public const string GroupId = "Group ID";

		public const string BarrelName = "Barrel Name";

		public const string USStation = "US Station";

		public const string DSStation = "DS Station";

		public const string IsDefaultCenterline = "Default Centerline";
	}

	public const string sBarrels = "Culvert Barrels";

	private const string sType = "Type";

	public const string HDFGroupName = "Geometry/Structures/Culvert Groups/Barrels";

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool CanEdit { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override bool RefreshOnNodeSelection { get; }

	public override string DefaultLayerName => "Culvert Barrels";
string IGeometryLayer.HDFGroupName=> "Geometry/Structures/Culvert Groups/Barrels";

	public override bool CanRename => false;

	public CulvertBarrelLayer(RASGeometry geometry)
		: base("Culvert Barrels", geometry.SourceFilename)
	{
		SimpleFeatureTypeName = "Barrel";
		CanEdit = true;
		ForwardEditRequestsToParent = true;
		RefreshOnNodeSelection = true;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Group ID", "Culvert Group ID", typeof(int));
		AddManagedAttributeColumn("Barrel Name", "Name", typeof(string));
		AddManagedAttributeColumn("US Station", "US Station", typeof(float), null, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("DS Station", "DS Station", typeof(float), null, DataColumnViewState.GrayedOut);
		IStructurePolylineLayerExtensions.InitializeCommonStructurePolylineColumns(this);
		AddManagedAttributeColumn("Default Centerline", "Default Centerline", typeof(bool), null, DataColumnViewState.Invisible);
		AddManagedWriteOnlyAttributeColumn("Structure ID", [SpecialName] (int fid) => Geometry.CulvertGroups.TryGetStructureIndex(TryGetGroupIndex(fid)));
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Geometry/Structures/Culvert Groups/Barrels"))
		{
			string path = "Geometry/Structures/Culvert Groups/Barrels/Attributes";
			if (hr.ExistsAsDataset(path))
			{
				HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/Structures/Culvert Groups/Barrels/Centerline");
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
			hw.Delete("Geometry/Structures/Culvert Groups/Barrels");
			return;
		}
		hw.CreateGroup("Geometry/Structures/Culvert Groups/Barrels");
		HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/Structures/Culvert Groups/Barrels/Centerline");
		WriteManagedColumns(hw, "Geometry/Structures/Culvert Groups/Barrels/Attributes");
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
			if (!Geometry.Structures.ComputeSetConnectivity(this, prog))
			{
				return false;
			}
			IOpeningLayerExtensions.WriteCellsConnectivity(this, hw, "Geometry/Structures/Culvert Groups/Barrels/Upstream Cells", "Structure ID", "Culv Group ID", "Barrel ID", [SpecialName] (int fID) => IStructurePolylineLayerExtensions.GetUSCellList(this, fID));
			IOpeningLayerExtensions.WriteCellsConnectivity(this, hw, "Geometry/Structures/Culvert Groups/Barrels/Downstream Cells", "Structure ID", "Culv Group ID", "Barrel ID", [SpecialName] (int fID) => IStructurePolylineLayerExtensions.GetDSCellList(this, fID));
		}
		return true;
	}

	public override string GetFeatureName(int fid)
	{
		return GetBarrelName(fid);
	}

	public override void SetFeatureName(int fid, string newName)
	{
		SetBarrelName(fid, newName);
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
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Group ID", -1);
	}

	int IOpeningLayer.TryGetGroupIndex(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TryGetGroupIndex
		return this.TryGetGroupIndex(fid);
	}

	public void SetGroup(int fid, int groupFid)
	{
		FeatureRow(fid)["Group ID"] = groupFid;
	}

	void IOpeningLayer.SetGroup(int fid,int groupFid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetGroup
		this.SetGroup(fid, groupFid);
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

	public IGroupLayer GetAssociatedGroupLayer()
	{
		return Geometry.CulvertGroups;
	}

	IGroupLayer IOpeningLayer.GetAssociatedGroupLayer()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetAssociatedGroupLayer
		return this.GetAssociatedGroupLayer();
	}

	public IStructureLayer GetAssociatedStructureLayer()
	{
		return Geometry.Structures;
	}

	IStructureLayer IStructureElementLayer.GetAssociatedStructureLayer()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetAssociatedStructureLayer
		return this.GetAssociatedStructureLayer();
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

	public string GetBarrelName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Barrel Name");
	}

	public void SetBarrelName(int fid, string newName)
	{
		FeatureRow(fid)["Barrel Name"] = newName;
	}

	public float GetUSStation(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "US Station");
	}

	public void SetUSStation(int fID, float sta)
	{
		FeatureRow(fID)["US Station"] = sta;
	}

	public float GetDSStation(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "DS Station");
	}

	public void SetDSStation(int fID, float sta)
	{
		FeatureRow(fID)["DS Station"] = sta;
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
