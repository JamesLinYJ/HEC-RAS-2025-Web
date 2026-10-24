using System;
using System.Collections.Generic;
using System.Linq;
using H5Assist.VB;
using RasMapperLib.Structures;

namespace RasMapperLib;

public class CulvertGroupLayer : DependentPolygonFeatureLayer, IGeometryLayer, IGroupLayer
{
	public enum CulvertShape
	{
		Circle = 1,
		Box,
		PipeArch,
		Ellipse,
		Arch,
		SemiCircle,
		LowArch,
		HighArch,
		ConspanArch
	}

	public class Cols
	{
		public const string StructureIdx = "Structure ID";

		public const string Name = "Group Name";

		public const string ShapeID = "Shape ID";

		public const string ShapeName = "Shape Name";

		public const string ChartID = "Chart ID";

		public const string ChartDesc = "Chart Name";

		public const string ScaleID = "Scale ID";

		public const string ScaleDesc = "Scale Name";

		public const string Rise = "Rise";

		public const string Span = "Span";

		public const string Length = "Barrel Length";

		public const string USDistance = "Upstream Distance";

		public const string TopMann = "Top Manning's n";

		public const string BotMann = "Bottom Manning's n";

		public const string DepthBotMann = "Bottom Mann. Depth";

		public const string DepthBlocked = "Depth Blocked";

		public const string EntranceLossCoef = "Entrance Loss Coef.";

		public const string ExitLossCoef = "Exit Loss Coef.";

		public const string InvertUS = "Invert US";

		public const string InvertDS = "Invert DS";
	}

	public const string sCulvertGroups = "Culvert Groups";

	public const string HDFGroupName = "Geometry/Structures/Culvert Groups";

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool CanEdit { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Culvert Groups";
string IGeometryLayer.HDFGroupName=> "Geometry/Structures/Culvert Groups";

	public CulvertGroupLayer(RASGeometry geometry)
		: base("Culvert Groups", geometry.SourceFilename)
	{
		SimpleFeatureTypeName = "Group";
		CanEdit = false;
		ForwardEditRequestsToParent = true;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Structure ID", "Structure ID", typeof(int));
		AddManagedAttributeColumn("Group Name", "Name", typeof(string));
		DataColumnGroup dataColumnGroup = new DataColumnGroup("Shape Info", isVisible: true);
		AddManagedAttributeColumn("Shape ID", "Shape", typeof(int), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Chart ID", "Chart", typeof(int), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Scale ID", "Scale", typeof(int), dataColumnGroup, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup2 = new DataColumnGroup("Coefficients", isVisible: true);
		AddManagedAttributeColumn("Rise", "Rise", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Span", "Span", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Barrel Length", "Length", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Upstream Distance", "US Distance", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Top Manning's n", "Mann Top", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Bottom Manning's n", "Mann Bottom", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Bottom Mann. Depth", "Depth for Bottom Mann", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Depth Blocked", "Depth Blocked", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Entrance Loss Coef.", "Entrance Loss", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Exit Loss Coef.", "Exit Loss", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Invert US", "US Invert", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Invert DS", "DS Invert", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (!hr.ExistsAsGroup("Geometry/Structures/Culvert Groups"))
		{
			return;
		}
		string path = "Geometry/Structures/Culvert Groups/Attributes";
		checked
		{
			if (hr.ExistsAsDataset(path))
			{
				int count = (int)hr.GetDatasetDimensions(path)[0] - 1 - FeatureCount();
				for (int i = 0; i <= count; i++)
				{
					AddFeature(RasMapperLib.Polygon.NullPolygon());
				}
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
			hw.Delete("Geometry/Structures/Culvert Groups");
			return;
		}
		hw.CreateGroup("Geometry/Structures/Culvert Groups");
		WriteManagedColumns(hw, "Geometry/Structures/Culvert Groups/Attributes");
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
			Geometry.CulvertBarrels.UpdateAssociations(oldNewIndexMap2.Item1, oldNewIndexMap2.Item2);
		}
	}

	protected override List<string> GetColumnDependecies()
	{
		return new string[1] { "Span" }.ToList();
	}

	protected override List<FeatureLayer> GetLayerDependencies()
	{
		return new List<FeatureLayer>(new FeatureLayer[1] { Geometry.CulvertBarrels });
	}

	protected override Polygon RecomputePolygon(int fid)
	{
		return StructureElementLayerExtensions.RecomputeGroupPolygon(this, Geometry.CulvertBarrels, fid);
	}

	public override void SetFeatureName(int fid, string newName)
	{
		SetGroupName(fid, newName);
	}

	public override string GetFeatureName(int fid)
	{
		return GetGroupName(fid);
	}

	public int AddNewGroup(int parentStructureIndex, string groupName = "")
	{
		return IGroupLayerExtensions.AddNewGroup(this, parentStructureIndex, groupName);
	}

	int IGroupLayer.AddNewGroup(int parentStructureIndex,string groupName )
	{
		//ILSpy generated this explicit interface implementation from .override directive in AddNewGroup
		return this.AddNewGroup(parentStructureIndex, groupName);
	}

	public void SetAssociatedStructure(int fid, int parentStructureIndex)
	{
		SetActualStructureIndex(fid, parentStructureIndex);
	}

	void IGroupLayer.SetAssociatedStructure(int fid,int parentStructureIndex)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetAssociatedStructure
		this.SetAssociatedStructure(fid, parentStructureIndex);
	}

	public void UpdateAssociations(int fromStructureFID, int toStructureFID)
	{
		if (fromStructureFID == -1)
		{
			return;
		}
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (TryGetStructureIndex(i) == fromStructureFID)
				{
					SetActualStructureIndex(i, toStructureFID);
				}
			}
		}
	}

