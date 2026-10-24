using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Layers;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class Thacker2DParams : BaseParams
{
	public enum CircularMeshKind
	{
		UniformVoronoi,
		UniformTriangle
	}

	public double eta0;

	public double D0;

	public double L;

	public double f;

	public bool UseGPU;

	public int LtsLevels = 3;

	public bool UseFBF = true;

	public double CellSize;

	public double Radius;

	public CircularMeshKind MeshKind;

	public bool ReIndexMesh { get; set; }

	public int QuantumSize { get; set; } = 128;

	public double Omega => Math.Sqrt(8.0 * SolverControl.GravSI * D0 / (L * L));

	public double A => (Math.Pow(D0 + eta0, 2.0) - D0 * D0) / (Math.Pow(D0 + eta0, 2.0) + D0 * D0);

	public override double SolveDuration => Math.PI * 20.0 / Omega;

	public override Extent SystemExtent => Extent.CenteredOn(Point.Origin, Radius * 2.0);

	public static Thacker2DParams Default()
	{
		return new Thacker2DParams
		{
			NValue = 0f,
			L = 610000.0,
			Radius = 650000.0,
			D0 = 50.0,
			eta0 = 2.0,
			CellSize = 10000.0,
			SolveDt = 1000.0,
			LtsLevels = 0,
			UseFBF = false,
			ReportFrequency = 1
		};
	}

	public static Thacker2DParams GPU()
	{
		return new Thacker2DParams
		{
			L = 610000.0,
			Radius = 650000.0,
			D0 = 50.0,
			eta0 = 2.0,
			CellSize = 10000.0,
			UseGPU = true,
			SolveDt = 1000.0,
			ReportFrequency = 1
		};
	}

	public static Thacker2DParams GPUSmall()
	{
		return new Thacker2DParams
		{
			L = 20.0,
			Radius = 5.0,
			D0 = 50.0,
			eta0 = 2.0,
			CellSize = 2.0,
			UseGPU = true,
			SolveDt = 10000.0,
			ReportFrequency = 1
		};
	}

	public static Thacker2DParams Big()
	{
		return new Thacker2DParams
		{
			NValue = 0f,
			L = 610000.0,
			Radius = 650000.0,
			D0 = 50.0,
			eta0 = 2.0,
			CellSize = 4000.0,
			SolveDt = 400.0,
			LtsLevels = 0,
			UseFBF = false,
			ReportFrequency = 10
		};
	}

	public static Thacker2DParams BigGPU()
	{
		return new Thacker2DParams
		{
			NValue = 0f,
			L = 610000.0,
			Radius = 650000.0,
			D0 = 50.0,
			eta0 = 2.0,
			CellSize = 4000.0,
			UseGPU = true,
			SolveDt = 400.0,
			ReportFrequency = 10
		};
	}

	public static Thacker2DParams Small()
	{
		return new Thacker2DParams
		{
			NValue = 0f,
			L = 20.0,
			Radius = 5.0,
			D0 = 50.0,
			eta0 = 2.0,
			CellSize = 2.0,
			SolveDt = 0.2
		};
	}

	public override float TerrainElevation(Point pt)
	{
		double dotProduct = pt.X * pt.X + pt.Y * pt.Y;
		return (float)((0.0 - D0) * (1.0 - dotProduct / (L * L)));
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) =>
		{
			double r = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
			return (float)Math.Max(TerrainElevation(pt), FuncEta(r, t));
		};
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) =>
		{
			double ptTerm = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
			if (ptTerm < 1E-12)
			{
				return new Vector(0.0, 0.0);
			}
			double omegaTerm = Omega * ptTerm * A * Math.Sin(Omega * t) / (2.0 * (1.0 - A * Math.Cos(Omega * t)));
			Vector vector = new Vector(pt.X, pt.Y);
			vector.Normalize();
			return vector * omegaTerm;
		};
	}

	public override Func<Point, float> InitialStageFunction()
	{
		Func<Point, double, float> fxn = AnalyticalStageFunction();
		return (Point pt) => fxn(pt, 0.0);
	}

	private double FuncEta(double r, double t)
	{
		double d = Omega * t;
		double computed = A * A;
		return D0 * (Math.Sqrt(1.0 - computed) / (1.0 - A * Math.Cos(d)) - 1.0 - r * r / (L * L) * ((1.0 - computed) / Math.Pow(1.0 - A * Math.Cos(d), 2.0) - 1.0));
	}

	public override Mesh CreateMesh()
	{
		Mesh mesh = ((MeshKind != CircularMeshKind.UniformTriangle) ? MeshFactory.CircularMesh(SystemExtent, CellSize) : MeshFactory.GenCircularTriangleMesh(SystemExtent, (double _) => CellSize));
		Mesh mesh2 = mesh;
		if (ReIndexMesh)
		{
			mesh2 = mesh2.ReindexMesh(QuantumSize);
		}
		return mesh2;
	}

	public override bool TryCreateTopology(out Topology topology)
	{
		if (MeshKind != CircularMeshKind.UniformTriangle)
		{
			topology = null;
			return false;
		}
		topology = MeshFactory.MakeCircularTriangleTopology(SystemExtent, (double _) => CellSize);
		return true;
	}

	public override void InitPlanOptions(Plan pl)
	{
		pl.SolverType = (UseGPU ? SolverType.GPU : SolverType.CPU);
		pl.UseGPUAdaptive = true;
		pl.LtsLevels = (SolverControl.LtsLevelOptions)LtsLevels;
		pl.UseFBF = UseFBF;
	}

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		Func<Point, double, float> func = AnalyticalStageFunction();
		for (int i = 0; i < state.CellCount; i++)
		{
			Point point = mesh.CellCenters[i];
			double funcPointTerm = Math.Max(0.0, func(point, 0.0) - TerrainElevation(point));
			state.CellVolume[i] = funcPointTerm * mesh.CellPolygonArea(i);
		}
		state.UpdateCells(null, null, null);
	}
}
