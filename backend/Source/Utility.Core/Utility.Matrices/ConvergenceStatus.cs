namespace Utility.Matrices;

public enum ConvergenceStatus
{
	SmallNorm = 3,
	Iterating = 1,
	Converged = 0,
	MaxIterations = -1,
	Divergent = -2,
	Crashed = -3
}
