using System;

namespace Ras.Engine.Structures.Culverts;

public static class CulvertMath
{
	private static readonly double[] Gauss5Nodes = new double[5] { 0.0, 0.53846931, -0.53846931, 0.90617985, -0.90617985 };

	private static readonly double[] Gauss5Weights = new double[5] { 0.56888889, 0.47862867, 0.47862867, 0.23692689, 0.23692689 };

	public static double Gauss5Integrate(double x1, double x2, Func<double, double> f)
	{
		if (x1 == x2)
		{
			return 0.0;
		}
		double computed = 0.0;
		double x2X1Term = 0.5 * (x2 - x1);
		double x1X2Term = 0.5 * (x1 + x2);
		for (int i = 0; i < 5; i++)
		{
			double gauss5NodesTerm = x2X1Term * Gauss5Nodes[i] + x1X2Term;
			computed += f(gauss5NodesTerm) * Gauss5Weights[i];
		}
		return computed * x2X1Term;
	}

	public static double Interpolate(double x1, double x2, double xint, double y1, double y2)
	{
		double result = (y1 + y2) / 2.0;
		if (x1 != x2)
		{
			result = (x2 - xint) * (y2 - y1) / (x1 - x2) + y2;
		}
		return result;
	}
}
