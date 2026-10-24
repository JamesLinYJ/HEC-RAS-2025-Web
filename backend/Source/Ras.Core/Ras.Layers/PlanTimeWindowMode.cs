using Utility.Attributes;

namespace Ras.Layers;

public enum PlanTimeWindowMode
{
	[EnumDisplayName("Boundary Condition")]
	BoundaryCondition,
	[EnumDisplayName("Custom")]
	Plan
}
