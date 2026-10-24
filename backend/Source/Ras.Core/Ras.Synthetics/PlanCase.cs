using System;
using Ras.Engine;
using Ras.Layers;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public sealed class PlanCase
{
	public string PlanName { get; init; }

	public BaseParams Params { get; init; }

	public string TerrainGroupKey { get; init; }

	public string GeometryGroupKey { get; init; }

	public string BoundaryConditionGroupKey { get; init; }

	public string BoundaryConditionName { get; init; }

	public Action<Ras.Layers.BoundaryCondition> ConfigureBoundaryCondition { get; init; }

	public string AnalyticalGroupKey { get; init; }

	public PlanTimeWindowMode TimeWindowMode { get; init; } = PlanTimeWindowMode.Plan;

	public SolverControl.EquationSet EquationSet { get; init; }

	public SolverControl.TimeStepping TimeStepping { get; init; }

	public Action<Plan> ConfigurePlan { get; init; }

	public Action<SolverControl> ConfigureSolverControl { get; init; }
}
