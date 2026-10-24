using Utility.Attributes;

namespace Ras.Engine;

public enum FluxLimiter
{
	[EnumDisplayName("Min-Mod")]
	MinMod,
	[EnumDisplayName("Superbee")]
	Superbee,
	[EnumDisplayName("Van Leer")]
	VanLeer
}
