// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
using System.Collections.Generic;

namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public sealed class PlanSweepCombination
{
	public IReadOnlyList<PlanSweepAssignment> Assignments { get; init; }

	public string Filename { get; init; }

	public string Label { get; init; }

	public string ExclusionReason { get; init; }

	public bool IsCollapsed { get; init; }

	public bool IsExcluded => !string.IsNullOrEmpty(ExclusionReason);
}
