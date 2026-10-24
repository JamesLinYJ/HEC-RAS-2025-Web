namespace Ras.Engine.Structures.Culverts;

public class CulvertFlowSolverResult
{
	public CulvertSolution Solution { get; }

	public CulvertSolverConvergence Convergence { get; }

	public CulvertFlowSolverResult(CulvertSolution solution, CulvertSolverConvergence convergence)
	{
		Solution = solution;
		Convergence = convergence;
	}
}
