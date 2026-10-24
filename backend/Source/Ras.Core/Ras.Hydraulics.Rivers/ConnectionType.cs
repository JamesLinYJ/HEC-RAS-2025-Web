using Utility.Attributes;

namespace Ras.Hydraulics.Rivers;

public enum ConnectionType
{
	[Alias("None")]
	None,
	[Alias("2D Flow Area")]
	FlowArea,
	[Alias("Internal")]
	Internal,
	[Alias("External")]
	External
}
