using System;
using Geospatial.Vectors;
using Ras.Engine;
using Utility.Units;

namespace Ras.Synthetics;

public class NormalDepthParams : InOutPlanarParams
{
	public double TargetDepth = 1.0;

	public UnitSystem BuildUnits = UnitSystem.SI;

	public override UnitSystem Units => BuildUnits;

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) => (float)((double)SimplePlanarTerrain(pt) + TargetDepth);
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		double v = 1.0 / (double)NValue * Math.Pow(TargetDepth, 2.0 / 3.0) * Math.Sqrt(Math.Abs(Slope));
		return (Point pt, double t) => new Vector(v, 0.0);
	}

	public new static NormalDepthParams Default()
	{
		NormalDepthParams normalDepthParams = new NormalDepthParams();
		normalDepthParams.CellsTall = 3;
		normalDepthParams.CellsWide = 15;
		normalDepthParams.CellSize = 10.0;
		normalDepthParams.RampSeconds = 500.0;
		normalDepthParams.NValue = 0.03f;
		normalDepthParams.Slope = 0.005;
		normalDepthParams.UpstreamEGSlope = 0.01;
		normalDepthParams.TargetDepth = 1.0;
		normalDepthParams.SolveDt = 1.6;
		normalDepthParams.SolveDuration = 7000.0;
		normalDepthParams.InitNormalDepthBCs();
		return normalDepthParams;
	}

	public new static NormalDepthParams GPU()
	{
		NormalDepthParams normalDepthParams = new NormalDepthParams();
		normalDepthParams.CellsTall = 3;
		normalDepthParams.CellsWide = 15;
		normalDepthParams.CellSize = 10.0;
		normalDepthParams.RampSeconds = 500.0;
		normalDepthParams.NValue = 0.03f;
		normalDepthParams.Slope = 0.005;
		normalDepthParams.UseGPU = true;
		normalDepthParams.TargetDepth = 1.0;
		normalDepthParams.SolveDt = 1.6;
		normalDepthParams.SolveDuration = 7000.0;
		normalDepthParams.InitNormalDepthBCs();
		return normalDepthParams;
	}

	public void InitNormalDepthBCs()
	{
		UpstreamFlow = ComputeFlowAtNormalDepth(Slope, NValue, CellSize * (double)CellsTall, TargetDepth);
		DownstreamStage = TargetDepth;
	}

	public static double ComputeFlowAtNormalDepth(double slope, double nValue, double channelWidth, double targetNormalDepth, UnitSystem units = UnitSystem.SI)
	{
		return ((units == UnitSystem.USCustomary) ? 1.486 : 1.0) / nValue * targetNormalDepth * channelWidth * Math.Pow(targetNormalDepth, 2.0 / 3.0) * Math.Sqrt(Math.Abs(slope));
	}

	public static ComputePackage BuildCart(double targetNormalDepth, out double expectedCellDepth, double rampSeconds = 0.0, double rotateDegrees = 0.0, UnitSystem units = UnitSystem.SI, SolverControl.EquationSet equationSet = SolverControl.EquationSet.SWE, SolverControl.TimeStepping timeStepping = SolverControl.TimeStepping.Explicit)
	{
		return Build(10, hexMesh: false, targetNormalDepth, out expectedCellDepth, rampSeconds, rotateDegrees, units, equationSet, timeStepping);
	}

	public static ComputePackage BuildHex(double targetNormalDepth, out double expectedCellDepth, double rampSeconds = 0.0, double rotateDegrees = 0.0, UnitSystem units = UnitSystem.SI, SolverControl.EquationSet equationSet = SolverControl.EquationSet.SWE, SolverControl.TimeStepping timeStepping = SolverControl.TimeStepping.Explicit)
	{
		return Build(9, hexMesh: true, targetNormalDepth, out expectedCellDepth, rampSeconds, rotateDegrees, units, equationSet, timeStepping);
	}

	private static ComputePackage Build(int crossChannelCells, bool hexMesh, double targetNormalDepth, out double expectedCellDepth, double rampSeconds, double rotateDegrees, UnitSystem units, SolverControl.EquationSet equationSet, SolverControl.TimeStepping timeStepping)
	{
		NormalDepthParams normalDepthParams = new NormalDepthParams
		{
			CellsTall = crossChannelCells,
			CellsWide = 100,
			CellSize = 10.0,
			NValue = 0.02f,
			Slope = 0.0004,
			TargetDepth = targetNormalDepth,
			RampSeconds = rampSeconds,
			HexMesh = hexMesh,
			RotateDegrees = rotateDegrees,
			SolveDuration = 7000.0
		};
		normalDepthParams.UpstreamFlow = ComputeFlowAtNormalDepth(normalDepthParams.Slope, normalDepthParams.NValue, normalDepthParams.CellSize * (double)normalDepthParams.CellsTall, normalDepthParams.TargetDepth, units);
		normalDepthParams.DownstreamStage = normalDepthParams.TargetDepth;
		normalDepthParams.UpstreamEGSlope = normalDepthParams.Slope;
		expectedCellDepth = targetNormalDepth;
		return PackageBuilder.Build(normalDepthParams, null, units, equationSet, timeStepping);
	}
}