	void IGroupLayer.UpdateAssociations(int fromStructureFID,int toStructureFID)
	{
		//ILSpy generated this explicit interface implementation from .override directive in UpdateAssociations
		this.UpdateAssociations(fromStructureFID, toStructureFID);
	}

	public int TryGetStructureIndex(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Structure ID", -1);
	}

	int IStructureElementLayer.TryGetStructureIndex(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TryGetStructureIndex
		return this.TryGetStructureIndex(fid);
	}

	private void SetActualStructureIndex(int fid, int idx)
	{
		FeatureRow(fid)["Structure ID"] = idx;
	}

	void IGroupLayer.SetActualStructureIndex(int fid,int idx)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetActualStructureIndex
		this.SetActualStructureIndex(fid, idx);
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

	public List<TimeSeriesVariable> ReadResultVariables(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		int structureIndex = TryGetStructureIndex(fid);
		if (structureIndex < 0)
		{
			return null;
		}
		string dset = Geometry.Structures.GetResultGroupName(structureIndex) + "/Culvert Groups/" + GetFeatureName(fid);
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadResultVariables_DSSHydrograph(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		int structureIndex = TryGetStructureIndex(fid);
		if (structureIndex < 0)
		{
			return null;
		}
		string replaceTerm = Geometry.Structures.GetResultGroupName(structureIndex) + "/Culvert Groups/" + GetFeatureName(fid);
		replaceTerm = replaceTerm.Replace("Base Output", "DSS Hydrograph Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, replaceTerm);
	}

	public List<TimeSeriesVariable> ReadResultVariables_DSSProfile(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		int structureIndex = TryGetStructureIndex(fid);
		if (structureIndex < 0)
		{
			return null;
		}
		string replaceTerm = Geometry.Structures.GetResultGroupName(structureIndex) + "/Culvert Groups/" + GetFeatureName(fid);
		replaceTerm = replaceTerm.Replace("Base Output", "DSS Profile Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, replaceTerm);
	}

	public string GetGroupName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Group Name");
	}

	public void SetGroupName(int fid, string name)
	{
		FeatureRow(fid)["Group Name"] = name;
	}

	public float GetRiseCoef(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Rise");
	}

	public void SetRiseCoef(int fID, float riseCoef)
	{
		FeatureRow(fID)["Rise"] = riseCoef;
	}

	public float GetSpanCoef(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Span");
	}

	float IGroupLayer.GetWidth(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetSpanCoef
		return this.GetSpanCoef(fid);
	}

	public void SetSpanCoef(int fID, float spanCoef)
	{
		FeatureRow(fID)["Span"] = spanCoef;
	}

	void IGroupLayer.SetWidth(int fID,float spanCoef)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetSpanCoef
		this.SetSpanCoef(fID, spanCoef);
	}

	public float GetLength(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Barrel Length");
	}

	public void SetLength(int fID, float length)
	{
		FeatureRow(fID)["Barrel Length"] = length;
	}

	public float GetUSDistance(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Upstream Distance");
	}

	public void SetUSDistance(int fID, float uSDistance)
	{
		FeatureRow(fID)["Upstream Distance"] = uSDistance;
	}

	public float GetTopMann(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Top Manning's n");
	}

	public void SetTopMann(int fID, float topMann)
	{
		FeatureRow(fID)["Top Manning's n"] = topMann;
	}

	public float GetBotMann(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Bottom Manning's n");
	}

	public void SetBotMann(int fID, float botMann)
	{
		FeatureRow(fID)["Bottom Manning's n"] = botMann;
	}

	public float GetDepthToUse(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Bottom Mann. Depth");
	}

	public void SetDepthToUse(int fID, float depthToUse)
	{
		FeatureRow(fID)["Bottom Mann. Depth"] = depthToUse;
	}

	public float GetDepthBlocked(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Depth Blocked");
	}

	public void SetDepthBlocked(int fID, float depthBlocked)
	{
		FeatureRow(fID)["Depth Blocked"] = depthBlocked;
	}

	public float GetEntranceLossCoef(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Entrance Loss Coef.");
	}

	public void SetEntranceLossCoef(int fID, float entranceLossCoef)
	{
		FeatureRow(fID)["Entrance Loss Coef."] = entranceLossCoef;
	}

	public float GetExitLossCoef(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Exit Loss Coef.");
	}

	public void SetExitLossCoef(int fID, float exitLossCoef)
	{
		FeatureRow(fID)["Exit Loss Coef."] = exitLossCoef;
	}

	public float GetInvertUS(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Invert US");
	}

	public void SetInvertUS(int fID, float invertUS)
	{
		FeatureRow(fID)["Invert US"] = invertUS;
	}

	public float GetInvertDS(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Invert DS");
	}

	public void SetInvertDS(int fID, float invertDS)
	{
		FeatureRow(fID)["Invert DS"] = invertDS;
	}

	public CulvertShape GetShapeID(int fid)
	{
		return (CulvertShape)DataTableExtensions.GetInteger(FeatureTable(), fid, "Shape ID");
	}

	public void SetShapeID(int fID, CulvertShape shapeID)
	{
		FeatureRow(fID)["Shape ID"] = (int)shapeID;
	}

	public int GetChartID(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Chart ID");
	}

	public void SetChartID(int fID, int chartID)
	{
		FeatureRow(fID)["Chart ID"] = chartID;
	}

	public int GetScaleID(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Scale ID");
	}

	public void SetScaleID(int fID, int scaleID)
	{
		FeatureRow(fID)["Scale ID"] = scaleID;
	}
}
