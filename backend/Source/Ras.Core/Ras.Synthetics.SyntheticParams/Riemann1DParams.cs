using System;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Synthetics.SyntheticParams;

public class Riemann1DParams : BasicRectangleParams
{
	public enum WavePattern
	{
		TwoShocks,
		TwoRarefactions,
		LeftShockRightRarefaction
	}

	private const string LeftFarFieldBoundaryName = "Left";

	private const string RightFarFieldBoundaryName = "Right";

	public RiemannState LeftState;

	public RiemannState RightState;

	public double DiscontinuityX;

	public WavePattern Pattern = WavePattern.LeftShockRightRarefaction;

	public double FarFieldEGSlope = 0.01;

	public static Riemann1DParams Create(WavePattern pattern)
	{
		RiemannState leftState;
		RiemannState rightState;
		switch (pattern)
		{
		case WavePattern.TwoShocks:
			leftState = new RiemannState(1.0, 2.0);
			rightState = new RiemannState(1.0, -2.0);
			break;
		case WavePattern.TwoRarefactions:
			leftState = new RiemannState(1.0, -5.0);
			rightState = new RiemannState(1.0, 5.0);
			break;
		default:
			leftState = new RiemannState(1.0, 0.0);
			rightState = new RiemannState(2.0, 0.0);
			break;
		}
		return new Riemann1DParams
		{
			CellsWide = 400,
			CellsTall = 1,
			CellSize = 0.5,
			NValue = 0f,
			LeftState = leftState,
			RightState = rightState,
			Pattern = pattern,
			DiscontinuityX = 100.0,
			SolveDt = 0.01,
			SolveDuration = 5.0
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
			state = ((x < DiscontinuityX) ? LeftState : RightState);
			return true;
		}
		double gravSI = SolverControl.GravSI;
		double sqrtGravSITimesDepth = Math.Sqrt(gravSI * LeftState.Depth);
		double sqrtGravSITimesDepth2 = Math.Sqrt(gravSI * RightState.Depth);
		double discontinuityXTimeTerm = (x - DiscontinuityX) / time;
		if (RightState.Velocity - LeftState.Velocity >= 2.0 * (sqrtGravSITimesDepth + sqrtGravSITimesDepth2))
		{
			return TryEvaluateVacuum(discontinuityXTimeTerm, sqrtGravSITimesDepth, sqrtGravSITimesDepth2, gravSI, out state);
		}
		if (!TryGetStarState(gravSI, out var depth, out var velocity))
		{
			state = default;
			return false;
		}
		double sqrtGravSITimesDepth3 = Math.Sqrt(gravSI * depth);
		if (discontinuityXTimeTerm <= velocity)
		{
			if (depth > LeftState.Depth)
			{
				double velocityLeftStateTerm = LeftState.Velocity - Math.Sqrt(0.5 * gravSI * depth * (depth + LeftState.Depth) / LeftState.Depth);
				state = ((discontinuityXTimeTerm <= velocityLeftStateTerm) ? LeftState : new RiemannState(depth, velocity));
				return true;
			}
			double velocityAboveSqrtGravSITimesDepth = LeftState.Velocity - sqrtGravSITimesDepth;
			double velocityAboveSqrtGravSITimesDepth2 = velocity - sqrtGravSITimesDepth3;
			if (discontinuityXTimeTerm <= velocityAboveSqrtGravSITimesDepth)
			{
				state = LeftState;
			}
			else if (discontinuityXTimeTerm >= velocityAboveSqrtGravSITimesDepth2)
			{
				state = new RiemannState(depth, velocity);
			}
			else
			{
				double velocityLeftStateTerm2 = (LeftState.Velocity + 2.0 * sqrtGravSITimesDepth - discontinuityXTimeTerm) / 3.0;
				double velocity2 = (LeftState.Velocity + 2.0 * sqrtGravSITimesDepth + 2.0 * discontinuityXTimeTerm) / 3.0;
				state = new RiemannState(velocityLeftStateTerm2 * velocityLeftStateTerm2 / gravSI, velocity2);
			}
			return true;
		}
		if (depth > RightState.Depth)
		{
			double velocityRightStateTerm = RightState.Velocity + Math.Sqrt(0.5 * gravSI * depth * (depth + RightState.Depth) / RightState.Depth);
			state = ((discontinuityXTimeTerm >= velocityRightStateTerm) ? RightState : new RiemannState(depth, velocity));
			return true;
		}
		double velocityRightStateTerm2 = RightState.Velocity + sqrtGravSITimesDepth2;
		double velocityTerm = velocity + sqrtGravSITimesDepth3;
		if (discontinuityXTimeTerm >= velocityRightStateTerm2)
		{
			state = RightState;
		}
		else if (discontinuityXTimeTerm <= velocityTerm)
		{
			state = new RiemannState(depth, velocity);
		}
		else
		{
			double velocityRightStateTerm3 = (discontinuityXTimeTerm - RightState.Velocity + 2.0 * sqrtGravSITimesDepth2) / 3.0;
			double velocity3 = (2.0 * discontinuityXTimeTerm + RightState.Velocity - 2.0 * sqrtGravSITimesDepth2) / 3.0;
			state = new RiemannState(velocityRightStateTerm3 * velocityRightStateTerm3 / gravSI, velocity3);
		}
		return true;
	}

