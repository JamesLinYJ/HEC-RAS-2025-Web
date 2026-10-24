using Utility.Attributes;

namespace Geospatial.Rasters;

public enum CalculatorNdvBehavior
{
	[EnumDisplayName("Allow (propagate)")]
	Allow,
	[EnumDisplayName("Map as NoData")]
	WriteNDV,
	[EnumDisplayName("Map as Zero")]
	WriteZero
}
