using Utility.Attributes;

namespace Ras.Hydraulics.Culverts;

public enum BarrelShape
{
	[Alias("Circle")]
	Circle,
	[Alias("Box")]
	Box,
	[Alias("Ellipse")]
	Ellipse,
	[Alias("Custom")]
	Custom
}
