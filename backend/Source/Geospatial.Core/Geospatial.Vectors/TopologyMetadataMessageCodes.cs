using Utility.Logging;

namespace Geospatial.Vectors;

public static class TopologyMetadataMessageCodes
{
	private const string ArcMD = "Metadata error with Arc[{ArcIndex}].";

	public static readonly MessageClass BadArcMetadata_CellCount = new MessageClass("TopoMetadata_ArcCellCount", MessageSeverity.Error, "Metadata error with Arc[{ArcIndex}]. Cell Count must be at least 1.");

	public static readonly MessageClass BadArcMetadata_Bias = new MessageClass("TopoMetadata_ArcBias", MessageSeverity.Warning, "Metadata error with Arc[{ArcIndex}]. Invalid bias <= 0, will use 1.0 instead.");

	public static readonly MessageClass BadArcMetadata_ConstSize = new MessageClass("TopoMetadata_ArcConstSize", MessageSeverity.Error, "Metadata error with Arc[{ArcIndex}]. Constant Size must be greater than 0.");

	public static Message GetBadArcMetadata_CellCount(int arcIdx)
	{
		return BadArcMetadata_CellCount.Create(arcIdx);
	}

	public static Message GetBadArcMetadata_Bias(int arcIdx)
	{
		return BadArcMetadata_Bias.Create(arcIdx);
	}

	public static Message GetBadArcMetadata_ConstSize(int arcIdx)
	{
		return BadArcMetadata_ConstSize.Create(arcIdx);
	}
}
