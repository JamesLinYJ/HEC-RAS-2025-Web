using Utility.Attributes;

namespace Geospatial.Rasters;

public enum VectorModShape
{
	[EnumDisplayName("Triangle")]
	Triangle,
	[EnumDisplayName("Square")]
	Square,
	[EnumDisplayName("Rectangle")]
	Rectangle,
	[EnumDisplayName("Circle")]
	Circle,
	[EnumDisplayName("Ellipse")]
	Ellipse,
	[EnumDisplayName("High Ground")]
	HighGround,
	[EnumDisplayName("Channel")]
	Channel,
	[EnumDisplayName("Custom")]
	Custom
}
