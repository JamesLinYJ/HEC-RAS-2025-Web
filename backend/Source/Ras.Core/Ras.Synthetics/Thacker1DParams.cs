using System;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;

namespace Ras.Synthetics;

public class Thacker1DParams : BasicRectangleParams
{
	public double eta0;

	public double D0;

	public double L;

	public int LtsLevels;

	public bool UseFBF;

	public double Omega => Math.Sqrt(2.0 * SolverControl.GravSI * D0 / (L * L));

	public double Ldomain => (double)CellsWide * CellSize;

	public static Thacker1DParams Default()
	{
		return new Thacker1DParams
		{
			CellsTall = 1,
			CellsWide = 160,
			CellSize = 10.0,
			NValue = 0f,
			Slope = 0.0,
			L = 500.0,
			D0 = 2.0,
			eta0 = 0.1,
			SolveDt = 0.5,
			SolveDuration = 500.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		return (float)((0.0 - D0) * (1.0 - Math.Pow((pt.X - Ldomain / 2.0) / L, 2.0)));
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) =>
		{
			double d = Omega * t;
			double ptLdomainTerm = pt.X - Ldomain / 2.0;
			return (float)(2.0 * eta0 * D0 / L * Math.Cos(d) * (ptLdomainTerm - eta0 * Math.Cos(d) / (2.0 * L)));
		};
	}

	public override Func<Point, float> InitialStageFunction()
	{
		Func<Point, double, float> fxn = AnalyticalStageFunction();
		return (Point pt) => fxn(pt, 0.0);
	}

	public override void InitPlanOptions(Plan pl)
	{
		base.InitPlanOptions(pl);
		pl.LtsLevels = (SolverControl.LtsLevelOptions)LtsLevels;
		pl.UseFBF = UseFBF;
	}
}
