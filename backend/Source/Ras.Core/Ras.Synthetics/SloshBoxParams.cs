using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;

namespace Ras.Synthetics;

public class SloshBoxParams : BasicRectangleParams
{
	public double DownstreamStage;

	public double UpstreamStage;

	public bool Randomize;

	public static SloshBoxParams Default()
	{
		return new SloshBoxParams
		{
			CellsTall = 1,
			CellsWide = 100,
			UpstreamStage = 10.01,
			DownstreamStage = 9.99,
			CellSize = 100.0,
			NValue = 0f,
			Slope = 0.0,
			SolveDt = 5.0,
			SolveDuration = 36000.0
		};
	}

	public override void InitSolverControl(SolverControl sc)
	{
		sc.advectionMethod = SolverControl.AdvectionMethod.None;
		sc.cellVelocityMethod = SolverControl.CellVelocityMethod.WLS;
		sc.faceElevationMethod = FaceElevationMethod.Frozen;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		double L = (double)CellsWide * CellSize;
		double mean = 0.5 * (UpstreamStage + DownstreamStage);
		if (!Randomize)
		{
			return (Point pt, double t) =>
			{
				double x = pt.X;
				double gravSISolverControlTerm = 2.0 * L / Math.Sqrt(SolverControl.GravSI * 0.5 * (DownstreamStage + UpstreamStage));
				double d = t / gravSISolverControlTerm * 2.0 * Math.PI;
				return (float)(mean + Math.Cos(d) * (DownstreamStage - UpstreamStage) * 0.5 * Math.Cos(Math.PI * x / L));
			};
		}
		return (Point pt, double t) =>
		{
			double x = pt.X;
			double upstreamStageAboveDownstreamStage = UpstreamStage - DownstreamStage;
			double cellSizePiTerm = Math.Floor(x / CellSize) * Math.PI * 100000.0 % 1.0;
			return (float)(mean + upstreamStageAboveDownstreamStage * (cellSizePiTerm - 0.5));
		};
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		if (Randomize)
		{
			return null;
		}
		return (Point pt, double t) =>
		{
			double cellsWideTimesCellSize = (double)CellsWide * CellSize;
			double upstreamStageDownstreamStageTerm = 0.5 * (UpstreamStage + DownstreamStage);
			double downstreamStageUpstreamStageTerm = 0.5 * (DownstreamStage - UpstreamStage);
			double gravSISolverControlTerm = 2.0 * cellsWideTimesCellSize / Math.Sqrt(SolverControl.GravSI * upstreamStageDownstreamStageTerm);
			double piTerm = Math.PI * 2.0 / gravSISolverControlTerm;
			double piOverCellsWideTimesCellSize = Math.PI / cellsWideTimesCellSize;
			return new Vector(downstreamStageUpstreamStageTerm * piTerm / (piOverCellsWideTimesCellSize * upstreamStageDownstreamStageTerm) * Math.Sin(piTerm * t) * Math.Sin(piOverCellsWideTimesCellSize * pt.X), 0.0);
		};
	}

	public override Func<Point, float> InitialStageFunction()
	{
		Func<Point, double, float> fxn = AnalyticalStageFunction();
		return (Point pt) => fxn(pt, 0.0);
	}

	public override Func<Point, float> InitialFaceStageFunction()
	{
		return (Point pt) => 10f;
	}
}
