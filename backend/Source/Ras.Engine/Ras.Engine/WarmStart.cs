using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine.Structures;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Engine;

public static class WarmStart
{
	private static void ComputeRampCurve(double floor, double target, double warmUpPeriod, double warmUpFraction, out double[] timesteps, out double[] values)
	{
		if (warmUpFraction <= 0.0)
		{
			timesteps = new double[2] { 0.0, warmUpPeriod };
			values = new double[2] { target, target };
		}
		else if (warmUpFraction >= 1.0)
		{
			timesteps = new double[2] { 0.0, warmUpPeriod };
			values = new double[2] { floor, target };
		}
		else
		{
			double warmUpFractionTimesWarmUpPeriod = warmUpFraction * warmUpPeriod;
			timesteps = new double[3] { 0.0, warmUpFractionTimesWarmUpPeriod, warmUpPeriod };
			values = new double[3] { floor, target, target };
		}
	}

	private static BoundaryCondition BuildRampedFaceBC(BoundaryCondition fullSimBC, double floor, double warmUpPeriod, double warmUpFraction)
	{
		double target = fullSimBC.ValueAtTime(0.0);
		BoundaryCondition boundaryCondition = fullSimBC.ShallowCopy();
		ComputeRampCurve(floor, target, warmUpPeriod, warmUpFraction, out var timesteps, out var values);
		boundaryCondition.Timesteps = timesteps;
		boundaryCondition.Values = values;
		return boundaryCondition;
	}

	private static BoundaryCondition BuildRampedFaceMultipleValuesBC(BoundaryCondition fullSimBC, MeshPropertyTables props, double warmUpPeriod, double warmUpFraction)
	{
		if (fullSimBC.Faces == null || fullSimBC.Faces.Length == 0)
		{
			throw new InvalidOperationException("Warm start: a Spatially Variable Stage/Velocity boundary condition has no faces.");
		}
		if (fullSimBC.FaceMultipleValues == null)
		{
			throw new InvalidOperationException("Warm start: a Spatially Variable Stage/Velocity boundary condition has no FaceMultipleValues.");
		}
		int length = fullSimBC.FaceMultipleValues.GetLength(1);
		int length2 = fullSimBC.FaceMultipleValues.GetLength(2);
		if (length != fullSimBC.Faces.Length)
		{
			throw new InvalidOperationException($"Warm start: Spatially Variable Stage/Velocity boundary condition has {fullSimBC.Faces.Length} faces but {length} face value columns.");
		}
		double[,] sourceArray = fullSimBC.FaceMultipleValuesAtTime(0.0);
		double[,] doubleValues = new double[length, length2];
		Array.Copy(sourceArray, doubleValues, doubleValues.Length);
		ComputeRampCurve(0.0, 1.0, warmUpPeriod, warmUpFraction, out var timesteps, out var values);
		double[,,] faceMultipleValues = new double[values.Length, length, length2];
		for (int i = 0; i < length; i++)
		{
			for (int j = 0; j < length2; j++)
			{
				double faceMinimumElevation = ((j == 0) ? ((double)props.FaceMinimumElevations[fullSimBC.Faces[i]]) : 0.0);
				for (int k = 0; k < values.Length; k++)
				{
					faceMultipleValues[k, i, j] = faceMinimumElevation + (doubleValues[i, j] - faceMinimumElevation) * values[k];
				}
			}
		}
		BoundaryCondition boundaryCondition = fullSimBC.ShallowCopy();
		boundaryCondition.Timesteps = timesteps;
		boundaryCondition.FaceMultipleValues = faceMultipleValues;
		boundaryCondition.Values = null;
		return boundaryCondition;
	}

	private static BoundaryCondition BuildRampedInternalFlowBC(BoundaryCondition fullSimBC, double warmUpPeriod, double warmUpFraction)
	{
		double target = fullSimBC.ValueAtTime(0.0);
		ComputeRampCurve(0.0, target, warmUpPeriod, warmUpFraction, out var timesteps, out var values);
		int cellLength = fullSimBC.Cells.Length;
		double[,] cellValues = new double[values.Length, cellLength];
		for (int i = 0; i < values.Length; i++)
		{
			for (int j = 0; j < cellLength; j++)
			{
				cellValues[i, j] = values[i] * fullSimBC.CellFractions[j];
			}
		}
		return new BoundaryCondition
		{
			Name = fullSimBC.Name,
			Cells = fullSimBC.Cells,
			CellFractions = fullSimBC.CellFractions,
			Timesteps = timesteps,
			Values = values,
			CellValues = cellValues
		};
	}

	private static void ValidateSupported(BoundaryConditionCollection bcs)
	{
		if (bcs.VariableFlowBoundaryConditions.Count > 0)
		{
			throw new NotImplementedException($"Warm start does not support {"VariableFlowBoundaryConditions"} yet ({bcs.VariableFlowBoundaryConditions.Count} present).");
		}
		if (bcs.VariableStageBoundaryConditions.Count > 0)
		{
			throw new NotImplementedException($"Warm start does not support {"VariableStageBoundaryConditions"} yet ({bcs.VariableStageBoundaryConditions.Count} present).");
		}
	}

