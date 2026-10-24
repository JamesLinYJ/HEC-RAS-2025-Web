using Utility.Attributes;

namespace Ras.Hydraulics.Classic;

public enum StructureConnectionType
{
	Unknown,
	[Alias("XS")]
	CrossSection,
	[Alias("SA")]
	StorageArea,
	[Alias("2D")]
	FlowArea
}
