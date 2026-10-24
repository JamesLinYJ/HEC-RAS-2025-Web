using System;
using System.Diagnostics;

namespace Geospatial.Vectors;

[DebuggerDisplay("{DebuggerDisplay()},nq")]
public struct Segment3D : ISegment<Point3D>
{
	public const double DefaultEpsilon = 1E-06;

	public Point3D A;

	public Point3D B;

	Point3D ISegment<Point3D>.PointA
	{
		get
		{
			return A;
		}
		set
		{
			A = value;
		}
	}

	Point3D ISegment<Point3D>.PointB
	{
		get
		{
			return B;
		}
		set
		{
			B = value;
		}
	}

	public double MinX => Math.Min(A.X, B.X);

	public double MaxX => Math.Max(A.X, B.X);

	public double MaxY => Math.Max(A.Y, B.Y);

	public double MinY => Math.Min(A.Y, B.Y);

	public double DeltaX => B.X - A.X;

	public double DeltaY => B.Y - A.Y;

	public double DeltaZ => B.Z - A.Z;

	public double Width => DeltaX;

	public double Height => DeltaY;

	public double Length2D => Point.Distance(A.X, A.Y, B.X, B.Y);

	public double Length3D => Point3D.Distance(A.X, A.Y, B.X, B.Y, A.Z, B.Z);

	public double Slope => DeltaY / DeltaX;

	public Point3D MidPoint => PointAtFraction(0.5);

	public Segment3D(Point3D ptA, Point3D ptB)
	{
		A = ptA;
		B = ptB;
	}

	public Point3D PointAtFraction(double t)
	{
		return new Point3D(A.X + t * DeltaX, A.Y + t * DeltaY, A.Z + t * DeltaZ);
	}

	public Segment AsSegment2D()
	{
		return new Segment(A.AsPoint2D(), B.AsPoint2D());
	}

	private string DebuggerDisplay()
	{
		return $"{A}->{B}";
	}
}
