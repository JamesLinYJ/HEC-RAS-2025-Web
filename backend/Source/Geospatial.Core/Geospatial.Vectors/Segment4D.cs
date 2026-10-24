using System;
using System.Diagnostics;

namespace Geospatial.Vectors;

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public struct Segment4D : ISegment<Point4D>
{
	public const double DefaultEpsilon = 1E-06;

	public Point4D A;

	public Point4D B;

	Point4D ISegment<Point4D>.PointA
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

	Point4D ISegment<Point4D>.PointB
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

	public double DeltaM => B.M - A.M;

	public double Width => DeltaX;

	public double Height => DeltaY;

	public double Length2D => Point.Distance(A.X, A.Y, B.X, B.Y);

	public double Length3D => Point3D.Distance(A.X, A.Y, A.Z, B.X, B.Y, B.Z);

	public double Length4D => Point4D.Distance(A.X, A.Y, A.Z, A.M, B.X, B.Y, B.Z, B.M);

	public double Slope => DeltaY / DeltaX;

	public Point4D MidPoint => PointAtFraction(0.5);

	public Segment4D(Point4D ptA, Point4D ptB)
	{
		A = ptA;
		B = ptB;
	}

	public Segment4D(Segment seg)
	{
		A = new Point4D(seg.A, double.NaN, double.NaN);
		B = new Point4D(seg.B, double.NaN, double.NaN);
	}

	public Segment4D Translate(Vector vec)
	{
		return new Segment4D(A.Translate(vec), B.Translate(vec));
	}

	public Point4D PointAtFraction(double t)
	{
		return new Point4D(A.X + t * DeltaX, A.Y + t * DeltaY, A.Z + t * DeltaZ, A.M + t * DeltaM);
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
