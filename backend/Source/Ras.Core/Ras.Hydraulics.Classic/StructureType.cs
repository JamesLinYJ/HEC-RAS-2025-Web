using Utility.Attributes;

namespace Ras.Hydraulics.Classic;

public enum StructureType
{
	Unknown,
	[Alias("Lateral")]
	Lateral,
	[Alias("Bridge")]
	Bridge,
	[Alias("Inline")]
	Inline,
	[Alias("Connection")]
	Connection,
	[Alias("Culvert")]
	Culvert
}
