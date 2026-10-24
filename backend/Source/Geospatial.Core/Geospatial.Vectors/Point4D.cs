using System;
using System.Diagnostics;
using Utility;

namespace Geospatial.Vectors;

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public struct Point4D : IPoint, IPoint3D, IPoint4D
{
	public double X;

	public double Y;

	public double Z;

	public double M;

	public static readonly Point4D NullPoint = new Point4D(double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue);

	double IPoint.X
	{
		get
		{
			return X;
		}
		set
		{
			X = value;
		}
	}

	double IPoint.Y
	{
		get
		{
			return Y;
		}
		set
		{
			Y = value;
		}
	}

	bool IPoint3D.HasZ => true;

	double IPoint3D.Z
	{
		get
		{
			return Z;
		}
		set
		{
			Z = value;
		}
	}

	bool IPoint4D.HasM => true;

	double IPoint4D.M
	{
		get
		{
			return M;
		}
		set
		{
			M = value;
		}
	}

	public Extent Extent => new Extent(X, X, Y, Y);

	public Point4D(Point xy, double z, double m)
	{
		X = xy.X;
		Y = xy.Y;
		Z = z;
		M = m;
	}

	public Point4D(Point3D xyz, double m)
	{
		X = xyz.X;
		Y = xyz.Y;
		Z = xyz.Z;
		M = m;
	}

	public Point4D(double x, double y, double z, double m)
	{
		X = x;
		Y = y;
		Z = z;
		M = m;
	}

	public Point4D Translate(double x, double y, double z, double m)
	{
		return new Point4D(X + x, Y + y, Z + z, M + m);
	}

	public Point4D Translate(Vector vec)
	{
		return new Point4D(X + vec.DeltaX, Y + vec.DeltaY, Z, M);
	}

	public static double Distance(double x1, double y1, double z1, double m1, double x2, double y2, double z2, double m2)
	{
		double x1AboveX2 = x1 - x2;
		double y1AboveY2 = y1 - y2;
		double z1AboveZ2 = z1 - z2;
		double m1AboveM2 = m1 - m2;
		return Math.Sqrt(x1AboveX2 * x1AboveX2 + y1AboveY2 * y1AboveY2 + z1AboveZ2 * z1AboveZ2 + m1AboveM2 * m1AboveM2);
	}

	public Point AsPoint2D()
	{
		return new Point(X, Y);
	}

	IVector IPoint.To(IPoint pt)
	{
		return new Vector(pt.X - X, pt.Y - Y);
	}

	IVector3D IPoint3D.To(IPoint3D pt)
	{
		return new Vector3D(pt.X - X, pt.Y - Y, pt.Z - Z);
	}

	IVector4D IPoint4D.To(IPoint4D pt)
	{
		return new Vector4D(pt.X - X, pt.Y - Y, pt.Z - Z, pt.M - M);
	}

	public static void SortByX(ref Point4D a, ref Point4D b, ref Point4D c)
	{
		if (!(a.X <= b.X) || !(b.X <= c.X))
		{
			if (a.X <= c.X && c.X <= b.X)
			{
				MathHelpers.Swap(ref b, ref c);
			}
			else if (b.X <= a.X && a.X <= c.X)
			{
				MathHelpers.Swap(ref a, ref b);
			}
			else if (b.X <= c.X && c.X <= a.X)
			{
				MathHelpers.Swap(ref a, ref b);
				MathHelpers.Swap(ref b, ref c);
			}
			else if (c.X <= a.X && a.X <= b.X)
			{
				MathHelpers.Swap(ref a, ref b);
				MathHelpers.Swap(ref a, ref c);
			}
			else
			{
				MathHelpers.Swap(ref a, ref c);
			}
		}
	}

	public static void SortByY(ref Point4D a, ref Point4D b, ref Point4D c)
	{
		if (!(a.Y <= b.Y) || !(b.Y <= c.Y))
		{
			if (a.Y <= c.Y && c.Y <= b.Y)
			{
				MathHelpers.Swap(ref b, ref c);
			}
			else if (b.Y <= a.Y && a.Y <= c.Y)
			{
				MathHelpers.Swap(ref a, ref b);
			}
			else if (b.Y <= c.Y && c.Y <= a.Y)
			{
				MathHelpers.Swap(ref a, ref b);
				MathHelpers.Swap(ref b, ref c);
			}
			else if (c.Y <= a.Y && a.Y <= b.Y)
			{
				MathHelpers.Swap(ref a, ref b);
				MathHelpers.Swap(ref a, ref c);
			}
			else
			{
				MathHelpers.Swap(ref a, ref c);
			}
		}
	}

	public static Point4D operator *(Point4D p, double doubleValue)
	{
		return new Point4D(p.X * doubleValue, p.Y * doubleValue, p.Z * doubleValue, p.M * doubleValue);
	}

	public static Point4D operator *(double doubleValue, Point4D p)
	{
		return p * doubleValue;
	}

	public static Point4D operator +(Point4D p1, Point4D p2)
	{
		return new Point4D(p1.X + p2.X, p1.Y + p2.Y, p1.Z + p2.Z, p1.M + p2.M);
	}

	public static Point4D operator +(Point4D p1, Vector4D v1)
	{
		return new Point4D(p1.X + v1.DeltaX, p1.Y + v1.DeltaY, p1.Z + v1.DeltaZ, p1.M + v1.DeltaM);
	}

	public static Point4D operator -(Point4D p1, Vector4D v1)
	{
		return new Point4D(p1.X - v1.DeltaX, p1.Y - v1.DeltaY, p1.Z - v1.DeltaZ, p1.M - v1.DeltaM);
	}

	public static Vector4D operator -(Point4D p1, Point4D p2)
	{
		return new Vector4D(p1.X - p2.X, p1.Y - p2.Y, p1.Z - p2.Z, p1.M - p2.M);
	}

	public override string ToString()
	{
		return DebuggerDisplay();
	}

	private string DebuggerDisplay()
	{
		return $"({X},{Y},{(double.IsNaN(Z) ? "" : Z.ToString())},{(double.IsNaN(M) ? "" : M.ToString())})";
	}
}