	public static BoundaryConditionCollection BuildWarmUpBoundaryConditions(Mesh mesh, MeshPropertyTables props, BoundaryConditionCollection fullSimBCColl, double warmUpPeriod, double warmUpFraction)
	{
		ValidateSupported(fullSimBCColl);
		BoundaryConditionCollection boundaryConditionCollection = new BoundaryConditionCollection(mesh);
		foreach (BoundaryCondition flowBoundaryCondition in fullSimBCColl.FlowBoundaryConditions)
		{
			boundaryConditionCollection.FlowBoundaryConditions.Add(BuildRampedFaceBC(flowBoundaryCondition, 0.0, warmUpPeriod, warmUpFraction));
		}
		foreach (BoundaryCondition stageBoundaryCondition in fullSimBCColl.StageBoundaryConditions)
		{
			if (stageBoundaryCondition.Faces == null || stageBoundaryCondition.Faces.Length == 0)
			{
				throw new InvalidOperationException("Warm start: a Stage boundary condition has no faces.");
			}
			double floor = ((IEnumerable<int>)stageBoundaryCondition.Faces).Min((Func<int, double>)((int f) => props.FaceMinimumElevations[f]));
			boundaryConditionCollection.StageBoundaryConditions.Add(BuildRampedFaceBC(stageBoundaryCondition, floor, warmUpPeriod, warmUpFraction));
		}
		foreach (BoundaryCondition spatiallyVariableStageVelocityBoundaryCondition in fullSimBCColl.SpatiallyVariableStageVelocityBoundaryConditions)
		{
			boundaryConditionCollection.SpatiallyVariableStageVelocityBoundaryConditions.Add(BuildRampedFaceMultipleValuesBC(spatiallyVariableStageVelocityBoundaryCondition, props, warmUpPeriod, warmUpFraction));
		}
		boundaryConditionCollection.NormalDepthBoundaryConditions.AddRange(fullSimBCColl.NormalDepthBoundaryConditions);
		foreach (BoundaryCondition internalFlowBoundaryCondition in fullSimBCColl.InternalFlowBoundaryConditions)
		{
			boundaryConditionCollection.InternalFlowBoundaryConditions.Add(BuildRampedInternalFlowBC(internalFlowBoundaryCondition, warmUpPeriod, warmUpFraction));
		}
		return boundaryConditionCollection;
	}

	public static MeshWaterState RunWarmUp(Mesh mesh, MeshPropertyTables props, BoundaryConditionCollection fullSimBCs, SolverControl solverControl, double warmUpPeriod, double warmUpFraction, HydraulicStructureCollection hydraulicStructures = null, ProgressReporter pr = null)
	{
		if (warmUpPeriod <= 0.0)
		{
			throw new ArgumentOutOfRangeException("warmUpPeriod", warmUpPeriod, "Warm-up period must be positive.");
		}
		if (warmUpFraction < 0.0 || warmUpFraction > 1.0)
		{
			throw new ArgumentOutOfRangeException("warmUpFraction", warmUpFraction, "Warm-up fraction must be between 0 and 1.");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		MeshWaterState meshWaterState = new MeshWaterState(mesh, props);
		meshWaterState.SetDry();
		BoundaryConditionCollection boundaryConditionCollection = BuildWarmUpBoundaryConditions(mesh, props, fullSimBCs, warmUpPeriod, warmUpFraction);
		ComputeDriver computeDriver = new ComputeDriver(mesh, props, meshWaterState, solverControl.computeControl, solverControl);
		computeDriver.Reporter = pr;
		computeDriver.BoundaryConditions.FlowBoundaryConditions.AddRange(boundaryConditionCollection.FlowBoundaryConditions);
		computeDriver.BoundaryConditions.StageBoundaryConditions.AddRange(boundaryConditionCollection.StageBoundaryConditions);
		computeDriver.BoundaryConditions.NormalDepthBoundaryConditions.AddRange(boundaryConditionCollection.NormalDepthBoundaryConditions);
		computeDriver.BoundaryConditions.InternalFlowBoundaryConditions.AddRange(boundaryConditionCollection.InternalFlowBoundaryConditions);
		computeDriver.BoundaryConditions.SpatiallyVariableStageVelocityBoundaryConditions.AddRange(boundaryConditionCollection.SpatiallyVariableStageVelocityBoundaryConditions);
		if (hydraulicStructures != null)
		{
			computeDriver.HydraulicStructures = hydraulicStructures;
		}
		OperationResult operationResult = computeDriver.Solve(warmUpPeriod);
		if (!operationResult.Result)
		{
			throw new StructuredException(operationResult);
		}
		return computeDriver.ConstructGlobalState();
	}

	public static void ApplyWarmStart(ComputeDriver fullSimDriver, double warmUpPeriod, double warmUpFraction, ProgressReporter pr = null)
	{
		if (!fullSimDriver.IdentityMapping)
		{
			throw new NotSupportedException("Warm start currently supports only single-partition (identity-mapping) ComputeDriver instances.");
		}
		ISolver solver = fullSimDriver.GetSolver(0);
		MeshWaterState meshWaterState = RunWarmUp(fullSimDriver.GlobalMesh, fullSimDriver.PropertyTables, fullSimDriver.BoundaryConditions, solver.Control, warmUpPeriod, warmUpFraction, fullSimDriver.HydraulicStructures, pr);
		solver.MaybeGetMeshWaterState().SetInitialFromRestartVars(meshWaterState.CellElevation, meshWaterState.FaceVelocity, meshWaterState.dtMicros);
	}
}
