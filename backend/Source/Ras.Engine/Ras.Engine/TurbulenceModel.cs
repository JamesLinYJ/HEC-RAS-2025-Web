using Utility.Attributes;

namespace Ras.Engine;

public enum TurbulenceModel
{
	[EnumDisplayName("Parabolic-Smagorinsky")]
	Smagorinsky,
	[EnumDisplayName("Standard k-epsilon")]
	KEpsilon,
	[EnumDisplayName("Prandtl k-l")]
	PrandtlKL
}
