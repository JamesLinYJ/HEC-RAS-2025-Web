using System;
using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public struct LineM
{
	private double _a;

	private double _b;

	private double _c;

	private double _distanceDenominator;

	public double A => _a;

	public double B => _b;

	public double C => _c;

	public bool IsHorizontal => _a == 0.0;

	public bool IsVertical => _b == 0.0;

	public LineM(double x0, double y0, double x1, double y1)
	{
		this = default;
		Assign(x0, y0, x1, y1);
		_a = y0 - y1;
		_b = x1 - x0;
		_c = (0.0 - _a) * x0 - _b * y0;
		_distanceDenominator = Math.Sqrt(Math.Pow(_a, 2.0) + Math.Pow(_b, 2.0));
	}

	public void Assign(PointM A, PointM B)
	{
		Assign(A.X, A.Y, B.X, B.Y);
	}

	public void Assign(double x0, double y0, double x1, double y1)
	{
		_a = y0 - y1;
		_b = x1 - x0;
		_c = (0.0 - _a) * x0 - _b * y0;
		_distanceDenominator = Math.Sqrt(Math.Pow(_a, 2.0) + Math.Pow(_b, 2.0));
	}

	public double DistanceToPoint(Point point)
	{
		return DistanceToPoint(point.X, point.Y);
	}

	public double DistanceToPoint(PointM pt)
	{
		return DistanceToPoint(pt.X, pt.Y);
	}

	public double DistanceToPoint(double x, double y)
	{
		return Math.Abs(_a * x + _b * y + _c) / _distanceDenominator;
	}

	public double X(double y)
	{
		if (_a == 0.0)
		{
			return double.NaN;
		}
		return (0.0 - (_b * y + C)) / _a;
	}

	public double Y(double x)
	{
		if (_b == 0.0)
		{
			return double.NaN;
		}
		return (0.0 - (_a * x + C)) / _b;
	}

	public static PointM LineLineIntersection(SegmentM segA, SegmentM segB)
	{
		double deltaX = segA.DeltaX();
		double deltaY = segA.DeltaY();
		double deltaX2 = segB.DeltaX();
		double deltaY2 = segB.DeltaY();
		double deltaXDeltaY2Term = deltaX * deltaY2 - deltaY * deltaX2;
		double segATerm = segA.B.X * segA.A.Y - segA.B.Y * segA.A.X;
		double segBTerm = segB.B.X * segB.A.Y - segB.B.Y * segB.A.X;
		PointM result = default;
		result.X = (segATerm * deltaX2 - deltaX * segBTerm) / deltaXDeltaY2Term;
		result.Y = (segATerm * deltaY2 - deltaY * segBTerm) / deltaXDeltaY2Term;
		return result;
	}
}
