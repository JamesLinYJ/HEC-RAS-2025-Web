using System;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;

namespace Ras.Synthetics.SyntheticParams;

public class SampsonParams : BasicRectangleParams
{
	public double BasinHalfWidth = 3000.0;

	public double EquilibriumCenterDepth = 10.0;

	public double LinearFriction = 0.001;

	public double VelocityAmplitude = 5.0;

	public double OscillationParameter => Math.Sqrt(8.0 * SolverControl.GravSI * EquilibriumCenterDepth / (BasinHalfWidth * BasinHalfWidth));

	public double AngularFrequency => 0.5 * Math.Sqrt(OscillationParameter * OscillationParameter - LinearFriction * LinearFriction);

	public static SampsonParams Underdamped()
	{
		return new SampsonParams
		{
			CellsWide = 100,
			CellsTall = 3,
			CellSizeX = 100.0,
			CellSize = 100.0,
			NValue = 0f,
			SolveDt = 1.0,
			SolveDuration = 6000.0
		};
	}

	private double CenteredX(Point pt)
	{
		return pt.X - SystemExtent.Center.X;
	}

	public override float TerrainElevation(Point pt)
	{
		double centeredX = CenteredX(pt);
		return (float)(0.0 - EquilibriumCenterDepth * (1.0 - centeredX * centeredX / (BasinHalfWidth * BasinHalfWidth)));
	}

	public double Velocity(double time)
	{
		return VelocityAmplitude * Math.Exp(-0.5 * LinearFriction * time) * Math.Sin(AngularFrequency * time);
	}

	public double SurfaceSlope(double time)
	{
		double linearFrictionTimeTerm = Math.Exp(-0.5 * LinearFriction * time);
		double angularFrequencyTimesTime = AngularFrequency * time;
		return (0.0 - VelocityAmplitude) * linearFrictionTimeTerm * (AngularFrequency * Math.Cos(angularFrequencyTimesTime) + 0.5 * LinearFriction * Math.Sin(angularFrequencyTimesTime)) / SolverControl.GravSI;
	}

	public double ShorelineCenter(double time)
	{
		return BasinHalfWidth * BasinHalfWidth * SurfaceSlope(time) / (2.0 * EquilibriumCenterDepth);
	}

	public double Stage(Point pt, double time)
	{
		double shorelineCenter = ShorelineCenter(time);
		return (0.0 - EquilibriumCenterDepth) * shorelineCenter * shorelineCenter / (BasinHalfWidth * BasinHalfWidth) + SurfaceSlope(time) * CenteredX(pt);
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double time) => (float)Stage(pt, time);
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)Stage(pt, 0.0);
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point _, double time) => new Vector(Velocity(time), 0.0);
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point _, double time) => (float)Math.Abs(Velocity(time));
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point _, double time) => (float)Velocity(time);
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point _, double _) => 0f;
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point _) => (float)Velocity(0.0);
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => 0f;
	}

	public override void InitSolverControl(SolverControl solverControl)
	{
		base.InitSolverControl(solverControl);
		solverControl.linFrictCoeff = LinearFriction;
		solverControl.nonlinFrictCoeff = 0.0;
	}

	public override void InitPlanOptions(Plan plan)
	{
		base.InitPlanOptions(plan);
		plan.LinearFrictionCoefficient = LinearFriction;
	}
}
