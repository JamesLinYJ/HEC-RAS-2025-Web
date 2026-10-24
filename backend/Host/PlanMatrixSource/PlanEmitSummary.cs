// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public sealed class PlanEmitSummary
{
	public int Written { get; set; }

	public int Skipped { get; set; }

	public int Failed { get; set; }

	public int Excluded { get; set; }

	public bool WasCancelled { get; set; }

	public string ManifestPath { get; set; }

	public bool WasAborted { get; set; }

	public string AbortReason { get; set; }
}
