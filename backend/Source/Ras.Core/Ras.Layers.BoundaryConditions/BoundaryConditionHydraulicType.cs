using Utility.Attributes;

namespace Ras.Layers.BoundaryConditions;

public enum BoundaryConditionHydraulicType
{
	[Alias("Stage")]
	[EnumDisplayName("Stage")]
	Stage,
	[Alias("Flow")]
	[EnumDisplayName("Flow")]
	Flow,
	[Alias("Normal Depth")]
	[EnumDisplayName("Normal Depth")]
	NormalDepth,
	[Alias("Stage/Flow")]
	[EnumDisplayName("Stage / Flow")]
	StageAndFlow,
	[Alias("Variable Stage")]
	[EnumDisplayName("Variable Stage")]
	VariableStage,
	[Alias("Unknown")]
	[EnumDisplayName("Unassigned")]
	Unknown
}
