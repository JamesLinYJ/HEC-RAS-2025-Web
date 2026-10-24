// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public sealed class PlanEmitOptions
{
	public bool Overwrite { get; set; }

	public bool NameEmbeddedPlan { get; set; } = true;

	public bool WriteManifest { get; set; } = true;
}
