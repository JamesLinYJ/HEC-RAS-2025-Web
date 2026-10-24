using System;
using System.Diagnostics;
using Utility;

namespace Geospatial.Vectors;

[DebuggerDisplay("{DebuggerDisplay()},nq")]
public struct Point3D : IPoint, IPoint3D, IPoint4D
{
	public double X;

	public double Y;

	public double Z;

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

	bool IPoint4D.HasM => false;

	double IPoint4D.M
	{
		get
		{
			throw new NotSupportedException();
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	public Extent Extent => new Extent(X, X, Y, Y);

	public Point3D(Point xy, double z)
	{
		X = xy.X;
		Y = xy.Y;
		Z = z;
	}

	public Point3D(double x, double y, double z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	public Point3D Offset(double x, double y, double z)
	{
		return new Point3D(X + x, Y + y, Z + z);
	}

	public Point AsPoint2D()
	{
		return new Point(X, Y);
	}

	public Point3D Translate(Vector vec)
	{
		return new Point3D(X + vec.DeltaX, Y + vec.DeltaY, Z);
	}

	public static double Distance(double x1, double y1, double z1, double x2, double y2, double z2)
	{
		double x1AboveX2 = x1 - x2;
		double y1AboveY2 = y1 - y2;
		double z1AboveZ2 = z1 - z2;
		return Math.Sqrt(x1AboveX2 * x1AboveX2 + y1AboveY2 * y1AboveY2 + z1AboveZ2 * z1AboveZ2);
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
		return new Vector3D(pt.X - X, pt.Y - Y, pt.Z - Z);
	}

	public static void SortByX(ref Point3D a, ref Point3D b, ref Point3D c)
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

	public static void SortByY(ref Point3D a, ref Point3D b, ref Point3D c)
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

	public static Point3D operator *(Point3D p, double doubleValue)
	{
		return new Point3D(p.X * doubleValue, p.Y * doubleValue, p.Z * doubleValue);
	}

	public static Point3D operator *(double doubleValue, Point3D p)
	{
		return p * doubleValue;
	}

	public static Point3D operator +(Point3D p1, Point3D p2)
	{
		return new Point3D(p1.X + p2.X, p1.Y + p2.Y, p1.Z + p2.Z);
	}

	public static Point3D operator +(Point3D p1, Vector3D v1)
	{
		return new Point3D(p1.X + v1.DeltaX, p1.Y + v1.DeltaY, p1.Z + v1.DeltaZ);
	}

	public static Point3D operator -(Point3D p1, Vector3D v1)
	{
		return new Point3D(p1.X - v1.DeltaX, p1.Y - v1.DeltaY, p1.Z - v1.DeltaZ);
	}

	public static Vector3D operator -(Point3D p1, Point3D p2)
	{
		return new Vector3D(p1.X - p2.X, p1.Y - p2.Y, p1.Z - p2.Z);
	}

	public override string ToString()
	{
		return DebuggerDisplay();
	}

	private string DebuggerDisplay()
	{
		return $"({X},{Y},{Z})";
	}
}
