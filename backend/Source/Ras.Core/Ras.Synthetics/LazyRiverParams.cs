using System;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class LazyRiverParams : BaseParams
{
	public double R0 = 100.0;

	public double R1 = 130.0;

	public double CellSize = 10.0;

	public double Ratio = 2.0;

	public double H0 = 10.0;

	public double U0 = 0.5 / Math.Exp(-1.0);

	public bool Streamwise;

	public double r0 => R1;

	public override Extent SystemExtent => new Extent(0.0 - R1, R1, 0.0 - R1, R1);

	public double Omega => FuncStreamwiseVelocity(R1) / R1;

	public static LazyRiverParams Default()
	{
		return new LazyRiverParams
		{
			NValue = 0f,
			R0 = 100.0,
			R1 = 130.0,
			CellSize = 10.0,
			Ratio = 2.0,
			H0 = 10.0,
			U0 = 0.5 / Math.Exp(-1.0),
			SolveDt = 1.0,
			SolveDuration = 5000.0,
			Streamwise = false
		};
	}

	public override float TerrainElevation(Point pt)
	{
		return 0f;
	}

	public double FuncEta(double r)
	{
		double r0Term = 2.0 * r / r0;
		return H0 + U0 * U0 / (4.0 * SolverControl.GravSI) * (1.0 - r0Term * Math.Exp(0.0 - r0Term) - Math.Exp(0.0 - r0Term));
	}

	public double FuncStreamwiseVelocity(double r)
	{
		return U0 / r0 * Math.Exp((0.0 - r) / r0);
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) =>
		{
			double r = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
			return _ValidRadius(r) ? ((float)FuncEta(r)) : (-9999f);
		};
	}

	public override Func<Point, float> InitialStageFunction()
	{
		Func<Point, double, float> fxn = AnalyticalStageFunction();
		return (Point pt) => fxn(pt, 0.0);
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		Func<Point, double, float> fxn = AnalyticalVelocityXFunction();
		return (Point pt) => fxn(pt, 0.0);
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		Func<Point, double, float> fxn = AnalyticalVelocityYFunction();
		return (Point pt) => fxn(pt, 0.0);
	}

	private bool _ValidRadius(double r)
	{
		if (0.99 * R0 < r)
		{
			return r < 1.01 * R1;
		}
		return false;
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point pt, double t) =>
		{
			double r = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
			return _ValidRadius(r) ? ((float)((0.0 - pt.Y) * FuncStreamwiseVelocity(r))) : (-9999f);
		};
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point pt, double t) =>
		{
			double r = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
			return _ValidRadius(r) ? ((float)(pt.X * FuncStreamwiseVelocity(r))) : (-9999f);
		};
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) =>
		{
			double r = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
			return _ValidRadius(r) ? (new Vector(0.0 - pt.Y, pt.X) * FuncStreamwiseVelocity(r)) : new Vector(-9999.0, -9999.0);
		};
	}

	public override Func<Point, Vector> InitialVelocityFunction()
	{
		Func<Point, double, Vector> fxn = AnalyticalVelocityFunction();
		return (Point pt) => fxn(pt, 0.0);
	}

	public override Mesh CreateMesh()
	{
		return MeshGenerator.FromTwoCircles(R0, R1, CellSize, Ratio);
	}
}
