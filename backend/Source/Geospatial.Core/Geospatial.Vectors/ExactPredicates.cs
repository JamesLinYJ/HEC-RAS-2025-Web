using System.Runtime.InteropServices;

namespace Geospatial.Vectors;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct ExactPredicates : IPredicates
{
	public static double CounterClockwiseNative(Point a, Point b, Point c)
	{
		return Vector.CrossProduct(a.X, a.Y, b.X, b.Y, c.X, c.Y);
	}

	public static double CounterClockwise(Point a, Point b, Point c)
	{
		ExactFloat exactFloat = new ExactFloat(a.X);
		ExactFloat exactFloat2 = new ExactFloat(a.Y);
		ExactFloat exactFloat3 = new ExactFloat(b.X);
		ExactFloat exactFloat4 = new ExactFloat(b.Y);
		ExactFloat exactFloat5 = new ExactFloat(c.X);
		ExactFloat exactFloat6 = new ExactFloat(c.Y);
		return ((exactFloat3 - exactFloat) * (exactFloat6 - exactFloat2) - (exactFloat4 - exactFloat2) * (exactFloat5 - exactFloat)).S.CompareTo(0L);
	}

	public static double InCircleNative(Point a, Point b, Point c, Point d)
	{
		double x = a.X;
		double y = a.Y;
		double x2 = b.X;
		double y2 = b.Y;
		double x3 = c.X;
		double y3 = c.Y;
		double x4 = d.X;
		double y4 = d.Y;
		double x4Term = x - x4;
		double x2AboveX4 = x2 - x4;
		double x3AboveX4 = x3 - x4;
		double y4Term = y - y4;
		double y2AboveY4 = y2 - y4;
		double y3AboveY4 = y3 - y4;
		double computed = x4Term;
		double computed2 = y4Term;
		double computed3 = x4Term * x4Term + y4Term * y4Term;
		double x2AboveX42 = x2AboveX4;
		double y2AboveY42 = y2AboveY4;
		double computed4 = x2AboveX4 * x2AboveX4 + y2AboveY4 * y2AboveY4;
		double x3AboveX42 = x3AboveX4;
		double y3AboveY42 = y3AboveY4;
		double computed5 = x3AboveX4 * x3AboveX4 + y3AboveY4 * y3AboveY4;
		double computed6 = computed * (y2AboveY42 * computed5 - y3AboveY42 * computed4);
		double computed7 = computed2 * (x2AboveX42 * computed5 - x3AboveX42 * computed4);
		double computed8 = computed3 * (x2AboveX42 * y3AboveY42 - x3AboveX42 * y2AboveY42);
		return computed6 - computed7 + computed8;
	}

	public static double InCircle(Point a, Point b, Point c, Point d)
	{
		ExactFloat exactFloat = new ExactFloat(a.X);
		ExactFloat exactFloat2 = new ExactFloat(a.Y);
		ExactFloat exactFloat3 = new ExactFloat(b.X);
		ExactFloat exactFloat4 = new ExactFloat(b.Y);
		ExactFloat exactFloat5 = new ExactFloat(c.X);
		ExactFloat exactFloat6 = new ExactFloat(c.Y);
		ExactFloat exactFloat7 = new ExactFloat(d.X);
		ExactFloat exactFloat8 = new ExactFloat(d.Y);
		ExactFloat exactFloat9 = exactFloat - exactFloat7;
		ExactFloat exactFloat10 = exactFloat3 - exactFloat7;
		ExactFloat exactFloat11 = exactFloat5 - exactFloat7;
		ExactFloat exactFloat12 = exactFloat2 - exactFloat8;
		ExactFloat exactFloat13 = exactFloat4 - exactFloat8;
		ExactFloat exactFloat14 = exactFloat6 - exactFloat8;
		ExactFloat exactFloat15 = exactFloat9;
		ExactFloat exactFloat16 = exactFloat12;
		ExactFloat exactFloat17 = exactFloat9 * exactFloat9 + exactFloat12 * exactFloat12;
		ExactFloat exactFloat18 = exactFloat10;
		ExactFloat exactFloat19 = exactFloat13;
		ExactFloat exactFloat20 = exactFloat10 * exactFloat10 + exactFloat13 * exactFloat13;
		ExactFloat exactFloat21 = exactFloat11;
		ExactFloat exactFloat22 = exactFloat14;
		ExactFloat exactFloat23 = exactFloat11 * exactFloat11 + exactFloat14 * exactFloat14;
		ExactFloat exactFloat24 = exactFloat15 * (exactFloat19 * exactFloat23 - exactFloat22 * exactFloat20);
		ExactFloat exactFloat25 = exactFloat16 * (exactFloat18 * exactFloat23 - exactFloat21 * exactFloat20);
		ExactFloat exactFloat26 = exactFloat17 * (exactFloat18 * exactFloat22 - exactFloat21 * exactFloat19);
		return (exactFloat24 - exactFloat25 + exactFloat26).S.CompareTo(0L);
	}
}
