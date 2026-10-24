using Utility.NumericalAnalysis;

namespace Ras.Engine;

public sealed class StabilityEvent
{
	public StabilitySeverity Severity { get; init; }

	public InstabilityKind Kind { get; init; }

	public string Location { get; init; }

	public string Variable { get; init; }

	public double Time { get; init; }

	public double Value { get; init; }

	public string Message { get; init; }
}