	public bool TryGetStarState(double gravity, out double depth, out double velocity)
	{
		double computed = 1E-12;
		double computed2 = Math.Max(LeftState.Depth, RightState.Depth);
		double pressureFunctionLeftStateTerm = PressureFunction(computed, LeftState, gravity) + PressureFunction(computed, RightState, gravity) + RightState.Velocity - LeftState.Velocity;
		double pressureFunctionLeftStateTerm2 = PressureFunction(computed2, LeftState, gravity) + PressureFunction(computed2, RightState, gravity) + RightState.Velocity - LeftState.Velocity;
		while (pressureFunctionLeftStateTerm2 < 0.0 && computed2 < 1000000000000.0)
		{
			computed2 *= 2.0;
			pressureFunctionLeftStateTerm2 = PressureFunction(computed2, LeftState, gravity) + PressureFunction(computed2, RightState, gravity) + RightState.Velocity - LeftState.Velocity;
		}
		if (pressureFunctionLeftStateTerm > 0.0 || pressureFunctionLeftStateTerm2 < 0.0)
		{
			depth = 0.0;
			velocity = 0.0;
			return false;
		}
		for (int i = 0; i < 100; i++)
		{
			double computed3 = 0.5 * (computed + computed2);
			if (PressureFunction(computed3, LeftState, gravity) + PressureFunction(computed3, RightState, gravity) + RightState.Velocity - LeftState.Velocity > 0.0)
			{
				computed2 = computed3;
			}
			else
			{
				computed = computed3;
			}
		}
		depth = 0.5 * (computed + computed2);
		double pressureFunction = PressureFunction(depth, LeftState, gravity);
		double pressureFunction2 = PressureFunction(depth, RightState, gravity);
		velocity = 0.5 * (LeftState.Velocity + RightState.Velocity + pressureFunction2 - pressureFunction);
		return true;
	}

	private bool TryEvaluateVacuum(double xi, double cLeft, double cRight, double gravity, out RiemannState state)
	{
		double velocityAboveCLeft = LeftState.Velocity - cLeft;
		double velocityLeftStateTerm = LeftState.Velocity + 2.0 * cLeft;
		double velocityRightStateTerm = RightState.Velocity - 2.0 * cRight;
		double velocityPlusCRight = RightState.Velocity + cRight;
		if (xi <= velocityAboveCLeft)
		{
			state = LeftState;
		}
		else if (xi < velocityLeftStateTerm)
		{
			double velocityLeftStateTerm2 = (LeftState.Velocity + 2.0 * cLeft - xi) / 3.0;
			state = new RiemannState(velocityLeftStateTerm2 * velocityLeftStateTerm2 / gravity, (LeftState.Velocity + 2.0 * cLeft + 2.0 * xi) / 3.0);
		}
		else if (xi <= velocityRightStateTerm)
		{
			state = new RiemannState(0.0, 0.0);
		}
		else if (xi < velocityPlusCRight)
		{
			double xiVelocityTerm = (xi - RightState.Velocity + 2.0 * cRight) / 3.0;
			state = new RiemannState(xiVelocityTerm * xiVelocityTerm / gravity, (2.0 * xi + RightState.Velocity - 2.0 * cRight) / 3.0);
		}
		else
		{
			state = RightState;
		}
		return true;
	}

	private static double PressureFunction(double depth, RiemannState state, double gravity)
	{
		if (depth > state.Depth)
		{
			return (depth - state.Depth) * Math.Sqrt(0.5 * gravity * (1.0 / depth + 1.0 / state.Depth));
		}
		return 2.0 * (Math.Sqrt(gravity * depth) - Math.Sqrt(gravity * state.Depth));
	}

	private RiemannState InitialState(Point pt)
	{
		if (!(pt.X < DiscontinuityX))
		{
			return RightState;
		}
		return LeftState;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double time) => (!TryEvaluate(pt.X, time, out var state)) ? (-9999f) : ((float)state.Depth);
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)InitialState(pt).Depth;
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
		return (Point pt) => (float)InitialState(pt).Velocity;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => 0f;
	}

	public override BoundaryConditionLine GetUpstreamBC(Ras.Layers.BoundaryCondition bc, Polyline pline)
	{
		if (!HasFarFieldFlowBoundaries())
		{
			return null;
		}
		double depthLeftStateTerm = LeftState.Depth * LeftState.Velocity * (double)CellsTall * CellSize;
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantFlow(bc, pline, depthLeftStateTerm);
		boundaryConditionLine.Name = "Left";
		return boundaryConditionLine;
	}

	public override BoundaryConditionLine GetDownstreamBC(Ras.Layers.BoundaryCondition bc, Polyline pline)
	{
		if (!HasFarFieldFlowBoundaries())
		{
			return null;
		}
		double depthRightStateTerm = (0.0 - RightState.Depth) * RightState.Velocity * (double)CellsTall * CellSize;
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantFlow(bc, pline, depthRightStateTerm);
		boundaryConditionLine.Name = "Right";
		return boundaryConditionLine;
	}

	public override void ConfigureEngineBC(Ras.Engine.BoundaryCondition engineBc, BoundaryConditionLine srcLine, string variable)
	{
		if (!(variable != "Flow") && srcLine != null && (!(srcLine.Name != "Left") || !(srcLine.Name != "Right")))
		{
			engineBc.EGSlope = FarFieldEGSlope;
		}
	}

	private bool HasFarFieldFlowBoundaries()
	{
		if (Pattern != WavePattern.TwoShocks)
		{
			return Pattern == WavePattern.TwoRarefactions;
		}
		return true;
	}
}
