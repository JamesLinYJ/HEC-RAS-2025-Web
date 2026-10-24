using System.Collections.Generic;

namespace Testing.Core.Regression;

public class ModelRepoProject : ModelRepoObject
{
	public List<InterestPoint> InterestPoints { get; set; } = new List<InterestPoint>();

	public List<PlanSolver> Plans { get; set; } = new List<PlanSolver>();
}
