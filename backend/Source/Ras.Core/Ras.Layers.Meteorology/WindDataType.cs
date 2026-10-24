using Utility.Attributes;

namespace Ras.Layers.Meteorology;

public enum WindDataType
{
	[Alias("VxVy")]
	VxVy,
	[Alias("MagDir")]
	MagDir
}
