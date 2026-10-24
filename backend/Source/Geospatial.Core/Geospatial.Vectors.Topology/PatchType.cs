using Utility.Attributes;

namespace Geospatial.Vectors.Topology;

public enum PatchType
{
	[EnumDisplayName("Quad")]
	Quad = 0,
	[EnumDisplayName("Triangular")]
	TrianglePaver = 2,
	[EnumDisplayName("Cartesian")]
	Cartesian = 3,
	[EnumDisplayName("1D")]
	OneD = 5,
	[EnumDisplayName("Empty")]
	Empty = 4
}
