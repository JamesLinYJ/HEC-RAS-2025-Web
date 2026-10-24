using System.Data;
using H5Assist.VB;

namespace RasMapperLib;

public class MeshRegionLayer : MeshPolygonLayer, IEnforceBreaklineLayer
{
	public const string sPerimeterSpacing = "Perimeter Spacing";

	public const string sNearRepeats = "Near Repeats";

	public const string sFarSpacing = "Far Spacing";

	public const string DefaultHDFGroupName = "Geometry/2D Flow Area Refinement Regions";

	public override string HDFGroupName { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override string DefaultLayerName => "Refinement Regions";

	public string NearSpacingColumnName { get; }

	public string NearSpacingDiskName { get; }

	public string FarSpacingColumnName { get; }

	public string FarSpacingDiskName { get; }

	public string NearRepeatsColumnName { get; }

	public string NearRepeatsDiskName { get; }

	public override bool FeaturesRequireUniqueNames { get; set; }

	public MeshRegionLayer(RASGeometry geometry)
		: base("Refinement Regions", geometry)
	{
		HDFGroupName = "Geometry/2D Flow Area Refinement Regions";
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "Region";
		NearSpacingColumnName = "Perimeter Spacing";
		NearSpacingDiskName = "Perimeter Spacing";
		FarSpacingColumnName = "Far Spacing";
		FarSpacingDiskName = "Far Spacing";
		NearRepeatsColumnName = "Near Repeats";
		NearRepeatsDiskName = "Near Spacing Repeats";
		FeaturesRequireUniqueNames = true;
	}

	public MeshRegionLayer(DataTable overrideDT)
		: base("Refinement Regions")
	{
		HDFGroupName = "Geometry/2D Flow Area Refinement Regions";
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "Region";
		NearSpacingColumnName = "Perimeter Spacing";
		NearSpacingDiskName = "Perimeter Spacing";
		FarSpacingColumnName = "Far Spacing";
		FarSpacingDiskName = "Far Spacing";
		NearRepeatsColumnName = "Near Repeats";
		NearRepeatsDiskName = "Near Spacing Repeats";
		FeaturesRequireUniqueNames = true;
		OverrideFeatureTable(overrideDT.Copy());
	}

	public override object Clone()
	{
		MeshRegionLayer meshRegionLayer = new MeshRegionLayer(FeatureTable());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = Feature(i);
				if (feature is IEditableFeature)
				{
					meshRegionLayer.SetFeature(i, ((IEditableFeature)feature)?.Save());
				}
			}
			return meshRegionLayer;
		}
	}

	public static void Migrate(H5Reader h5r, H5Writer h5w)
	{
		if (h5r.ExistsAsGroup("Geometry/2D Flow Area Refinement Regions"))
		{
			if (h5w.ExistsAsGroup("Geometry/2D Flow Area Refinement Regions"))
			{
				h5w.Delete("Geometry/2D Flow Area Refinement Regions");
			}
			h5r.CopyTo(h5w, "Geometry/2D Flow Area Refinement Regions", "Geometry/2D Flow Area Refinement Regions");
		}
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		EnforceBreaklineLayerExtensions.InitializeBreaklineColumns(this);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureAdded)
		{
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				SetupNewRegion(featureIndicesChanged);
				Geometry?.D2FlowArea.NotifyMeshInputsChanged(Polygon(featureIndicesChanged));
			}
			return;
		}
		if (args.ChangeType == FeatureChangedType.FeatureModified)
		{
			foreach (int featureIndicesChanged2 in args.FeatureIndicesChanged)
			{
				Geometry?.D2FlowArea.NotifyMeshInputsChanged(Polygon(featureIndicesChanged2));
			}
			return;
		}
		if (args.ChangeType != FeatureChangedType.FeatureDeleted)
		{
			return;
		}
		foreach (Feature featuresDeleted in args.FeaturesDeleted)
		{
			Geometry?.D2FlowArea.NotifyMeshInputsChanged(featuresDeleted as Polygon);
		}
	}

	private void SetupNewRegion(int idx)
	{
		SetUniqueFeatureName(idx);
		SetIsDefaultName(idx, isDefaultName: true);
		EnforceBreaklineLayerExtensions.SetEnforceOneCellProtectionRadius(this, idx, enforceOneCellProtectionRadius: true);
	}

	public override void SetFeatureName(int fid, string newName)
	{
		FeatureRow(fid)["Name"] = newName;
		SetIsDefaultName(fid, isDefaultName: false);
	}
}
