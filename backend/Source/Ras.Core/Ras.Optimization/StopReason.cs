namespace Ras.Optimization;

public enum StopReason
{
	None,
	StepTolerance,
	MaxEvaluations,
	ObjectiveTolerance,
	MaxWallClock,
	Cancelled,
	Exhausted
}
