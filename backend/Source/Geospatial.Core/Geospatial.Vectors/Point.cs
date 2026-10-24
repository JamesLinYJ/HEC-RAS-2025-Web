using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Geospatial.Clipper2Lib;
using Utility;
using Utility.Hashing;
using Utility.Systems;

namespace Geospatial.Vectors;

[StructLayout(LayoutKind.Explicit)]
[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public struct Point : IPoint, IPoint3D, IPoint4D, IEquatable<Point>, IStreamableObject, ICloneable, IExtent
{
	public static readonly Point NullPoint = new Point(double.MaxValue, double.MaxValue);

	public static readonly Point Origin = new Point(0.0, 0.0);

	[FieldOffset(0)]
	public double X;

	[FieldOffset(8)]
	public double Y;

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

	bool IPoint3D.HasZ => false;

	bool IPoint4D.HasM => false;

	double IPoint3D.Z
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

	public static bool IsNull(Point pt)
	{
		return pt == NullPoint;
	}

	public bool IsNull()
	{
		return IsNull(this);
	}

	public bool IsUndefined()
	{
		if (!double.IsNaN(X))
		{
			return double.IsNaN(Y);
		}
		return true;
	}

	public bool IsInfinity()
	{
		if (!double.IsInfinity(X))
		{
			return double.IsInfinity(Y);
		}
		return true;
	}

	public bool IsValid()
	{
		if (!IsUndefined() && !IsInfinity())
		{
			return !IsNull();
		}
		return false;
	}

	public void Negate()
	{
		X = 0.0 - X;
		Y = 0.0 - Y;
	}

	public Point(Point pt, double scale)
	{
		X = pt.X * scale;
		Y = pt.Y * scale;
	}

	public Point(Point64 pt, double scale)
	{
		X = (double)pt.X * scale;
		Y = (double)pt.Y * scale;
	}

	public Point(Point64 pt)
	{
		X = pt.X;
		Y = pt.Y;
	}

	public Point(Point pt)
	{
		X = pt.X;
		Y = pt.Y;
	}

	public Point(IPoint pt)
	{
		X = pt.X;
		Y = pt.Y;
	}

	public Point(Point3D pt)
	{
		X = pt.X;
		Y = pt.Y;
	}

	public Point(Point4D pt)
	{
		X = pt.X;
		Y = pt.Y;
	}

	public Point(double x, double y)
	{
		X = x;
		Y = y;
	}

	public Point Translate(Vector vec)
	{
		return Translate(vec.DeltaX, vec.DeltaY);
	}

	public Point Move(Vector dist)
	{
		return Translate(dist);
	}

	public Point Translate(double x, double y)
	{
		return new Point(X + x, Y + y);
	}

	public Point TranslateAtAngle(double distance, double radiansAngle)
	{
		double x = Math.Cos(radiansAngle) * distance + X;
		double y = Math.Sin(radiansAngle) * distance + Y;
		return new Point(x, y);
	}

	public Point Toward(Point other, double fraction)
	{
		return Translate(To(other).Ratio(fraction));
	}

	public Point Midpoint(Point other)
	{
		return new Point(X + 0.5 * (other.X - X), Y + 0.5 * (other.Y - Y));
	}

	public double DistanceTo(Point other)
	{
		double otherTerm = X - other.X;
		double otherTerm2 = Y - other.Y;
		return Math.Sqrt(otherTerm * otherTerm + otherTerm2 * otherTerm2);
	}

	public double DistanceSquaredTo(Point other)
	{
		double otherTerm = X - other.X;
		double otherTerm2 = Y - other.Y;
		return otherTerm * otherTerm + otherTerm2 * otherTerm2;
	}

	public double DistanceTo(IPoint otherPoint)
	{
		return DistanceTo(otherPoint.X, otherPoint.Y);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public double DistanceTo(double x, double y)
	{
		return Math.Sqrt(DistanceSquaredTo(x, y));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public double DistanceSquaredTo(double x, double y)
	{
		double computed = X - x;
		double computed2 = Y - y;
		return computed * computed + computed2 * computed2;
	}

	public static double Distance(double x1, double y1, double x2, double y2)
	{
		double x1AboveX2 = x1 - x2;
		double y1AboveY2 = y1 - y2;
		return Math.Sqrt(x1AboveX2 * x1AboveX2 + y1AboveY2 * y1AboveY2);
	}

	public Vector To(Point other)
	{
		return new Vector(other.X - X, other.Y - Y);
	}

	public Vector To(double x, double y)
	{
		return new Vector(x - X, y - Y);
	}

	public bool CloseToXY(Point testPoint, double xyDistanceThreshold = 0.0)
	{
		if (Math.Abs(X - testPoint.X) <= xyDistanceThreshold)
		{
			return Math.Abs(Y - testPoint.Y) <= xyDistanceThreshold;
		}
		return false;
	}

	public bool CloseToXY(IPoint testPoint, double xyDistanceThreshold = 0.0)
	{
		if (Math.Abs(X - testPoint.X) <= xyDistanceThreshold)
		{
			return Math.Abs(Y - testPoint.Y) <= xyDistanceThreshold;
		}
		return false;
	}

	public bool EqualXY(in Point other)
	{
		if (X == other.X)
		{
			return Y == other.Y;
		}
		return false;
	}

	public static void SortByX(ref Point a, ref Point b, ref Point c)
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

	public static void SortByY(ref Point a, ref Point b, ref Point c)
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

	public static Point? TryParsePoint(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return null;
		}
		if (!s.Contains(','))
		{
			return null;
		}
		if (s.StartsWith("("))
		{
			s = s.Substring(1);
		}
		if (s.EndsWith(")"))
		{
			s = s.Substring(0, s.Length - 1);
		}
		string[] parts = s.Split(',');
		if (parts.Length != 2)
		{
			return null;
		}
		string s2 = parts[0];
		string s3 = parts[1];
		if (double.TryParse(s2, out var result) && double.TryParse(s3, out var result2))
		{
			return new Point(result, result2);
		}
		return null;
	}

	public Vector2 AsVector2()
	{
		return new Vector2((float)X, (float)Y);
	}

	object ICloneable.Clone()
	{
		return this;
	}

	public static Point operator *(Point p, double doubleValue)
	{
		return new Point(p.X * doubleValue, p.Y * doubleValue);
	}

	public static Point operator *(double doubleValue, Point p)
	{
		return p * doubleValue;
	}

	public static Point operator +(Point p1, Point p2)
	{
		return new Point(p1.X + p2.X, p1.Y + p2.Y);
	}

	public static Point operator +(Point p1, Vector v1)
	{
		return new Point(p1.X + v1.DeltaX, p1.Y + v1.DeltaY);
	}

	public static Point operator -(Point p1, Vector v1)
	{
		return new Point(p1.X - v1.DeltaX, p1.Y - v1.DeltaY);
	}

	public static Vector operator -(Point p1, Point p2)
	{
		return new Vector(p1.X - p2.X, p1.Y - p2.Y);
	}

	public static bool operator ==(Point p1, Point p2)
	{
		if (p1.X == p2.X)
		{
			return p1.Y == p2.Y;
		}
		return false;
	}

	public static bool operator !=(Point p1, Point p2)
	{
		return !(p1 == p2);
	}

	public override int GetHashCode()
	{
		return GetStableHash();
	}

	public int GetStableHash()
	{
		return StableHash.Combine(X, Y);
	}

	public int GetDotnetHash()
	{
		return HashCode.Combine(X, Y);
	}

	public override bool Equals(object obj)
	{
		if (obj is Point point)
		{
			if (point.X == X)
			{
				return point.Y == Y;
			}
			return false;
		}
		return false;
	}

	public bool Equals(Point p)
	{
		if (p.X == X)
		{
			return p.Y == Y;
		}
		return false;
	}

	IVector IPoint.To(IPoint pt)
	{
		return new Vector(pt.X - X, pt.Y - Y);
	}

	IVector3D IPoint3D.To(IPoint3D pt)
	{
		return new Vector(pt.X - X, pt.Y - Y);
	}

	IVector4D IPoint4D.To(IPoint4D pt)
	{
		return new Vector(pt.X - X, pt.Y - Y);
	}

	public override string ToString()
	{
		return $"{X},{Y}";
	}

	public string ToString(int digits)
	{
		return $"{Math.Round(X, digits)}, {Math.Round(Y, digits)}";
	}

	public string ToParentheticalString()
	{
		return $"({X},{Y})";
	}

	private string DebuggerDisplay()
	{
		return ToParentheticalString();
	}

	public void StreamIn(BinaryReader br)
	{
		X = br.ReadDouble();
		Y = br.ReadDouble();
	}

	public void StreamOut(BinaryWriter bw)
	{
		bw.Write(X);
		bw.Write(Y);
	}
}
