using Utility.Attributes;

namespace Ras.Layers;

public enum MetInterpolationType
{
	[Alias("Nearest")]
	Nearest,
	[Alias("Inv Distance Sq")]
	InverseDistanceSquared,
	[Alias("Inv Distance Sq (Restricted)")]
	InverseDistanceSquaredRestricted,
	[Alias("Bilinear")]
	Bilinear
}
