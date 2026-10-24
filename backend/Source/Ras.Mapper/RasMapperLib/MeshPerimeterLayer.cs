using H5Assist.VB;

namespace RasMapperLib;

public class MeshPerimeterLayer : MeshPolygonLayer, IVirtualFeatureLayer
{
	private const string sPerimeter = "Perimeter";

	private RASD2FlowArea _meshLayer;

	public override string HDFGroupName { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Perimeters";

	public FeatureLayer Layer => this;

	public FeatureLayer SourceLayer => _meshLayer;

	public override string FeatureColumnName { get; }

	public string CountAttributeName => "";

	public override bool FeaturesRequireUniqueNames { get; set; }

	public MeshPerimeterLayer(RASD2FlowArea meshLyr)
		: base("Perimeters", meshLyr.Geometry)
	{
		HDFGroupName = "Geometry/2D Flow Areas";
		SimpleFeatureTypeName = "Perimeter";
		ForwardEditRequestsToParent = true;
		FeatureColumnName = "Perimeter";
		FeaturesRequireUniqueNames = true;
		_meshLayer = meshLyr;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Perimeter", typeof(Polygon));
	}

	public override void HDFSaveFeatureTable(H5Writer hw)
	{
		HDFSaveFeatureTable_503(hw);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureAdded)
		{
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				SetupNewPerimeter(featureIndicesChanged);
			}
			return;
		}
		if (args.ChangeType != FeatureChangedType.FeatureModified)
		{
			return;
		}
		foreach (int featureIndicesChanged2 in args.FeatureIndicesChanged)
		{
			Geometry?.D2FlowArea.NotifyMeshInputsChanged(featureIndicesChanged2);
		}
	}

	protected override void ResetFeatureAttributes(int fid)
	{
		base.ResetFeatureAttributes(fid);
	}

	public override void SetFeatureName(int fid, string newName)
	{
		FeatureRow(fid)["Name"] = newName;
		SetIsDefaultName(fid, isDefaultName: false);
	}

	private void SetupNewPerimeter(int fid)
	{
		SetUniqueFeatureName(fid);
		SetIsDefaultName(fid, isDefaultName: true);
	}
}
