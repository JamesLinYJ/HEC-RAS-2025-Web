using System;
using System.Diagnostics;

namespace RasMapperLib;

[DebuggerDisplay("({X}, {Y}, {Z}, {M})")]
public struct BigPointM
{
	public decimal X;

	public decimal Y;

	public BigPointM(decimal ax, decimal ay)
	{
		this = default;
		X = ax;
		Y = ay;
	}

	public BigPointM(double ax, double ay)
	{
		this = default;
		X = new decimal(ax);
		Y = new decimal(ay);
	}

	public BigPointM(Point2Double pt2D)
		: this(pt2D.X, pt2D.Y)
	{
	}

	public BigPointM(PointM ptm)
		: this(ptm.X, ptm.Y)
	{
	}

	public decimal DistanceTo(BigPointM ptB)
	{
		return DistanceTo(ptB.X, ptB.Y);
	}

	public decimal DistanceTo(decimal ax, decimal ay)
	{
		decimal dx = decimal.Subtract(X, ax);
		decimal dy = decimal.Subtract(Y, ay);
		return DecimalLength(dx, dy);
	}

	public bool CloseTo(BigPointM point, decimal distance)
	{
		return CloseTo(point.X, point.Y, distance);
	}

	public bool CloseTo(decimal px, decimal py, decimal distance)
	{
		decimal absoluteSubtract = Math.Abs(decimal.Subtract(X, px));
		if (decimal.Compare(absoluteSubtract, distance) > 0)
		{
			return false;
		}
		decimal absoluteSubtract2 = Math.Abs(decimal.Subtract(Y, py));
		if (decimal.Compare(absoluteSubtract2, distance) > 0)
		{
			return false;
		}
		if (decimal.Compare(DecimalLength(absoluteSubtract, absoluteSubtract2), distance) > 0)
		{
			return false;
		}
		return true;
	}

	public static decimal DecimalSQRT(decimal X, decimal epsilon = 0.0m)
	{
		decimal divideTerm = new decimal(Math.Sqrt(Convert.ToDouble(X)));
		decimal computed = 0m;
		do
		{
			computed = divideTerm;
			if (decimal.Compare(computed, 0.0m) == 0)
			{
				return 0m;
			}
			divideTerm = decimal.Divide(decimal.Add(computed, decimal.Divide(X, computed)), 2m);
		}
		while (decimal.Compare(Math.Abs(decimal.Subtract(computed, divideTerm)), epsilon) > 0);
		return divideTerm;
	}

	public static decimal DecimalLength(decimal dx, decimal dy)
	{
		decimal d = decimal.Multiply(dx, dx);
		decimal d2 = decimal.Multiply(dy, dy);
		return DecimalSQRT(decimal.Add(d, d2));
	}
}
