using System;
using Geospatial.Vectors;

namespace Geospatial.VelocityFields;

public class CircularStaticVelocityField : IStaticVelocityField
{
	public Point Center { get; private set; }

	public CircularStaticVelocityField(Point center)
	{
		Center = center;
	}

	public Vector GetVelocity(Point point)
	{
		Vector result = Center.To(point);
		float computed = (float)result.Length;
		if (result.X == 0.0 && result.Y == 0.0)
		{
			return result;
		}
		Vector vector = result.Perpendicular(clockwise: true);
		vector.Normalize();
		return vector * computed;
	}

	public Point GetPosition(Point start, float time)
	{
		double startTerm = Math.Atan2(start.Y, start.X);
		double distanceTo = Center.DistanceTo(start);
		double piDistanceToTerm = Math.PI * 2.0 * distanceTo;
		double getVelocityStartTerm = GetVelocity(start).Length * (double)time / piDistanceToTerm;
		double piDistanceToTerm2 = (getVelocityStartTerm - Math.Truncate(getVelocityStartTerm)) * 2.0 * Math.PI * distanceTo;
		double naN = double.NaN;
		naN = ((1 == 0) ? (startTerm + piDistanceToTerm2) : (startTerm - piDistanceToTerm2));
		double xComponent = Math.Cos(naN) * distanceTo;
		double yComponent = Math.Sin(naN) * distanceTo;
		return new Point((float)xComponent, (float)yComponent);
	}
}
