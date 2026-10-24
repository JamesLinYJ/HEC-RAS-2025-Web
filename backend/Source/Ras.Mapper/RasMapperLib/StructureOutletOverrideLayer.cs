using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using H5Assist.Identifiers;
using H5Assist.VB;
using RasMapperLib.Structures;
using Utility.Progress;

namespace RasMapperLib;

public abstract class StructureOutletOverrideLayer : PolylineFeatureLayer, IGeometryLayer, IStructurePolylineLayer
{
	public class Cols
	{
		public const string StructID = "Structure ID";

		public const string Name = "Name";

		public const string Width = "Width";

		public const string Station = "Station";

		public const string IsDefaultCenterline = "Default Centerline";
	}

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override bool FeaturesRequireUniqueNames { get; set; }

	public abstract string HDFGroupName { get; }

	public StructureOutletOverrideLayer(RASGeometry geom, string name)
		: base(name, geom.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		FeaturesRequireUniqueNames = true;
		Geometry = geom;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Structure ID", "Structure ID", typeof(int));
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddManagedAttributeColumn("Station", "Station", typeof(float));
		AddManagedAttributeColumn("Width", "Width", typeof(float));
		IStructurePolylineLayerExtensions.InitializeCommonStructurePolylineColumns(this);
		AddManagedAttributeColumn("Default Centerline", "Default Centerline", typeof(bool), null, DataColumnViewState.Invisible);
	}

	protected virtual void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup(HDFGroupName) && hr.ExistsAsDataset(HDFGroupName + "/Attributes"))
		{
			HDF5Storage.ReadPolyFeatures(this, hr, HDFGroupName + "/Centerline");
			ReadManagedColumns(hr, HDFGroupName + "/Attributes");
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
			hw.Delete(HDFGroupName);
			return;
		}
		hw.CreateGroup(HDFGroupName);
		HDF5Storage.WritePolylineFeatures(this, hw, HDFGroupName + "/Centerline");
		WriteManagedColumns(hw, HDFGroupName + "/Attributes");
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

	public int TryGetStructureIndex(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Structure ID", -1);
	}

	int IStructureElementLayer.TryGetStructureIndex(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TryGetStructureIndex
		return this.TryGetStructureIndex(fid);
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

	public void SetStructure(int fid, int structureFid)
	{
		FeatureRow(fid)["Structure ID"] = structureFid;
	}

	public abstract int AddNewElement(int parentIndex, string elementName = "");

	int IStructurePolylineLayer.AddNewElement(int parentIndex,string elementName )
	{
		//ILSpy generated this explicit interface implementation from .override directive in AddNewElement
		return this.AddNewElement(parentIndex, elementName);
	}

	public float GetWidth(int fID)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fID, "Width");
	}

	float IStructurePolylineLayer.GetWidth(int fID)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetWidth
		return this.GetWidth(fID);
	}

	public void UpdateAssociations(int fromStructureFID, int toStructureFID)
	{
		List<int> elementsWithinStructure = StructureElementLayerExtensions.FindElementsWithinStructure(this, fromStructureFID);
		foreach (int elementsWithinStructure2 in elementsWithinStructure)
		{
			SetStructure(elementsWithinStructure2, toStructureFID);
		}
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
			WriteCellsConnectivity(hw, HDFGroupName + "/Upstream Cells", [SpecialName] (int fID) => IStructurePolylineLayerExtensions.GetUSCellList(this, fID));
			WriteCellsConnectivity(hw, HDFGroupName + "/Downstream Cells", [SpecialName] (int fID) => IStructurePolylineLayerExtensions.GetDSCellList(this, fID));
		}
		return true;
	}

	private void WriteCellsConnectivity(H5Writer hw, string datasetname, Func<int, List<MeshFV2D.ProfileCellRange>> getCellList)
	{
		int getCellListCountSum = 0;
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				List<MeshFV2D.ProfileCellRange> getCellListList = getCellList(i);
				if (getCellListList != null)
				{
					getCellListCountSum += getCellListList.Count;
				}
			}
			if (getCellListCountSum == 0)
			{
				if (hw.ExistsAsDataset(datasetname))
				{
					hw.Delete(datasetname);
				}
				return;
			}
			int[] intValues = new int[getCellListCountSum - 1 + 1];
			int[] intValuesArray = new int[getCellListCountSum - 1 + 1];
			int[] intValues2 = new int[getCellListCountSum - 1 + 1];
			float[] floatValues = new float[getCellListCountSum - 1 + 1];
			float[] floatValuesArray = new float[getCellListCountSum - 1 + 1];
			int ordinal = 0;
			int count2 = FeatureCount() - 1;
			for (int j = 0; j <= count2; j++)
			{
				int structureIndex = TryGetStructureIndex(j);
				List<MeshFV2D.ProfileCellRange> getCellList2 = getCellList(j);
				if (getCellList2 == null)
				{
					continue;
				}
				foreach (MeshFV2D.ProfileCellRange getCellList3 in getCellList2)
				{
					intValues[ordinal] = structureIndex;
					intValuesArray[ordinal] = j;
					intValues2[ordinal] = getCellList3.Cell;
					floatValues[ordinal] = (float)getCellList3.StationStart;
					floatValuesArray[ordinal] = (float)getCellList3.StationEnd;
					ordinal++;
				}
			}
			CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
			compoundTypeDefinition.AddField<int>("Structure ID");
			compoundTypeDefinition.AddField<int>("Outlet ID");
			compoundTypeDefinition.AddField<int>("Cell Index");
			compoundTypeDefinition.AddField<float>("Station Start");
			compoundTypeDefinition.AddField<float>("Station End");
			hw.Create1DCompoundDataset(datasetname, getCellListCountSum, compoundTypeDefinition);
			using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, datasetname, getCellListCountSum);
			compoundWriter.WriteColumn(0, intValues);
			compoundWriter.WriteColumn(1, intValuesArray);
			compoundWriter.WriteColumn(2, intValues2);
			compoundWriter.WriteColumn(3, floatValues);
			compoundWriter.WriteColumn(4, floatValuesArray);
		}
	}

	public bool GetIsDefaultCenterline(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Default Centerline");
	}

	public void SetIsDefaultCenterline(int fid, bool isDefaultCenterline)
	{
		FeatureRow(fid)["Default Centerline"] = isDefaultCenterline;
	}

	public float GetStation(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Station");
	}

	public void SetStation(int fid, float station)
	{
		FeatureRow(fid)["Station"] = station;
	}
}
