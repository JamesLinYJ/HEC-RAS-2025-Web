using System.IO;
using System.Runtime.CompilerServices;

namespace RasMapperLib;

public class IneffectiveFlowAreaLayer : ObstructionAreaLayer
{
	[CompilerGenerated]
	private string _SimpleFeaturetypeName;

	public override string HDFGroupName { get; }

	public override string SimpleFeatureTypeName
	{
		[CompilerGenerated]
		get
		{
			return _SimpleFeaturetypeName;
		}
		[CompilerGenerated]
		set
		{
			_SimpleFeaturetypeName = value;
		}
	}

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Ineffective Flow Areas";

	public IneffectiveFlowAreaLayer(RASGeometry geometry)
		: base("Ineffective Flow Areas", geometry)
	{
		HDFGroupName = "Geometry/Ineffective Flow Areas";
		SimpleFeatureTypeName = "Ineffective Flow Area";
		ForwardEditRequestsToParent = true;
	}

	public static IneffectiveFlowAreaLayer Stream(BinaryReader br, RASGeometry geom)
	{
		IneffectiveFlowAreaLayer ineffectiveFlowAreaLayer = new IneffectiveFlowAreaLayer(geom);
		ineffectiveFlowAreaLayer.InitializeEmptyFeatureTable();
		ineffectiveFlowAreaLayer.SuppressTableReload(boolValue: true);
		ineffectiveFlowAreaLayer.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polygon poly = RasMapperLib.Polygon.Stream(br);
				ineffectiveFlowAreaLayer.AddFeature(poly);
			}
			return ineffectiveFlowAreaLayer;
		}
	}

	public bool EnsureIneffectiveFlowAreas()
	{
		NeedToRecompute();
		return true;
	}
}
