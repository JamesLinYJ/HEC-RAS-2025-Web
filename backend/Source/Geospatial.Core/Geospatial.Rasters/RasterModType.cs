using Utility.Attributes;

namespace Geospatial.Rasters;

public enum RasterModType
{
	[EnumDisplayName("Set Value")]
	SetValue,
	[EnumDisplayName("Take Higher")]
	TakeHigher,
	[EnumDisplayName("Take Lower")]
	TakeLower,
	[EnumDisplayName("Add Value")]
	AddValue
}
