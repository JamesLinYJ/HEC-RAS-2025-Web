using System;
using Geospatial.Vectors;

namespace Ras.Synthetics;

public class BathtubParams : BasicRectangleParams
{
	public double InitStage = 1.0;

	public double PrecipRate = 0.001;

	public double EvapoRate = 0.005;

	public static BathtubParams Default()
	{
		return new BathtubParams
		{
			CellsTall = 1,
			CellsWide = 2,
			CellSize = 10.0,
			NValue = 0f,
			Slope = 0.0,
			InitStage = 1.0,
			PrecipRate = 0.001,
			EvapoRate = 0.005,
			SolveDt = 1.0,
			SolveDuration = 100.0
		};
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) => (float)(InitStage + t * (PrecipRate - EvapoRate));
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)InitStage;
	}
}
