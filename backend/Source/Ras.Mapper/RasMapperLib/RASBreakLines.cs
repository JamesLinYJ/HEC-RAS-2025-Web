using System.Data;
using H5Assist.VB;

namespace RasMapperLib;

public class RASBreakLines : PolylineFeatureLayer, IGeometryLayer, IEnforceBreaklineLayer
{
	public string HDFGroupName { get; }

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override bool CanRename { get; }

	public override string DefaultLayerName => "Breaklines";

	public string NearSpacingColumnName { get; }

	public string NearSpacingDiskName { get; }

	public string FarSpacingColumnName { get; }

	public string FarSpacingDiskName { get; }

	public string NearRepeatsColumnName { get; }

	public string NearRepeatsDiskName { get; }

	public override bool FeaturesRequireUniqueNames { get; set; }

	private string IGeometryLayer_HDFGroupName => HDFGroupName;

	public RASBreakLines(RASGeometry geometry)
		: base("Breaklines", geometry.SourceFilename)
	{
		HDFGroupName = "Geometry/2D Flow Area Break Lines";
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "Breakline";
		CanRename = false;
		NearSpacingColumnName = "Near Spacing";
		NearSpacingDiskName = "Cell Spacing Near";
		FarSpacingColumnName = "Far Spacing";
		FarSpacingDiskName = "Cell Spacing Far";
		NearRepeatsColumnName = "Near Repeats";
		NearRepeatsDiskName = "Near Repeats";
		FeaturesRequireUniqueNames = true;
		Geometry = geometry;
	}

	public RASBreakLines(DataTable overrideDT)
		: base("Breaklines")
	{
		HDFGroupName = "Geometry/2D Flow Area Break Lines";
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "Breakline";
		CanRename = false;
		NearSpacingColumnName = "Near Spacing";
		NearSpacingDiskName = "Cell Spacing Near";
		FarSpacingColumnName = "Far Spacing";
		FarSpacingDiskName = "Cell Spacing Far";
		NearRepeatsColumnName = "Near Repeats";
		NearRepeatsDiskName = "Near Repeats";
		FeaturesRequireUniqueNames = true;
		OverrideFeatureTable(overrideDT.Copy());
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		DataColumnGroup dataColumnGroup = new DataColumnGroup("Node Information", isVisible: true);
		AddManagedAttributeColumn("Name", "Name", typeof(string), dataColumnGroup);
		AddAttributeColumn("IsDefaultName", typeof(bool), null, DataColumnViewState.Invisible);
		EnforceBreaklineLayerExtensions.InitializeBreaklineColumns(this);
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup(HDFGroupName))
		{
			string hdfGroupNameTerm = HDFGroupName + "/Attributes";
			if (!hr.ExistsAsDataset(hdfGroupNameTerm))
			{
				HDF5Storage.ReadPolyFeatures(this, hr, HDFGroupName + "/Polylines", sanitizeDuplicatePoints: true);
				HDFLoadFeatureTable_503(hr);
			}
			else
			{
				HDF5Storage.ReadPolyFeatures(this, hr, HDFGroupName + "/Polyline", sanitizeDuplicatePoints: true);
				ReadManagedColumns(hr, hdfGroupNameTerm);
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
		HDF5Storage.ReadStringColumn(this, hr, HDFGroupName + "/Names", "Name");
		HDF5Storage.ReadColumns(this, hr, HDFGroupName + "/Cell Spacing", "Near Spacing", "Far Spacing");
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete(HDFGroupName);
			return;
		}
		hw.CreateGroup(HDFGroupName);
		HDF5Storage.WritePolylineFeatures(this, hw, HDFGroupName + "/Polyline");
		WriteManagedColumns(hw, HDFGroupName + "/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public override object Clone()
	{
		RASBreakLines rASBreakLines = new RASBreakLines(FeatureTable());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = Feature(i);
				if (feature is IEditableFeature)
				{
					rASBreakLines.SetFeature(i, ((IEditableFeature)feature)?.Save());
				}
			}
			return rASBreakLines;
		}
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureAdded)
		{
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				SetupNewBreakline(featureIndicesChanged);
				Geometry?.D2FlowArea.NotifyMeshInputsChanged(Polyline(featureIndicesChanged));
			}
			return;
		}
		if (args.ChangeType == FeatureChangedType.FeatureModified)
		{
			foreach (int featureIndicesChanged2 in args.FeatureIndicesChanged)
			{
				Geometry?.D2FlowArea.NotifyMeshInputsChanged(Polyline(featureIndicesChanged2));
			}
			return;
		}
		if (args.ChangeType != FeatureChangedType.FeatureDeleted)
		{
			return;
		}
		foreach (Feature featuresDeleted in args.FeaturesDeleted)
		{
			Geometry?.D2FlowArea.NotifyMeshInputsChanged(featuresDeleted as Polyline);
		}
	}

	private void SetupNewBreakline(int blIdx)
	{
		SetUniqueFeatureName(blIdx);
		SetIsDefaultName(blIdx, isDefaultName: true);
		EnforceBreaklineLayerExtensions.SetEnforceOneCellProtectionRadius(this, blIdx, enforceOneCellProtectionRadius: true);
	}

	public override void SetFeatureName(int fid, string newName)
	{
		FeatureRow(fid)["Name"] = newName;
		SetIsDefaultName(fid, isDefaultName: false);
	}

	public bool GetIsDefaultName(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "IsDefaultName");
	}

	public void SetIsDefaultName(int fid, bool isDefaultName)
	{
		FeatureRow(fid)["IsDefaultName"] = isDefaultName;
	}
}
