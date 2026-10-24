using System;

namespace Ras.Layers.Computes;

internal static class SolveCoreEstimator
{
	public static int EstimateGeometryPrepare(Geometry geom)
	{
		return geom.FlowAreaLayer.EstimateGeometryPrepareCoreCount();
	}

	public static int EstimateBoundaryConditionPrepare(BoundaryCondition bc)
	{
		return 1;
	}

	public static int EstimateSolve(Plan plan)
	{
		if (plan.SolverType == SolverType.GPU)
		{
			return 1;
		}
		int coreCount = plan.CoreCount;
		if (coreCount <= 0)
		{
			coreCount = Plan.DefaultCoreCount;
		}
		return Math.Min(coreCount, Environment.ProcessorCount);
	}
}
