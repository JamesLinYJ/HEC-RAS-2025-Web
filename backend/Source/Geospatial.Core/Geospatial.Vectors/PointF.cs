using System;

namespace Geospatial.Vectors;

public struct PointF(float x, float y)
{
	public float X = x;

	public float Y = y;

	public PointF Translate(float x, float y)
	{
		return new PointF(X + x, Y + y);
	}

	public float DistanceTo(PointF otherPoint)
	{
		float otherPointTerm = X - otherPoint.X;
		float otherPointTerm2 = Y - otherPoint.Y;
		return (float)Math.Sqrt(otherPointTerm * otherPointTerm + otherPointTerm2 * otherPointTerm2);
	}

	public float DistanceTo(float x, float y)
	{
		float computed = X - x;
		float computed2 = Y - y;
		return (float)Math.Sqrt(computed * computed + computed2 * computed2);
	}

	public override string ToString()
	{
		return "(" + X + ", " + Y + ")";
	}
}
