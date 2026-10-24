using Utility.Attributes;

namespace Ras.Engine;

public enum AdvectionScheme
{
	[EnumDisplayName("First-Order Upwind")]
	FirstOrderUpwind,
	[EnumDisplayName("TVD (Second-Order Upwind + Flux Limiter)")]
	TVD
}
