namespace Testing.Core.Regression;

public class PlanSolver
{
	public string Name { get; set; } = "";

	public bool RequiresGpu { get; set; }

	public PlanSolver()
	{
	}

	public PlanSolver(string name, bool requiresGpu)
	{
		Name = name ?? "";
		RequiresGpu = requiresGpu;
	}
}
