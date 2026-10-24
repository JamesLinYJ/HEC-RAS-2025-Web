using System;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public struct QuadrilateralShape
{
	public PointM TopLeft;

	public PointM TopRight;

	public PointM BottomRight;

	public PointM BottomLeft;

	public QuadrilateralShape(PointM TopLeft, PointM TopRight, PointM BottomRight, PointM BottomLeft)
	{
		this = default;
		this.TopLeft = TopLeft;
		this.TopRight = TopRight;
		this.BottomRight = BottomRight;
		this.BottomLeft = BottomLeft;
	}

	public void Assign(PointM TopLeft, PointM TopRight, PointM BottomRight, PointM BottomLeft)
	{
		this.TopLeft = TopLeft;
		this.TopRight = TopRight;
		this.BottomRight = BottomRight;
		this.BottomLeft = BottomLeft;
	}

	public void InterpolationWeights(PointM p, ref double TopLeftWeight, ref double TopRightWeight, ref double BottomRightWeight, ref double BottomLeftWeight)
	{
		double cx = default;
		double cy = default;
		ComputationCoordinates(p, ref cx, ref cy);
		TopLeftWeight = WeightedValue(cx, cy, 1.0, 0.0, 0.0, 0.0);
		TopRightWeight = WeightedValue(cx, cy, 0.0, 1.0, 0.0, 0.0);
		BottomRightWeight = WeightedValue(cx, cy, 0.0, 0.0, 1.0, 0.0);
		BottomLeftWeight = 1.0 - TopLeftWeight - TopRightWeight - BottomRightWeight;
	}

	public void ComputationCoordinates(PointM p, ref double cx, ref double cy)
	{
		double tolerance = 1E-06;
		cx = 0.0;
		cy = 0.0;
		int ordinal = 0;
		double JXsee = default;
		double JYsee = default;
		double JXeta = default;
		double JYeta = default;
		double x = default;
		double x2 = default;
		do
		{
			double weightedValue = WeightedValue(cx, cy, TopLeft.X, TopRight.X, BottomRight.X, BottomLeft.X);
			double weightedValue2 = WeightedValue(cx, cy, TopLeft.Y, TopRight.Y, BottomRight.Y, BottomLeft.Y);
			Jacobian(cx, cy, ref JXsee, ref JYsee, ref JXeta, ref JYeta);
			RasMapperLib.Utilities.Math.Solve2x2(JXsee, JXeta, JYsee, JYeta, p.X - weightedValue, p.Y - weightedValue2, ref x, ref x2);
			cx += x;
			cy += x2;
			ordinal = checked(ordinal + 1);
		}
		while (ordinal <= 1000 && (!(System.Math.Abs(x) < tolerance) || !(System.Math.Abs(x2) < tolerance)));
	}

	public PointM GlobalCoordinates(double cx, double cy)
	{
		PointM result = default;
		result.X = WeightedValue(cx, cy, TopLeft.X, TopRight.X, BottomRight.X, BottomLeft.X);
		result.Y = WeightedValue(cx, cy, TopLeft.Y, TopRight.Y, BottomRight.Y, BottomLeft.Y);
		return result;
	}

	public double WeightedValue(double cx, double cy, double v0, double v1, double v2, double v3)
	{
		double cxTimesCy = cx * cy;
		double cxCyTerm = (1.0 - cx - cy + cxTimesCy) * v0;
		double cxCyTerm2 = (1.0 + cx - cy - cxTimesCy) * v1;
		double cxCyTerm3 = (1.0 + cx + cy + cxTimesCy) * v2;
		double cxCyTerm4 = (1.0 - cx + cy - cxTimesCy) * v3;
		return 0.25 * (cxCyTerm + cxCyTerm2 + cxCyTerm3 + cxCyTerm4);
	}

	public void Jacobian(double cx, double cy, ref double JXsee, ref double JYsee, ref double JXeta, ref double JYeta)
	{
		double cyTerm = (1.0 - cy) * 0.25;
		double cyTerm2 = (1.0 + cy) * 0.25;
		double cxTerm = (1.0 - cx) * 0.25;
		double cxTerm2 = (1.0 + cx) * 0.25;
		JXsee = cyTerm * (0.0 - TopLeft.X + TopRight.X) + cyTerm2 * (BottomRight.X - BottomLeft.X);
		JYsee = cyTerm * (0.0 - TopLeft.Y + TopRight.Y) + cyTerm2 * (BottomRight.Y - BottomLeft.Y);
		JXeta = cxTerm * (0.0 - TopLeft.X + BottomLeft.X) + cxTerm2 * (0.0 - TopRight.X + BottomRight.X);
		JYeta = cxTerm * (0.0 - TopLeft.Y + BottomLeft.Y) + cxTerm2 * (0.0 - TopRight.Y + BottomRight.Y);
	}

	public override string ToString()
	{
		return "p0:" + TopLeft.ToString() + " p1:" + TopRight.ToString() + " p2:" + BottomRight.ToString() + " p3:" + BottomLeft.ToString();
	}
}
