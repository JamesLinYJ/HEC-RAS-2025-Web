using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;

namespace Ras.Synthetics.Analysis;

public static class BaseAnalysis
{
	public static List<MeshTimestep> TrackHistory(Solver solver)
	{
		List<MeshTimestep> history = new List<MeshTimestep>();
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			history.Add(mt.Clone());
		});
		return history;
	}

	public static List<(double Time, double Stage)> TrackCellElevation(Solver solver, int cellIdx)
	{
		List<(double, double)> samples = new List<(double, double)>();
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			samples.Add((mt.Time, mt.WaterState.CellElevation[cellIdx]));
		});
		return samples;
	}

	public static List<(double Time, double Velocity)> TrackFaceVelocity(Solver solver, int faceIdx)
	{
		List<(double, double)> samples = new List<(double, double)>();
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			samples.Add((mt.Time, mt.WaterState.FaceVelocity[faceIdx]));
		});
		return samples;
	}

	public static List<(double Time, Vector Velocity)> TrackCellVelocity(Solver solver, int cellIdx)
	{
		List<(double, Vector)> samples = new List<(double, Vector)>();
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			Vector vector;
			if (solver.Control.timeStepping == SolverControl.TimeStepping.Implicit)
			{
				HydraulicMeshComputer hydraulicMeshComputer = solver.HydraulicMeshComputer;
				MeshWaterState waterState = solver.WaterState;
				vector = hydraulicMeshComputer.CellComputes.VelocityWLS(cellIdx, waterState.FaceArea, waterState.FaceVelocity);
			}
			else
			{
				vector = mt.WaterState.CellVelocity[cellIdx];
			}
			samples.Add((mt.Time, vector));
		});
		return samples;
	}

	public static List<(double Time, double Depth)> TrackCellDepth(Solver solver, int cellIdx)
	{
		double minElev = solver.PropertyTables.CellMinElevation(cellIdx);
		List<(double, double)> samples = new List<(double, double)>();
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			double cellElevationWaterStateTerm = mt.WaterState.CellElevation[cellIdx] - minElev;
			samples.Add((mt.Time, Math.Max(0.0, cellElevationWaterStateTerm)));
		});
		return samples;
	}

	public static List<(double Time, double Velocity)> TrackCellVelMag(Solver solver, int cellIdx)
	{
		List<(double, double)> samples = new List<(double, double)>();
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			Vector vector;
			if (solver.Control.timeStepping == SolverControl.TimeStepping.Implicit)
			{
				HydraulicMeshComputer hydraulicMeshComputer = solver.HydraulicMeshComputer;
				MeshWaterState waterState = solver.WaterState;
				vector = hydraulicMeshComputer.CellComputes.VelocityWLS(cellIdx, waterState.FaceArea, waterState.FaceVelocity);
			}
			else
			{
				vector = mt.WaterState.CellVelocity[cellIdx];
			}
			samples.Add((mt.Time, vector.Length));
		});
		return samples;
	}

	public static Dictionary<int, List<(double Time, double Velocity)>> TrackFaceVelocities(Solver solver, IEnumerable<int> faceIndices)
	{
		int[] source = faceIndices.Distinct().ToArray();
		Dictionary<int, List<(double Time, double Velocity)>> samples = source.ToDictionary((int faceIdx) => faceIdx, (int _) => new List<(double, double)>());
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			foreach (int faceIndex in faceIndices)
			{
				samples[faceIndex].Add((solver.Time, solver.WaterState.FaceVelocity[faceIndex]));
			}
		});
		return samples;
	}

	public static List<(double Time, double Flow)> TrackTransectFlow(Solver solver, IEnumerable<int> faceIndices, IEnumerable<bool> posFlow)
	{
		List<(double, double)> samples = new List<(double, double)>();
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			double computed = 0.0;
			for (int i = 0; i < faceIndices.Count(); i++)
			{
				int elementAt = faceIndices.ElementAt(i);
				int elementAtPosFlowTerm = (posFlow.ElementAt(i) ? 1 : (-1));
				computed += (double)elementAtPosFlowTerm * mt.WaterState.FaceFlow[elementAt];
			}
			samples.Add((mt.Time, computed));
		});
		return samples;
	}

	public static float[] SnapshotCellElevation(MeshWaterState ws)
	{
		double[] doubleValues = ws?.CellElevation;
		if (doubleValues == null)
		{
			return Array.Empty<float>();
		}
		float[] floatValues = new float[doubleValues.Length];
		for (int i = 0; i < doubleValues.Length; i++)
		{
			floatValues[i] = (float)doubleValues[i];
		}
		return floatValues;
	}

	public static void SnapshotCellVelocity(MeshWaterState ws, HydraulicMeshComputer hmc, out float[] velMag, out Vector[] cellVel)
	{
		if (ws?.FaceVelocity == null || ws.FaceArea == null || hmc == null)
		{
			cellVel = Array.Empty<Vector>();
			velMag = Array.Empty<float>();
			return;
		}
		int cellCount = hmc.CellCount;
		cellVel = new Vector[cellCount];
		hmc.CellComputes.VelocityWLS(ws.FaceArea, ws.FaceVelocity, cellVel);
		velMag = new float[cellCount];
		for (int i = 0; i < cellCount; i++)
		{
			velMag[i] = (float)cellVel[i].Length;
		}
	}

	public static float[] SnapshotCellVelocityMagnitude(MeshWaterState ws, HydraulicMeshComputer hmc)
	{
		SnapshotCellVelocity(ws, hmc, out var velMag, out var _);
		return velMag;
	}

	public static List<(double Time, double Value)> TrackScalar(Solver solver, Func<MeshTimestep, double> extract, double minThreshold = 0.0)
	{
		List<(double, double)> samples = new List<(double, double)>();
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			double extract2 = extract(mt);
			if (extract2 > minThreshold)
			{
				samples.Add((mt.Time, extract2));
			}
		});
		return samples;
	}

	public static List<(double Time, double Value)> TrackScalar(ComputeDriver driver, Func<MeshTimestep, double> extract, double minThreshold = 0.0)
	{
		List<(double, double)> samples = new List<(double, double)>();
		driver.TimestepDone = (MeshTimestep mt) =>
		{
			double extract2 = extract(mt);
			if (extract2 > minThreshold)
			{
				samples.Add((mt.Time, extract2));
			}
		};
		return samples;
	}

	public static void FitLine(List<double> x, List<double> y, out double slope, out double intercept)
	{
		double average = x.Average();
		double average2 = y.Average();
		double sum = x.Sum((double v) => v * v);
		double computed = 0.0;
		for (int intervalIndex = 0; intervalIndex < y.Count(); intervalIndex++)
		{
			computed += y[intervalIndex] * x[intervalIndex];
		}
		int ordinal = x.Count();
		slope = (computed / (double)ordinal - average * average2) / (sum / (double)ordinal - average * average);
		intercept = average2 - slope * average;
	}
}
