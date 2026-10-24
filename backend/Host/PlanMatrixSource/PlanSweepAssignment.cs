// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public readonly struct PlanSweepAssignment
{
	public PlanSweepProperty Property { get; init; }

	public object Value { get; init; }

	public bool IsRelevant { get; init; }

	public string IrrelevantReason { get; init; }
}
