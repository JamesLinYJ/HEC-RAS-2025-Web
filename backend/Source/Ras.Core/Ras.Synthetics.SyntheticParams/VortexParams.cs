using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Layers;

namespace Ras.Synthetics.SyntheticParams;

public class VortexParams : BaseParams
{
	public enum CircularMeshKind
	{
		UniformVoronoi,
		GradedTriangle,
		GradedVoronoi
	}

	public double R = 1000.0;

	public double r0 = 100.0;

	public double h0 = 10.0;

	public double U0 = 0.2;

	public bool UseGPU;

	public int LtsLevels = 3;

	public bool UseFBF = true;

	public double CellSize = 25.0;

	public double CenterCellSize = 10.0;

	public double EdgeCellSize = 50.0;

	public CircularMeshKind MeshKind;

	public bool ReIndexMesh { get; set; }

	public int QuantumSize { get; set; } = 128;

	public override double SolveDuration => 5000.0;

	public override double SolveDt => 0.2;

	public override Extent SystemExtent => Extent.CenteredOn(Point.Origin, R * 2.0);

	public static VortexParams Default()
	{
		return new VortexParams
		{
			R = 1000.0,
			r0 = 100.0,
			h0 = 10.0,
			U0 = 0.2,
			SolveDt = 0.1,
			LtsLevels = 0,
			UseFBF = true,
			ReportFrequency = 100
		};
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) => ComputeStage(pt);
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return ComputeStage;
	}

	private float ComputeStage(Point pt)
	{
		double ptTerm = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
		if (ptTerm < R)
		{
			return (float)FuncEta(ptTerm);
		}
		return -9999f;
	}

	private double FuncEta(double r)
	{
		double r0Term = 2.0 * r / r0;
		return h0 + U0 * U0 / (4.0 * SolverControl.GravSI) * (1.0 - r0Term * Math.Exp(0.0 - r0Term) - Math.Exp(0.0 - r0Term));
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) => ComputeVelocity(pt);
	}

	private Vector ComputeVelocity(Point p)
	{
		double computed = Math.Sqrt(p.X * p.X + p.Y * p.Y);
		if (computed < R)
		{
			return FuncVelocity(p.X, p.Y, computed);
		}
		return new Vector(-9999.0, -9999.0);
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point pt, double t) => ComputeVelocityMagnitude(pt);
	}

	public override Func<Point, float> InitialVelocityMagnitudeFunction()
	{
		return ComputeVelocityMagnitude;
	}

	private float ComputeVelocityMagnitude(Point p)
	{
		double computed = Math.Sqrt(p.X * p.X + p.Y * p.Y);
		if (computed < R)
		{
			return (float)FuncVelocity(p.X, p.Y, computed).Length;
		}
		return -9999f;
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point pt, double t) => ComputeVelocityX(pt);
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return ComputeVelocityX;
	}

	private float ComputeVelocityX(Point p)
	{
		double computed = Math.Sqrt(p.X * p.X + p.Y * p.Y);
		if (computed < R)
		{
			return FuncVelocityX(p.Y, computed);
		}
		return -9999f;
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point pt, double t) => ComputeVelocityY(pt);
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return ComputeVelocityY;
	}

	private float ComputeVelocityY(Point p)
	{
		double computed = Math.Sqrt(p.X * p.X + p.Y * p.Y);
		if (computed < R)
		{
			return FuncVelocityY(p.X, computed);
		}
		return -9999f;
	}

	private Vector FuncVelocity(double X, double Y, double r)
	{
		Vector vector = new Vector(Y, 0.0 - X);
		return U0 * vector / r0 * Math.Exp((0.0 - r) / r0);
	}

	private float FuncVelocityX(double Y, double r)
	{
		return (float)(U0 * Y / r0 * Math.Exp((0.0 - r) / r0));
	}

	private float FuncVelocityY(double X, double r)
	{
		return (float)(U0 * (0.0 - X) / r0 * Math.Exp((0.0 - r) / r0));
	}

	public override Mesh CreateMesh()
	{
		Mesh mesh = MeshKind switch
		{
			CircularMeshKind.GradedTriangle => MeshFactory.GenCircularTriangleMesh(SystemExtent, RadialSizeFunc()), 
			CircularMeshKind.GradedVoronoi => GenCircularVoronoiMesh(SystemExtent, RadialSizeFunc()), 
			_ => MeshFactory.CircularMesh(SystemExtent, CellSize), 
		};
		if (ReIndexMesh)
		{
			mesh = mesh.ReindexMesh(QuantumSize);
		}
		return mesh;
	}

	private Func<double, double> RadialSizeFunc()
	{
		return (double r) => CenterCellSize + (EdgeCellSize - CenterCellSize) * Math.Min(Math.Max(r / R, 0.0), 1.0);
	}

	public override bool TryCreateTopology(out Topology topology)
	{
		if (MeshKind != CircularMeshKind.GradedTriangle)
		{
			topology = null;
			return false;
		}
		topology = MeshFactory.MakeCircularTriangleTopology(SystemExtent, RadialSizeFunc());
		return true;
	}

	public static Mesh GenCircularVoronoiMesh(Extent ext, Func<double, double> radialSizeFunc)
	{
		double radialSizeFunc22 = ext.Width / 2.0;
		Point center = ext.Center;
		List<Point> points = new List<Point> { center };
		double radialSizeFuncSum = 0.0;
		while (true)
		{
			double radialSizeFunc2 = radialSizeFunc(radialSizeFuncSum);
			if (radialSizeFunc2 <= 0.0)
			{
				radialSizeFunc2 = radialSizeFunc22;
			}
			if (radialSizeFunc22 - radialSizeFuncSum <= radialSizeFunc2)
			{
				break;
			}
			radialSizeFuncSum += radialSizeFunc2;
			int symmetricRingCount = MeshFactory.SymmetricRingCount(radialSizeFuncSum, radialSizeFunc(radialSizeFuncSum));
			for (int i = 0; i < symmetricRingCount; i++)
			{
				double piSymmetricRingCountTerm = Math.PI * 2.0 * (double)i / (double)symmetricRingCount;
				points.Add(new Point(center.X + radialSizeFuncSum * Math.Cos(piSymmetricRingCountTerm), center.Y + radialSizeFuncSum * Math.Sin(piSymmetricRingCountTerm)));
			}
		}
		int symmetricRingCount2 = MeshFactory.SymmetricRingCount(radialSizeFunc22, radialSizeFunc(radialSizeFunc22));
		List<Point> pointsList = new List<Point>();
		for (int j = 0; j < symmetricRingCount2; j++)
		{
			double piSymmetricRingCount2Term = Math.PI * 2.0 * (double)j / (double)symmetricRingCount2;
			pointsList.Add(new Point(center.X + radialSizeFunc22 * Math.Cos(piSymmetricRingCount2Term), center.Y + radialSizeFunc22 * Math.Sin(piSymmetricRingCount2Term)));
		}
		Polygon perimeter = new Polygon(pointsList);
		MeshGenerationParams meshGenerationParams = MeshGenerationParams.Default();
		meshGenerationParams.CreateVirtualCells = false;
		MeshFactory.TryCreateMesh(perimeter, points, null, out var mesh, out var _, meshGenerationParams);
		return mesh;
	}

	public override void InitPlanOptions(Plan pl)
	{
		pl.SolverType = (UseGPU ? SolverType.GPU : SolverType.CPU);
		pl.LtsLevels = (SolverControl.LtsLevelOptions)LtsLevels;
		pl.UseFBF = UseFBF;
		pl.UseDiffusion = false;
	}

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		Func<Point, double, float> func = AnalyticalStageFunction();
		Func<Point, double, Vector> func2 = AnalyticalVelocityFunction();
		for (int i = 0; i < state.CellCount; i++)
		{
			Point cellCenter = mesh.CellCenters[i];
			double func3 = func(cellCenter, 0.0);
			state.CellVolume[i] = func3 * mesh.CellPolygonArea(i);
		}
		for (int j = 0; j < state.FaceCount; j++)
		{
			Face face = mesh.Faces[j];
			if (face.cellA >= 0 && face.cellB >= 0)
			{
				Segment segment = mesh.FaceSegment(j);
				Vector other = -1.0 * segment.PerpendicularUnitVector();
				Point faceMidPoint = mesh.FaceMidPoint(j);
				Vector vector = func2(faceMidPoint, 0.0);
				double func4 = func(faceMidPoint, 0.0);
				double faceArea = segment.Length * func4;
				double faceVelocity = vector.Dot(other);
				state.FaceArea[j] = faceArea;
				state.FaceFlow[j] = faceArea * faceVelocity;
				state.FaceVelocity[j] = faceVelocity;
			}
		}
		state.UpdateCells(null, null, null);
	}
}
