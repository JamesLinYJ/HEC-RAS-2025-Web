using Utility.Attributes;

namespace Ras.Hydraulics.HydraulicDesign;

public enum TractiveForceSolutionMethod
{
	[EnumDisplayName("Lane")]
	Lane,
	[EnumDisplayName("Shields")]
	Shields,
	[EnumDisplayName("User Defined Mobility")]
	UserDefinedMobility
}
