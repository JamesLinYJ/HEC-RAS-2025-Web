using Utility.Attributes;

namespace Ras.Layers;

public enum SpatialDataType
{
	[Alias("Constant")]
	Constant,
	[Alias("Point")]
	Point,
	[Alias("Grid")]
	Gridded
}
