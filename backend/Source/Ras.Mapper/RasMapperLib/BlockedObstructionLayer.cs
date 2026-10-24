using System.IO;
using System.Runtime.CompilerServices;

namespace RasMapperLib;

public class BlockedObstructionLayer : ObstructionAreaLayer
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

	public override string DefaultLayerName => "Blocked Obstructions";

	public BlockedObstructionLayer(RASGeometry geometry)
		: base("Blocked Obstructions", geometry)
	{
		HDFGroupName = "Geometry/Blocked Obstruction";
		SimpleFeatureTypeName = "Blocked Obstruction";
		ForwardEditRequestsToParent = true;
	}

	public static BlockedObstructionLayer Stream(BinaryReader br, RASGeometry geom)
	{
		BlockedObstructionLayer blockedObstructionLayer = new BlockedObstructionLayer(geom);
		blockedObstructionLayer.InitializeEmptyFeatureTable();
		blockedObstructionLayer.SuppressTableReload(boolValue: true);
		blockedObstructionLayer.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polygon poly = RasMapperLib.Polygon.Stream(br);
				blockedObstructionLayer.AddFeature(poly);
			}
			return blockedObstructionLayer;
		}
	}

	public bool EnsureBlockedObstructions()
	{
		NeedToRecompute();
		return true;
	}
}
