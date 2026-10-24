// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
using System.Collections.Generic;

namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public sealed class PlanSweepOptions
{
	public IReadOnlyList<PlanCombinationRule> IllegalRules { get; set; } = PlanSweepRules.Illegal;

	public bool CollapseIrrelevant { get; set; } = true;
}
