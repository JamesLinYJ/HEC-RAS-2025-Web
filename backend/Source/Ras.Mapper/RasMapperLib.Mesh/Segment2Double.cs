using System;

namespace RasMapperLib.Mesh;

public struct Segment2Double
{
	public Point2Double A;

	public Point2Double B;

	public double MaxY()
	{
		return Math.Max(A.Y, B.Y);
	}

	public double MinY()
	{
		return Math.Min(A.Y, B.Y);
	}

	public double DeltaY()
	{
		return B.Y - A.Y;
	}
}
