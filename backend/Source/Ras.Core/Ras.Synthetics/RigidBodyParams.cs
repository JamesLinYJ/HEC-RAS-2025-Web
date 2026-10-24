using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class RigidBodyParams : BaseParams
{
	public double Omega0 = 1E-05;

	public double HCenter = 1.0;

	public bool ParabolicIC = true;

	public double CellSize;

	public double Radius;

	public override Extent SystemExtent => new Extent(0.0 - Radius, Radius, 0.0 - Radius, Radius);

	public static RigidBodyParams Default()
	{
		return new RigidBodyParams
		{
			NValue = 0f,
			CellSize = 25.0,
			Radius = 500.0,
			Omega0 = 1E-05,
			HCenter = 1.0,
			ParabolicIC = true,
			SolveDt = 1.0,
			SolveDuration = 10000.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		return 0f;
	}

	public double FuncEta(double r)
	{
		if (ParabolicIC)
		{
			return HCenter + Omega0 * Omega0 / (2.0 * SolverControl.GravSI) * r * r;
		}
		return HCenter;
	}

	public Vector FuncVelocity(Point pnt)
	{
		Vector vector = new Vector(pnt.X, pnt.Y);
		double length = vector.Length;
		vector.Normalize();
		vector = vector.Perpendicular(clockwise: false);
		return Omega0 * length * vector;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) =>
		{
			double r = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y);
			return (float)FuncEta(r);
		};
	}

	public override Func<Point, float> InitialStageFunction()
	{
		Func<Point, double, float> fxn = AnalyticalStageFunction();
		return (Point pt) => fxn(pt, 0.0);
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) => FuncVelocity(pt);
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point pt, double t) => (float)FuncVelocity(pt).Length;
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point pt, double t) => (float)FuncVelocity(pt).X;
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point pt, double t) => (float)FuncVelocity(pt).Y;
	}

	public override Func<Point, Vector> InitialVelocityFunction()
	{
		return (Point pt) => FuncVelocity(pt);
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point pt) => (float)FuncVelocity(pt).X;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point pt) => (float)FuncVelocity(pt).Y;
	}

	public override Mesh CreateMesh()
	{
		return MeshFactory.CircularMesh(SystemExtent, CellSize);
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
				float func4 = func(faceMidPoint, 0.0);
				state.FaceFlow[j] = segment.Length * (double)func4 * vector.Dot(other);
				state.FaceVelocity[j] = vector.Dot(other);
			}
		}
		state.UpdateCells(null, null, null);
	}
}
