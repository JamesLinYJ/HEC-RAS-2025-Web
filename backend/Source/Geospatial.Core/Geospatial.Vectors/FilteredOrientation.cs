using System;
using System.Runtime.InteropServices;

namespace Geospatial.Vectors;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct FilteredOrientation
{
	public static int CounterClockwise(Point a, Point b, Point c)
	{
		double computed = b.X - a.X;
		double computed2 = b.Y - a.Y;
		double computed3 = c.X - a.X;
		double computed4 = c.Y - a.Y;
		double computed5 = Math.Max(Math.Abs(computed), Math.Abs(computed3));
		double computed6 = Math.Max(Math.Abs(computed2), Math.Abs(computed4));
		double computed7 = 8.887205737259276E-16 * computed5 * computed6;
		if (computed5 > computed6)
		{
			double computed8 = computed5;
			computed5 = computed6;
			computed6 = computed8;
		}
		if (computed5 < 1E-146)
		{
			return 0;
		}
		if (computed6 < 1E+153)
		{
			double computed9 = computed * computed4 - computed2 * computed3;
			if (computed9 > computed7)
			{
				return 1;
			}
			if (computed9 < 0.0 - computed7)
			{
				return -1;
			}
		}
		return 0;
	}
}
