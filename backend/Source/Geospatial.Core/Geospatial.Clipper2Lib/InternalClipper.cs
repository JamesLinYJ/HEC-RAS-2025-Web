using System;
using System.Runtime.CompilerServices;
using Geospatial.Vectors;

namespace Geospatial.Clipper2Lib;

public static class InternalClipper
{
	public struct UInt128Struct
	{
		public ulong lo64;

		public ulong hi64;
	}

	internal const long MaxInt64 = long.MaxValue;

	internal const long MaxCoord = 2305843009213693951L;

	internal const double max_coord = 2.305843009213694E+18;

	internal const double min_coord = -2.305843009213694E+18;

	internal const long Invalid64 = long.MaxValue;

	internal const double floatingPointTolerance = 1E-12;

	internal const double defaultMinimumEdgeLength = 0.1;

	private static readonly string precision_range_error = "Error: Precision is out of range.";

	public static double CrossProduct(Point64 pt1, Point64 pt2, Point64 pt3)
	{
		return (double)(pt2.X - pt1.X) * (double)(pt3.Y - pt2.Y) - (double)(pt2.Y - pt1.Y) * (double)(pt3.X - pt2.X);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void CheckPrecision(int precision)
	{
		if (precision < -8 || precision > 8)
		{
			throw new Exception(precision_range_error);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static bool IsAlmostZero(double value)
	{
		return Math.Abs(value) <= 1E-12;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int TriSign(long x)
	{
		if (x < 0)
		{
			return -1;
		}
		return (x > 1) ? 1 : 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static UInt128Struct MultiplyUInt64(ulong a, ulong b)
	{
		ulong num = (a & 0xFFFFFFFFu) * (b & 0xFFFFFFFFu);
		ulong num2 = (a >> 32) * (b & 0xFFFFFFFFu) + (num >> 32);
		ulong num3 = (a & 0xFFFFFFFFu) * (b >> 32) + (num2 & 0xFFFFFFFFu);
		UInt128Struct result = default;
		result.lo64 = ((num3 & 0xFFFFFFFFu) << 32) | (num & 0xFFFFFFFFu);
		result.hi64 = (a >> 32) * (b >> 32) + (num2 >> 32) + (num3 >> 32);
		return result;
	}

	internal static bool ProductsAreEqual(long a, long b, long c, long d)
	{
		ulong a2 = (ulong)Math.Abs(a);
		ulong b2 = (ulong)Math.Abs(b);
		long a3 = Math.Abs(c);
		ulong b3 = (ulong)Math.Abs(d);
		UInt128Struct uInt128Struct = MultiplyUInt64(a2, b2);
		UInt128Struct uInt128Struct2 = MultiplyUInt64((ulong)a3, b3);
		int num = TriSign(a) * TriSign(b);
		int num2 = TriSign(c) * TriSign(d);
		if (uInt128Struct.lo64 == uInt128Struct2.lo64 && uInt128Struct.hi64 == uInt128Struct2.hi64)
		{
			return num == num2;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static bool IsCollinear(Point64 pt1, Point64 sharedPt, Point64 pt2)
	{
		long a = sharedPt.X - pt1.X;
		long b = pt2.Y - sharedPt.Y;
		long c = sharedPt.Y - pt1.Y;
		long d = pt2.X - sharedPt.X;
		return ProductsAreEqual(a, b, c, d);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static double DotProduct(Point64 pt1, Point64 pt2, Point64 pt3)
	{
		return (double)(pt2.X - pt1.X) * (double)(pt3.X - pt2.X) + (double)(pt2.Y - pt1.Y) * (double)(pt3.Y - pt2.Y);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static double CrossProduct(Point vec1, Point vec2)
	{
		return vec1.Y * vec2.X - vec2.Y * vec1.X;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static double DotProduct(Point vec1, Point vec2)
	{
		return vec1.X * vec2.X + vec1.Y * vec2.Y;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long CheckCastInt64(double val)
	{
		if (val >= 2.305843009213694E+18 || val <= -2.305843009213694E+18)
		{
			return long.MaxValue;
		}
		return (long)Math.Round(val, MidpointRounding.AwayFromZero);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool GetSegmentIntersectPt(Point64 ln1a, Point64 ln1b, Point64 ln2a, Point64 ln2b, out Point64 ip)
	{
		double num = ln1b.Y - ln1a.Y;
		double num2 = ln1b.X - ln1a.X;
		double num3 = ln2b.Y - ln2a.Y;
		double num4 = ln2b.X - ln2a.X;
		double num5 = num * num4 - num3 * num2;
		if (num5 == 0.0)
		{
			ip = default;
			return false;
		}
		double num6 = ((double)(ln1a.X - ln2a.X) * num3 - (double)(ln1a.Y - ln2a.Y) * num4) / num5;
		if (num6 <= 0.0)
		{
			ip = ln1a;
		}
		else if (num6 >= 1.0)
		{
			ip = ln1b;
		}
		else
		{
			ip.X = (long)((double)ln1a.X + num6 * num2);
			ip.Y = (long)((double)ln1a.Y + num6 * num);
		}
		return true;
	}

	internal static bool SegsIntersect(Point64 seg1a, Point64 seg1b, Point64 seg2a, Point64 seg2b, bool inclusive = false)
	{
		if (!inclusive)
		{
			if (CrossProduct(seg1a, seg2a, seg2b) * CrossProduct(seg1b, seg2a, seg2b) < 0.0)
			{
				return CrossProduct(seg2a, seg1a, seg1b) * CrossProduct(seg2b, seg1a, seg1b) < 0.0;
			}
			return false;
		}
		double num = CrossProduct(seg1a, seg2a, seg2b);
		double num2 = CrossProduct(seg1b, seg2a, seg2b);
		if (num * num2 > 0.0)
		{
			return false;
		}
		double num3 = CrossProduct(seg2a, seg1a, seg1b);
		double num4 = CrossProduct(seg2b, seg1a, seg1b);
		if (num3 * num4 > 0.0)
		{
			return false;
		}
		if (num == 0.0 && num2 == 0.0 && num3 == 0.0)
		{
			return num4 != 0.0;
		}
		return true;
	}

	public static Rect64 GetBounds(Path64 path)
	{
		if (path.Count == 0)
		{
			return default;
		}
		Rect64 invalidRect = Clipper.InvalidRect64;
		foreach (Point64 item in path)
		{
			if (item.X < invalidRect.left)
			{
				invalidRect.left = item.X;
			}
			if (item.X > invalidRect.right)
			{
				invalidRect.right = item.X;
			}
			if (item.Y < invalidRect.top)
			{
				invalidRect.top = item.Y;
			}
			if (item.Y > invalidRect.bottom)
			{
				invalidRect.bottom = item.Y;
			}
		}
		return invalidRect;
	}

	public static Point64 GetClosestPtOnSegment(Point64 offPt, Point64 seg1, Point64 seg2)
	{
		if (seg1.X == seg2.X && seg1.Y == seg2.Y)
		{
			return seg1;
		}
		double num = seg2.X - seg1.X;
		double num2 = seg2.Y - seg1.Y;
		double num3 = ((double)(offPt.X - seg1.X) * num + (double)(offPt.Y - seg1.Y) * num2) / (num * num + num2 * num2);
		if (num3 < 0.0)
		{
			num3 = 0.0;
		}
		else if (num3 > 1.0)
		{
			num3 = 1.0;
		}
		return new Point64((double)seg1.X + Math.Round(num3 * num, MidpointRounding.ToEven), (double)seg1.Y + Math.Round(num3 * num2, MidpointRounding.ToEven));
	}

	public static PointInPolygonResult PointInPolygon(Point64 pt, Path64 polygon)
	{
		int count = polygon.Count;
		int i = 0;
		if (count < 3)
		{
			return PointInPolygonResult.IsOutside;
		}
		for (; i < count && polygon[i].Y == pt.Y; i++)
		{
		}
		if (i == count)
		{
			return PointInPolygonResult.IsOutside;
		}
		bool flag = polygon[i].Y < pt.Y;
		bool flag2 = flag;
		int num = 0;
		int j = i + 1;
		int num2 = count;
		double num3;
		while (true)
		{
			if (j == num2)
			{
				if (num2 == 0 || i == 0)
				{
					break;
				}
				num2 = i;
				j = 0;
			}
			if (flag)
			{
				for (; j < num2 && polygon[j].Y < pt.Y; j++)
				{
				}
			}
			else
			{
				for (; j < num2 && polygon[j].Y > pt.Y; j++)
				{
				}
			}
			if (j == num2)
			{
				continue;
			}
			Point64 pt2 = polygon[j];
			Point64 pt3 = ((j <= 0) ? polygon[count - 1] : polygon[j - 1]);
			if (pt2.Y == pt.Y)
			{
				if (pt2.X == pt.X || (pt2.Y == pt3.Y && pt.X < pt3.X != pt.X < pt2.X))
				{
					return PointInPolygonResult.IsOn;
				}
				j++;
				if (j == i)
				{
					break;
				}
				continue;
			}
			if (pt.X >= pt2.X || pt.X >= pt3.X)
			{
				if (pt.X > pt3.X && pt.X > pt2.X)
				{
					num = 1 - num;
				}
				else
				{
					num3 = CrossProduct(pt3, pt2, pt);
					if (num3 == 0.0)
					{
						return PointInPolygonResult.IsOn;
					}
					if (num3 < 0.0 == flag)
					{
						num = 1 - num;
					}
				}
			}
			flag = !flag;
			j++;
		}
		if (flag == flag2)
		{
			if (num != 0)
			{
				return PointInPolygonResult.IsInside;
			}
			return PointInPolygonResult.IsOutside;
		}
		if (j == count)
		{
			j = 0;
		}
		num3 = ((j == 0) ? CrossProduct(polygon[count - 1], polygon[0], pt) : CrossProduct(polygon[j - 1], polygon[j], pt));
		if (num3 == 0.0)
		{
			return PointInPolygonResult.IsOn;
		}
		if (num3 < 0.0 == flag)
		{
			num = 1 - num;
		}
		if (num != 0)
		{
			return PointInPolygonResult.IsInside;
		}
		return PointInPolygonResult.IsOutside;
	}

	public static bool Path2ContainsPath1(Path64 path1, Path64 path2)
	{
		PointInPolygonResult pointInPolygonResult = PointInPolygonResult.IsOn;
		foreach (Point64 item in path1)
		{
			switch (PointInPolygon(item, path2))
			{
			case PointInPolygonResult.IsOutside:
				if (pointInPolygonResult == PointInPolygonResult.IsOutside)
				{
					return false;
				}
				pointInPolygonResult = PointInPolygonResult.IsOutside;
				break;
			case PointInPolygonResult.IsInside:
				if (pointInPolygonResult == PointInPolygonResult.IsInside)
				{
					return true;
				}
				pointInPolygonResult = PointInPolygonResult.IsInside;
				break;
			}
		}
		return PointInPolygon(GetBounds(path1).MidPoint(), path2) != PointInPolygonResult.IsOutside;
	}
}
