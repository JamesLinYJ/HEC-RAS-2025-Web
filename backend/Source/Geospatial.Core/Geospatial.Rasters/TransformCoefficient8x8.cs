using System;
using CommunityToolkit.HighPerformance;

namespace Geospatial.Rasters;

public struct TransformCoefficient8x8
{
	public readonly double A;

	public readonly double B;

	public readonly double C;

	public readonly double D;

	public readonly double E;

	public readonly double F;

	public readonly double G;

	public readonly double H;

	public TransformCoefficient8x8(double[,] rhs, int m)
	{
		A = Clean(rhs[0, m]);
		B = Clean(rhs[1, m]);
		C = Clean(rhs[2, m]);
		D = Clean(rhs[3, m]);
		E = Clean(rhs[4, m]);
		F = Clean(rhs[5, m]);
		G = Clean(rhs[6, m]);
		H = Clean(rhs[7, m]);
	}

	public TransformCoefficient8x8(Span2D<double> rhs, int m)
	{
		A = Clean(rhs[0, m]);
		B = Clean(rhs[1, m]);
		C = Clean(rhs[2, m]);
		D = Clean(rhs[3, m]);
		E = Clean(rhs[4, m]);
		F = Clean(rhs[5, m]);
		G = Clean(rhs[6, m]);
		H = Clean(rhs[7, m]);
	}

	private static double Clean(double input)
	{
		return Math.Round(input, 10);
	}

	public void RasterRowCoefficients(double tr, out double intercept, out double coefSC, out double coefSC2)
	{
		intercept = A + (C + F * tr) * tr;
		coefSC = B + (D + H * tr) * tr;
		coefSC2 = E + G * tr;
	}

	public void RasterRowCoefficients(int tr, out double intercept, out double coefSC, out double coefSC2)
	{
		RasterRowCoefficients((double)tr + 0.5, out intercept, out coefSC, out coefSC2);
	}

	public double TransformOordinate(double tr, double tc)
	{
		return A + B * tc + C * tr + D * tc * tr + E * tc * tc + F * tr * tr + G * tc * tc * tr + H * tr * tr * tc;
	}
}
