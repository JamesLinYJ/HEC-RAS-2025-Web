using System;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics.SyntheticParams;

public class WindCircularBasinParams : BaseParams
{
	public double H = 100.0;

	public double W = 0.0001;

	public double Fc = 0.0001;

	public double Kappa = 0.001;

	public double Rhow = 1000.0;

	public double CellSize = 1000.0;

	public double Radius = 50000.0;

	public override Extent SystemExtent => new Extent(0.0 - Radius, Radius, 0.0 - Radius, Radius);

	public static WindCircularBasinParams Default()
	{
		return new WindCircularBasinParams
		{
			NValue = 0f,
			H = 100.0,
			W = 0.0001,
			Fc = 0.0001,
			Kappa = 0.001,
			Rhow = 1000.0,
			CellSize = 1000.0,
			Radius = 50000.0,
			SolveDt = 100.0,
			SolveDuration = 500000.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		return 0f;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) => (float)FuncStage(pt);
	}

	public double FuncStage(Point pt)
	{
		double ptTerm = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
		if (ptTerm >= Radius)
		{
			return -9999.0;
		}
		double ptTerm2 = Math.Atan2(pt.Y, pt.X);
		double gravSI = SolverControl.GravSI;
		if (Fc < double.Epsilon)
		{
			return H + W * ptTerm * ptTerm / (4.0 * gravSI * H * Radius) * Math.Sin(2.0 * ptTerm2);
		}
		return H + W * Fc / (Radius * gravSI * H * Kappa) * (Radius * Radius / 8.0 + ptTerm * ptTerm / 4.0 * (Kappa / Fc * Math.Sin(2.0 * ptTerm2) - 1.0));
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)H;
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point pt, double t) => (float)FuncVelocityMagnitude(pt);
	}

	public double FuncVelocityMagnitude(Point pt)
	{
		double ptTerm = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
		if (ptTerm >= Radius)
		{
			return -9999.0;
		}
		return W * ptTerm / (2.0 * Radius * Kappa * H);
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) => FuncVelocity(pt);
	}

	public Vector FuncVelocity(Point pt)
	{
		if (Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y) >= Radius)
		{
			return new Vector(-9999.0, -9999.0);
		}
		double radiusKappaTerm = W / (2.0 * Radius * Kappa * H);
		return new Vector(radiusKappaTerm * pt.Y, (0.0 - radiusKappaTerm) * pt.X);
	}

	public Vector FuncWindStress(Point pt)
	{
		if (Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y) >= Radius)
		{
			return new Vector(-9999.0, -9999.0);
		}
		return new Vector(Rhow * (W / Radius) * pt.Y, 0.0);
	}

	public float FuncWindStressX(Point pt)
	{
		if (Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y) >= Radius)
		{
			return -9999f;
		}
		return (float)(Rhow * (W / Radius) * pt.Y);
	}

	public float FuncWindStressY(Point pt)
	{
		if (Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y) >= Radius)
		{
			return -9999f;
		}
		return 0f;
	}

	public override Mesh CreateMesh()
	{
		return MeshFactory.CircularMesh(SystemExtent, CellSize);
	}
}
