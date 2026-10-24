// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public readonly struct PlanEmitOutcome
{
	public PlanEmitStatus Status { get; init; }

	public string FilePath { get; init; }

	public long FileSizeBytes { get; init; }

	public string Message { get; init; }
}
