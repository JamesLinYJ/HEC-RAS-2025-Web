using System.ComponentModel;
using Utility.Attributes;

namespace Geospatial.Rasters;

public enum RoundTo
{
	[EnumDisplayName("None")]
	[Description("None")]
	None,
	[EnumDisplayName("1/10")]
	[Description("1/10")]
	Tenths,
	[EnumDisplayName("1/100")]
	[Description("1/100")]
	Hundredths,
	[EnumDisplayName("1/1000")]
	[Description("1/1000")]
	Thousandths,
	[EnumDisplayName("1/16")]
	[Description("1/16")]
	Sixteenths,
	[EnumDisplayName("1/32")]
	[Description("1/32")]
	ThirtySeconds,
	[EnumDisplayName("1/64")]
	[Description("1/64")]
	SixtyFourths,
	[EnumDisplayName("1/128")]
	[Description("1/128")]
	HundredTwentyEighths,
	[EnumDisplayName("1/1024")]
	[Description("1/1024")]
	ThousandTwentyFourths
}
