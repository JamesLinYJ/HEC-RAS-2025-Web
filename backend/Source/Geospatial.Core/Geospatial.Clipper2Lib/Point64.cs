using System;
using Geospatial.Vectors;

namespace Geospatial.Clipper2Lib;

public struct Point64
{
	public long X;

	public long Y;

	public Point64(Point64 pt)
	{
		X = pt.X;
		Y = pt.Y;
	}

	public Point64(Point64 pt, double scale)
	{
		X = (long)Math.Round((double)pt.X * scale, MidpointRounding.AwayFromZero);
		Y = (long)Math.Round((double)pt.Y * scale, MidpointRounding.AwayFromZero);
	}

	public Point64(long x, long y)
	{
		X = x;
		Y = y;
	}

	public Point64(double x, double y)
	{
		X = (long)Math.Round(x, MidpointRounding.AwayFromZero);
		Y = (long)Math.Round(y, MidpointRounding.AwayFromZero);
	}

	public Point64(Point pt)
	{
		X = (long)Math.Round(pt.X, MidpointRounding.AwayFromZero);
		Y = (long)Math.Round(pt.Y, MidpointRounding.AwayFromZero);
	}

	public Point64(Point pt, double scale)
	{
		X = (long)Math.Round(pt.X * scale, MidpointRounding.AwayFromZero);
		Y = (long)Math.Round(pt.Y * scale, MidpointRounding.AwayFromZero);
	}

	public static bool operator ==(Point64 lhs, Point64 rhs)
	{
		if (lhs.X == rhs.X)
		{
			return lhs.Y == rhs.Y;
		}
		return false;
	}

	public static bool operator !=(Point64 lhs, Point64 rhs)
	{
		if (lhs.X == rhs.X)
		{
			return lhs.Y != rhs.Y;
		}
		return true;
	}

	public static Point64 operator +(Point64 lhs, Point64 rhs)
	{
		return new Point64(lhs.X + rhs.X, lhs.Y + rhs.Y);
	}

	public static Point64 operator -(Point64 lhs, Point64 rhs)
	{
		return new Point64(lhs.X - rhs.X, lhs.Y - rhs.Y);
	}

	public override readonly string ToString()
	{
		return $"{X},{Y} ";
	}

	public override readonly bool Equals(object? obj)
	{
		if (obj != null && obj is Point64 point)
		{
			return this == point;
		}
		return false;
	}

	public override readonly int GetHashCode()
	{
		return HashCode.Combine(X, Y);
	}
}
