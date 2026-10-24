using System;
using System.Collections.Generic;
using System.Linq;
using H5Assist.VB;
using RasMapperLib.Structures;

namespace RasMapperLib;

public class GateGroupLayer : DependentPolygonFeatureLayer, IGeometryLayer, IGroupLayer
{
	public enum GateGroupMethod
	{
		Sluice,
		Radial,
		OverflowClosedTop,
		OverflowOpenAir,
		UserDefinedCurves
	}

	internal class Cols
	{
		public const string StructureIdentifier = "Structure Identifier";

		public const string StructureIndex = "Structure ID";

		public const string Name = "Group Name";

		public const string Width = "Width";

		public const string Height = "Height";

		public const string Invert = "Invert";

		public const string Method = "Method";

		public const string SluiceCoef = "Sluice Coef";

		public const string RadialCoef = "Radial Coef";

		public const string ExpTrunion = "Exp Trunion";

		public const string ExpOpen = "Exp Open";

		public const string ExpHead = "Exp Head";

		public const string TrunionHeight = "Trunion Height";

		public const string OrificeCoef = "Orifice Coef";

		public const string HeadRef = "Head Ref";

		public const string SpillwayShape = "Spillway Shape";

		public const string WeirCoef = "Weir Coef";

		public const string DesignHead = "Design Head";

		public const string DesignHeight = "Design Height";

		public const string UseRehbok = "Use Rehbok";

		public const string UseKVC = "Use KVC";

		public const string KVCMethod = "KVC Method";

		public const string UserCurveSet = "User Curve Set";
	}

	public const string sGateGroups = "Gate Groups";

	public const string HDFGroupName = "Geometry/Structures/Gate Groups";

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool CanEdit { get; }

	public override bool ForwardEditRequestsToParent { get; }
string IGeometryLayer.HDFGroupName=> "Geometry/Structures/Gate Groups";

	public GateGroupLayer(RASGeometry geom)
		: base("Gate Groups", geom.SourceFilename)
	{
		SimpleFeatureTypeName = "Group";
		CanEdit = false;
		ForwardEditRequestsToParent = true;
		Geometry = geom;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Structure ID", "Structure ID", typeof(int), null, DataColumnViewState.Invisible);
		AddManagedAttributeColumn("Group Name", "Name", typeof(string));
		DataColumnGroup dataColumnGroup = new DataColumnGroup("Culvert Information", isVisible: true);
		AddManagedAttributeColumn("Width", "Width", typeof(float), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Height", "Height", typeof(float), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Invert", "Invert", typeof(float), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Method", "Method", typeof(int), dataColumnGroup, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup2 = new DataColumnGroup("Coefficients", isVisible: true);
		AddManagedAttributeColumn("Sluice Coef", "Sluice Coef", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Radial Coef", "Radial Coef", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Exp Trunion", "Trunion Exponent", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Exp Open", "Open Exponent", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Exp Head", "Head Exponent", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Orifice Coef", "Orifice Coef", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Head Ref", "Head Reference", typeof(int), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Trunion Height", "Trunion Height", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Spillway Shape", "Spillway Shape", typeof(string), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Weir Coef", "Weir Coef", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Design Head", "Design Head", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Design Height", "Design Height", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Use Rehbok", "Use Rehbok", typeof(bool), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Use KVC", "Use Kindsvater Carter", typeof(bool), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("KVC Method", "Kindsvater Carter Method", typeof(int), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("User Curve Set", "User Curve Set", typeof(string), dataColumnGroup2, DataColumnViewState.GrayedOut);
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		if (!hr.ExistsAsGroup("Geometry/Structures/Gate Groups"))
		{
			return;
		}
		string path = "Geometry/Structures/Gate Groups/Attributes";
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
				int count2 = FeatureCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
				}
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
			hw.Delete("Geometry/Structures/Gate Groups");
			return;
		}
		hw.CreateGroup("Geometry/Structures/Gate Groups");
		WriteManagedColumns(hw, "Geometry/Structures/Gate Groups/Attributes");
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
			Geometry.GateOpenings.UpdateAssociations(oldNewIndexMap2.Item1, oldNewIndexMap2.Item2);
		}
	}

	protected override Polygon RecomputePolygon(int fid)
	{
		return StructureElementLayerExtensions.RecomputeGroupPolygon(this, Geometry.GateOpenings, fid);
	}

	public override void SetFeatureName(int fid, string newName)
	{
		SetGroupName(fid, newName);
	}

	public override string GetFeatureName(int fid)
	{
		return GetGroupName(fid);
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
		string dset = Geometry.Structures.GetResultGroupName(structureIndex) + "/Gate Groups/" + GetFeatureName(fid);
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
		string replaceTerm = Geometry.Structures.GetResultGroupName(structureIndex) + "/Gate Groups/" + GetFeatureName(fid);
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
		string replaceTerm = Geometry.Structures.GetResultGroupName(structureIndex) + "/Gate Groups/" + GetFeatureName(fid);
		replaceTerm = replaceTerm.Replace("Base Output", "DSS Profile Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, replaceTerm);
	}

	protected override List<string> GetColumnDependecies()
	{
		return new string[1] { "Width" }.ToList();
	}

	protected override List<FeatureLayer> GetLayerDependencies()
	{
		return new List<FeatureLayer>(new FeatureLayer[1] { Geometry.GateOpenings });
	}

	public string GetGroupName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Group Name");
	}

	public void SetGroupName(int fid, string name)
	{
		FeatureRow(fid)["Group Name"] = name;
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

	public float GetWidth(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Width");
	}

	float IGroupLayer.GetWidth(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetWidth
		return this.GetWidth(fid);
	}

	public void SetWidth(int fID, float width)
	{
		FeatureRow(fID)["Width"] = width;
	}

	void IGroupLayer.SetWidth(int fID,float width)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetWidth
		this.SetWidth(fID, width);
	}

	public float GetHeight(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Height");
	}

	public void SetHeight(int fID, float height)
	{
		FeatureRow(fID)["Height"] = height;
	}

	public float GetInvert(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Invert");
	}

	public void SetInvert(int fID, float inv)
	{
		FeatureRow(fID)["Invert"] = inv;
	}

	public GateGroupMethod GetMethod(int fid)
	{
		return (GateGroupMethod)DataTableExtensions.GetInteger(FeatureTable(), fid, "Method");
	}

	public void SetMethod(int fID, GateGroupMethod method)
	{
		FeatureRow(fID)["Method"] = method;
	}

	public float GetWeirCoef(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Weir Coef");
	}

	public void SetWeirCoef(int fID, float weirCoef)
	{
		FeatureRow(fID)["Weir Coef"] = weirCoef;
	}
}
