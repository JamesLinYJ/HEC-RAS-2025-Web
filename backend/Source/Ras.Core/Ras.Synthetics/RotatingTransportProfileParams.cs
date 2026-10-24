using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public abstract class RotatingTransportProfileParams : BaseParams
{
	public const double DomainLength = 100.0;

	public const int CellsPerAxis = 120;

	public const double FlowDepth = 1.0;

	public const double RotationPeriod = 400.0;

	public const double ProfileSemiAxisY = 12.5;

	public const double ProfileSemiAxisX = 18.75;

	public double AngularVelocity => Math.PI / 200.0;

	public Point RotationCenter => new Point(50.0, 50.0);

	public Point InitialProfileCenter => new Point(50.0, 25.0);

	public override double SolveDt { get; set; } = 0.5;

	public override double SolveDuration { get; set; } = 100.0;

	public override int ReportFrequency { get; set; } = 1;

	public override Extent SystemExtent => new Extent(0.0, 100.0, 0.0, 100.0);

	public override float TerrainElevation(Point point)
	{
		return 0f;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point _) => 1f;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point _, double _) => 1f;
	}

	public override Func<Point, Vector> InitialVelocityFunction()
	{
		return AnalyticalVelocity;
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point point, double _) => AnalyticalVelocity(point);
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point point) => (float)AnalyticalVelocity(point).X;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point point) => (float)AnalyticalVelocity(point).Y;
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point point, double _) => (float)AnalyticalVelocity(point).X;
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point point, double _) => (float)AnalyticalVelocity(point).Y;
	}

	public override Mesh CreateMesh()
	{
		return MeshGenerator.FromExtent(SystemExtent, 120, 120, generateHexCells: true);
	}

	public Func<Point, double> InitialConcentrationFunction()
	{
		return InitialConcentration;
	}

	public Func<Point, double, double> AnalyticalConcentrationFunction()
	{
		return AnalyticalConcentration;
	}

	public Point RotatePoint(Point point, double time)
	{
		Point rotationCenter = RotationCenter;
		double angularVelocityTimesTime = AngularVelocity * time;
		double computed = Math.Cos(angularVelocityTimesTime);
		double computed2 = Math.Sin(angularVelocityTimesTime);
		double pointRotationCenterTerm = point.X - rotationCenter.X;
		double pointRotationCenterTerm2 = point.Y - rotationCenter.Y;
		return new Point(rotationCenter.X + pointRotationCenterTerm * computed - pointRotationCenterTerm2 * computed2, rotationCenter.Y + pointRotationCenterTerm * computed2 + pointRotationCenterTerm2 * computed);
	}

	public Point ProfileCenter(double time)
	{
		return RotatePoint(InitialProfileCenter, time);
	}

	public TransportFlowField CreateTransportFlowField(Mesh mesh)
	{
		HydraulicMeshComputer metadataCache = mesh.MetadataCache;
		double[] doubleValues = new double[mesh.FaceCount];
		double[] doubleValuesArray = new double[mesh.FaceCount];
		double[] doubleValues2 = new double[mesh.CellCount];
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			double faceSimpleLengthMeshTerm = (doubleValuesArray[i] = 1.0 * mesh.FaceSimpleLength(i));
			if (mesh.Faces[i].cellB >= 0)
			{
				Vector vector = AnalyticalVelocity(mesh.FaceSegment(i).Midpoint());
				Vector other = metadataCache.FaceNormal[i];
				doubleValues[i] = vector.Dot(other) * faceSimpleLengthMeshTerm;
			}
		}
		for (int j = 0; j < mesh.CellCount; j++)
		{
			doubleValues2[j] = 1.0 * metadataCache.CellAreas[j];
		}
		return TransportFlowField.Steady(doubleValues, doubleValuesArray, doubleValues2);
	}

	protected abstract double ProfileConcentration(double normalizedRadius);

	private double InitialConcentration(Point point)
	{
		return ProfileConcentration(NormalizedRadius(point));
	}

	private double AnalyticalConcentration(Point point, double time)
	{
		Point point2 = RotatePoint(point, 0.0 - time);
		return InitialConcentration(point2);
	}

	private double NormalizedRadius(Point point)
	{
		Point initialProfileCenter = InitialProfileCenter;
		double pointInitialProfileCenterTerm = (point.X - initialProfileCenter.X) / 18.75;
		double pointInitialProfileCenterTerm2 = (point.Y - initialProfileCenter.Y) / 12.5;
		return Math.Sqrt(pointInitialProfileCenterTerm * pointInitialProfileCenterTerm + pointInitialProfileCenterTerm2 * pointInitialProfileCenterTerm2);
	}

	private Vector AnalyticalVelocity(Point point)
	{
		Point rotationCenter = RotationCenter;
		return new Vector((0.0 - AngularVelocity) * (point.Y - rotationCenter.Y), AngularVelocity * (point.X - rotationCenter.X));
	}
}
