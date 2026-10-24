namespace Ras.Layers.Computes;

public readonly struct PlanSolveRequest
{
	public Plan Plan { get; }

	public string OutputFilename { get; }

	public PlanSolveRequest(Plan plan, string outputFilename)
	{
		Plan = plan;
		OutputFilename = outputFilename;
	}
}
