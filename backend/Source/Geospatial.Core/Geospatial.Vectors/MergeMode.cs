using Utility.Attributes;

namespace Geospatial.Vectors;

public enum MergeMode
{
	[EnumDisplayName("Reject Branching")]
	RejectBranching,
	[EnumDisplayName("Prefer Closest")]
	PreferClosest
}
