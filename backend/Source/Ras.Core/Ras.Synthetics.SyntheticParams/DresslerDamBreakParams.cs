using System;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics.SyntheticParams;

public class DresslerDamBreakParams : BasicRectangleParams
{
	public double ReservoirDepth = 6.0;

	public double DamX = 1000.0;

	public double ChezyCoefficient = 40.0;

	public static DresslerDamBreakParams Default()
	{
		return new DresslerDamBreakParams
		{
			CellsWide = 400,
			CellsTall = 2,
			CellSize = 5.0,
			NValue = 0f,
			SolveDt = 0.05,
			SolveDuration = 40.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		return 0f;
	}

	public bool TryEvaluate(double x, double time, out RiemannState state)
	{
		if (time <= 0.0)
		{
			state = ((x <= DamX) ? new RiemannState(ReservoirDepth, 0.0) : new RiemannState(0.0, 0.0));
			return true;
		}
		double sqrtGravSITimesReservoirDepth = Math.Sqrt(SolverControl.GravSI * ReservoirDepth);
		double damXTimeTerm = DamX - time * sqrtGravSITimesReservoirDepth;
		double damXTimeTerm2 = DamX + 2.0 * time * sqrtGravSITimesReservoirDepth;
		if (x <= damXTimeTerm)
		{
			state = new RiemannState(ReservoirDepth, 0.0);
			return true;
		}
		if (x >= damXTimeTerm2)
		{
			state = new RiemannState(0.0, 0.0);
			return true;
		}
		double transitionXi = TransitionXi(time, sqrtGravSITimesReservoirDepth);
		double damXTransitionXiTerm = DamX + transitionXi * time * sqrtGravSITimesReservoirDepth;
		CorrectedState(damXTransitionXiTerm, time, sqrtGravSITimesReservoirDepth, out var state2);
		if (x <= damXTransitionXiTerm)
		{
			return CorrectedState(x, time, sqrtGravSITimesReservoirDepth, out state);
		}
		double computed = (damXTimeTerm2 - x) / (damXTimeTerm2 - damXTransitionXiTerm);
		state = new RiemannState(state2.Depth * computed * computed, state2.Velocity);
		return true;
	}

	private bool CorrectedState(double x, double time, double celerity, out RiemannState state)
	{
		double damXTimeTerm = (x - DamX) / (time * celerity);
		double computed = Math.Max(2.0 - damXTimeTerm, 1E-08);
		double computed2 = 6.0 / (5.0 * computed) - 2.0 / 3.0 + 4.0 * Math.Sqrt(3.0) / 135.0 * Math.Pow(computed, 1.5);
		double computed3 = 12.0 / computed - 2.6666666666666665 + 8.0 * Math.Sqrt(3.0) / 189.0 * Math.Pow(computed, 1.5) - 108.0 / (7.0 * computed * computed);
		double gravSIPolynomialOverChezyCoefficientPolynomial = SolverControl.GravSI * SolverControl.GravSI / (ChezyCoefficient * ChezyCoefficient);
		double celerityDamXTerm = 2.0 * celerity / 3.0 - (x - DamX) / (3.0 * time) + gravSIPolynomialOverChezyCoefficientPolynomial * computed2 * time;
		double velocity = 2.0 * celerity / 3.0 + 2.0 * (x - DamX) / (3.0 * time) + gravSIPolynomialOverChezyCoefficientPolynomial * computed3 * time;
		state = new RiemannState(Math.Max(celerityDamXTerm * celerityDamXTerm / SolverControl.GravSI, 0.0), velocity);
		if (double.IsFinite(state.Depth))
		{
			return double.IsFinite(state.Velocity);
		}
		return false;
	}

	private double TransitionXi(double time, double celerity)
	{
		double result = -1.0;
		double negativeInfinity = double.NegativeInfinity;
		for (int i = 0; i <= 600; i++)
		{
			double computed = -1.0 + 2.999 * (double)i / 600.0;
			double x = DamX + computed * time * celerity;
			CorrectedState(x, time, celerity, out var state);
			if (state.Velocity > negativeInfinity)
			{
				negativeInfinity = state.Velocity;
				result = computed;
			}
		}
		return result;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double time) => (!TryEvaluate(pt.X, time, out var state)) ? (-9999f) : ((float)state.Depth);
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (!(pt.X <= DamX)) ? 0f : ((float)ReservoirDepth);
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double time) => (!TryEvaluate(pt.X, time, out var state)) ? default(Vector) : new Vector(state.Velocity, 0.0);
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point pt, double time) => (!TryEvaluate(pt.X, time, out var state)) ? (-9999f) : ((float)Math.Abs(state.Velocity));
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point pt, double time) => (!TryEvaluate(pt.X, time, out var state)) ? (-9999f) : ((float)state.Velocity);
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point _, double _) => 0f;
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point _) => 0f;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => 0f;
	}

	public override void InitSolverControl(SolverControl solverControl)
	{
		base.InitSolverControl(solverControl);
		solverControl.bedFrictionMethod = SolverControl.BedFrictionMethod.Constant;
		solverControl.nonlinFrictCoeff = SolverControl.GravSI / (ChezyCoefficient * ChezyCoefficient);
	}
}
