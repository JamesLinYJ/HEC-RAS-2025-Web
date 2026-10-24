using System;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics;

public class AirPressureBoxParams : BasicRectangleParams
{
	public double RampSeconds = 172800.0;

	public double StandardAirPressure = 101325.0;

	public double AirPresGrad = 0.1;

	public double WaterDensity = 1000.0;

	public double StillWaterStage = 10.0;

	public static AirPressureBoxParams Default()
	{
		return new AirPressureBoxParams
		{
			CellsTall = 1,
			CellsWide = 20,
			CellSize = 500.0,
			NValue = 10f,
			Slope = 0.0,
			SolveDt = 10.0,
			SolveDuration = 345600.0
		};
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) =>
		{
			double airPresGradOverGravSITimesWaterDensity = AirPresGrad / (SolverControl.GravSI * WaterDensity);
			return (float)(StillWaterStage - airPresGradOverGravSITimesWaterDensity * (pt.X - (double)CellsWide * CellSize / 2.0));
		};
	}

	public double FuncAirPressure(Point pt)
	{
		return StandardAirPressure + (pt.X - (double)CellsWide * CellSize / 2.0) * AirPresGrad;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)StillWaterStage;
	}
}
