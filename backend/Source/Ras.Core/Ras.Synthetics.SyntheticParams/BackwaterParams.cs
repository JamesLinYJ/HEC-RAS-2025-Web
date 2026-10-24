using System;
using System.Threading;
using Geospatial.Vectors;
using Ras.Synthetics.Solvers;
using Utility.Units;

namespace Ras.Synthetics.SyntheticParams;

public class BackwaterParams : TrapezoidalParams
{
	private double[] anaStage;

	private double[] anaVelocity;

	private readonly Lock _precomputeLock = new Lock();

	private double dx = 0.01;

	public override UnitSystem Units => UnitSystem.USCustomary;

	public new static BackwaterParams Default()
	{
		return new BackwaterParams
		{
			CellsTall = 1,
			CellsWide = 100,
			CellSize = 40.0,
			CellSizeX = 30.0,
			RampSeconds = 500.0,
			NValue = 0.025f,
			Slope = 0.0016,
			UpstreamEGSlope = 0.0016,
			UpstreamFlow = 400.0,
			DownstreamStage = 5.0,
			BottomWidth = 20.0,
			SideSlopeHV = 2.0,
			SolveDt = 1.0,
			SolveDuration = 3600.0,
			ReportFrequency = 60
		};
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) => ComputeStage(pt);
	}

	private float ComputeStage(Point pt)
	{
		EnsurePrecomputed();
		return InterpolateFromAnalytical(pt, anaStage);
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) => ComputeVelocity(pt);
	}

	private Vector ComputeVelocity(Point p)
	{
		EnsurePrecomputed();
		return new Vector(InterpolateFromAnalytical(p, anaVelocity), 0.0);
	}

	private void EnsurePrecomputed()
	{
		if (anaStage != null)
		{
			return;
		}
		using (_precomputeLock.EnterScope())
		{
			if (anaStage == null)
			{
				PrecomputeAnalytical();
			}
		}
	}

	private float InterpolateFromAnalytical(Point p, double[] anaValues)
	{
		int anaStageLength = anaStage.Length;
		double anaStageLengthDxTerm = (double)(anaStageLength - 1) * dx;
		double computed = anaStageLengthDxTerm - p.X;
		if (computed < 0.0)
		{
			computed = 0.0;
		}
		if (computed > anaStageLengthDxTerm)
		{
			computed = anaStageLengthDxTerm;
		}
		int anaStageLengthOffset = (int)(computed / dx);
		if (anaStageLengthOffset >= anaStageLength - 1)
		{
			anaStageLengthOffset = anaStageLength - 2;
		}
		double dxTerm = (computed - (double)anaStageLengthOffset * dx) / dx;
		return (float)(anaValues[anaStageLengthOffset] * (1.0 - dxTerm) + anaValues[anaStageLengthOffset + 1] * dxTerm);
	}

	private void PrecomputeAnalytical()
	{
		double cellSizeX = ((CellSizeX > 0.0) ? CellSizeX : CellSize);
		double cellsWideTimesCellSizeX = (double)CellsWide * cellSizeX;
		int count = (int)Math.Ceiling(cellsWideTimesCellSizeX / dx);
		CrossSectionTrapezoidal xs = new CrossSectionTrapezoidal
		{
			BottomWidth = BottomWidth,
			SideSlopeHV = SideSlopeHV
		};
		bool unitsUsCustomaryTerm = Units == UnitSystem.USCustomary;
		SolverStandardStep solverStandardStep = new SolverStandardStep
		{
			Flow = UpstreamFlow,
			XSec = (double _) => xs,
			BedElevation = (double x) => Slope * x,
			ManningN = (double _) => NValue,
			Gravity = (unitsUsCustomaryTerm ? 32.174 : 9.80665),
			ManningKn = (unitsUsCustomaryTerm ? 1.486 : 1.0)
		};
		double[] doubleValues = new double[count + 1];
		for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
		{
			doubleValues[intervalIndex] = Math.Min((double)intervalIndex * dx, cellsWideTimesCellSizeX);
		}
		solverStandardStep.TrySolve(doubleValues, 0.0, DownstreamStage, SolverStandardStep.MarchDirection.Upstream, out var result);
		double[] anaStageArray = new double[result.Length];
		double[] anaVelocityArray = new double[result.Length];
		for (int intervalIndex2 = 0; intervalIndex2 < result.Length; intervalIndex2++)
		{
			anaStageArray[intervalIndex2] = result[intervalIndex2].Stage;
			anaVelocityArray[intervalIndex2] = result[intervalIndex2].Velocity;
		}
		anaVelocity = anaVelocityArray;
		anaStage = anaStageArray;
	}
}
