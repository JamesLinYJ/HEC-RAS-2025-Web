using System;

namespace RasMapperLib;

public struct CircleM
{
	public double X;

	public double Y;

	public double R;

	public void Assign(double ax, double ay, double ar)
	{
		X = ax;
		Y = ay;
		R = ar;
	}

	public void Compute(PointM A, PointM B, PointM C)
	{
		ComputeCenter(A, B, C, ref X, ref Y);
		R = A.DistanceTo(X, Y);
	}

	public static void ComputeCenter(PointM A, PointM B, PointM C, ref double X1, ref double Y1)
	{
		double computed = B.X - A.X;
		double computed2 = B.Y - A.Y;
		double computed3 = computed * computed + computed2 * computed2;
		double computed4 = C.X - A.X;
		double computed5 = C.Y - A.Y;
		double computed6 = computed4 * computed4 + computed5 * computed5;
		double computed7 = 2.0 * (computed * computed5 - computed2 * computed4);
		if (computed7 == 0.0)
		{
			throw new Exception("Colinear points");
		}
		X1 = A.X + (computed5 * computed3 - computed2 * computed6) / computed7;
		Y1 = A.Y + (computed * computed6 - computed4 * computed3) / computed7;
	}

	public bool ContainsPoint(PointM point)
	{
		return point.CloseTo(X, Y, R);
	}

	public PointM Center()
	{
		return new PointM(X, Y);
	}
}
