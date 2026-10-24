using System;

namespace Utility.NumericalAnalysis;

[Flags]
public enum InstabilityKind
{
	None = 0,
	Oscillation = 1,
	Magnitude = 2,
	Divergence = 4,
	NonFinite = 8,
	NegativeDepth = 0x10,
	FroudeExceeded = 0x20
}
