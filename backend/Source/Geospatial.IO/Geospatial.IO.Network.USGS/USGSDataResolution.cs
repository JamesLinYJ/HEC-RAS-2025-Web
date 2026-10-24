using Utility.Attributes;

namespace Geospatial.IO.Network.USGS;

public enum USGSDataResolution
{
	Unknown,
	Original,
	[EnumDisplayName("1 Meter")]
	OneMeter,
	[EnumDisplayName("1/9 Arcsec")]
	OneNinthArcSecond,
	[EnumDisplayName("5 Meter")]
	FiveMeter,
	[EnumDisplayName("1/3 Arcsec")]
	OneThirdArcSecond,
	[EnumDisplayName("1 Arcsec")]
	OneArcSecond,
	[EnumDisplayName("2 Arcsec")]
	TwoArcSecond,
	Custom
}
