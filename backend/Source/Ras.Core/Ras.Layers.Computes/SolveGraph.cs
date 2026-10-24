using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Hydraulics;
using Utility.Logging;
using Utility.Progress;
using Utility.Scheduling;

namespace Ras.Layers.Computes;

public static class SolveGraph
{
	public static JobGraph Build(IEnumerable<PlanSolveRequest> requests)
	{
		List<PlanSolveRequest> requestsList = requests.ToList();
		Dictionary<object, ScheduledJob> instance = new Dictionary<object, ScheduledJob>(ReferenceEqualityComparer.Instance);
		Dictionary<object, ScheduledJob> instance2 = new Dictionary<object, ScheduledJob>(ReferenceEqualityComparer.Instance);
		List<ScheduledJob> scheduledJobs = new List<ScheduledJob>();
		foreach (PlanSolveRequest r in requestsList)
		{
			Plan plan = r.Plan;
			Geometry geom = plan.Geometry;
			BoundaryCondition bc = plan.BoundaryCondition;
			ScheduledJob scheduledJob4 = null;
			if (geom != null && !instance.TryGetValue(geom, out scheduledJob4))
			{
				scheduledJob4 = new ScheduledJob("PrepareGeometry", GeometryIdentity(geom), "Prepare Geometry \"" + geom.Name + "\"", Cost("cpu", SolveCoreEstimator.EstimateGeometryPrepare(geom)), (ProgressReporter pr) => geom.PrepareForCompute(pr), geom);
				instance[geom] = scheduledJob4;
				scheduledJobs.Add(scheduledJob4);
			}
			ScheduledJob scheduledJob5 = null;
			if (bc != null && !instance2.TryGetValue(bc, out scheduledJob5))
			{
				scheduledJob5 = new ScheduledJob("PrepareBoundaryCondition", BoundaryConditionIdentity(bc), "Prepare Boundary Conditions \"" + bc.Name + "\"", Cost("cpu", SolveCoreEstimator.EstimateBoundaryConditionPrepare(bc)), (ProgressReporter pr) => bc.PrepareForCompute(pr), bc);
				instance2[bc] = scheduledJob5;
				scheduledJobs.Add(scheduledJob5);
			}
			string filename = plan.Name ?? plan.Filename ?? Guid.NewGuid().ToString();
			MeshWaterState icState = null;
			ScheduledJob scheduledJob = new ScheduledJob("BuildInitialConditions", "IC:" + filename, "Initial Conditions for \"" + plan.Name + "\"", Cost("cpu", 1), (ProgressReporter pr) =>
			{
				if (geom == null || bc == null)
				{
					return OperationResult.Fail("Plan \"" + plan.Name + "\" missing geometry or boundary condition.");
				}
				FlowArea flowArea = geom.FlowAreaLayer[0];
				Mesh mesh = flowArea.Mesh;
				MeshPropertyTables propertyTables = flowArea.PropertyTables;
				if (propertyTables == null)
				{
					return OperationResult.Fail("Plan \"" + plan.Name + "\": property tables not available after geometry prepare.");
				}
				icState = new MeshWaterState(mesh, propertyTables);
				bc.ApplyInitialWaterState(mesh, geom.AssociatedTerrain, icState, pr);
				return OperationResult.Success();
			}, plan);
			if (scheduledJob4 != null)
			{
				scheduledJob.AddDependency(scheduledJob4);
			}
			if (scheduledJob5 != null)
			{
				scheduledJob.AddDependency(scheduledJob5);
			}
			scheduledJobs.Add(scheduledJob);
			Result preparedResult = null;
			ScheduledJob scheduledJob2 = new ScheduledJob("PrepareResultFile", "PrepResult:" + filename, "Prepare Result File for \"" + plan.Name + "\"", Cost("cpu", 1), (ProgressReporter pr) =>
			{
				if (icState == null)
				{
					return OperationResult.Fail("Plan \"" + plan.Name + "\": initial conditions not built.");
				}
				preparedResult = Result.PrepareResult(r.OutputFilename, plan, geom, bc, icState, pr);
				return (preparedResult == null) ? OperationResult.Fail("Failed to prepare result file.") : OperationResult.Success();
			}, plan);
			scheduledJob2.AddDependency(scheduledJob);
			scheduledJobs.Add(scheduledJob2);
			Dictionary<string, int> intByString = new Dictionary<string, int> { ["cpu"] = SolveCoreEstimator.EstimateSolve(plan) };
			if (plan.SolverType == SolverType.GPU)
			{
				intByString["gpu"] = 1;
			}
			ScheduledJob scheduledJob3 = new ScheduledJob("Solve", "Solve:" + filename, "Solve \"" + plan.Name + "\"", intByString, (ProgressReporter pr) => (preparedResult == null) ? OperationResult.Fail("Plan \"" + plan.Name + "\": no prepared result to solve against.") : preparedResult.Compute((Action a) =>
			{
				a();
			}, pr), plan);
			scheduledJob3.AddDependency(scheduledJob2);
			scheduledJobs.Add(scheduledJob3);
		}
		return new JobGraph(scheduledJobs);
	}

	private static Dictionary<string, int> Cost(string resource, int amount)
	{
		return new Dictionary<string, int> { [resource] = amount };
	}

	private static string GeometryIdentity(Geometry g)
	{
		if (!string.IsNullOrEmpty(g.Filename))
		{
			return Path.GetFullPath(g.Filename);
		}
		return g.Name ?? "<unnamed-geom>";
	}

	private static string BoundaryConditionIdentity(BoundaryCondition bc)
	{
		if (!string.IsNullOrEmpty(bc.Filename))
		{
			return Path.GetFullPath(bc.Filename);
		}
		return bc.Name ?? "<unnamed-bc>";
	}
}
