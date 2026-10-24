using System.Collections.Generic;
using Geospatial.Vectors;

namespace Ras.Layers.Terrains;

public static class ChannelSectionHelpers
{
	public static double GetDistanceOfPointIdx(Polyline line, int pointIdx)
	{
		if (pointIdx >= line.PointCount - 1)
		{
			return line.Length;
		}
		return line.DistanceAlong(line.PointOnLine(pointIdx, 0.0));
	}

	public static Polyline TrimLineAgainstStartEnd(Polyline lineToTrim, Polyline start, Polyline end, double tolerance = 0.05)
	{
		PointOnPolyline aIntersection = lineToTrim.GetFirstIntersection(start).Value.AIntersection;
		double distanceAlong = lineToTrim.DistanceAlong(aIntersection);
		PointOnPolyline aIntersection2 = lineToTrim.GetFirstIntersection(end).Value.AIntersection;
		double distanceAlong2 = lineToTrim.DistanceAlong(aIntersection2);
		List<Point> points = new List<Point>();
		for (int i = 0; i < lineToTrim.PointCount; i++)
		{
			double distanceOfPointIdx = GetDistanceOfPointIdx(lineToTrim, i);
			if (distanceOfPointIdx + tolerance >= distanceAlong && distanceOfPointIdx - tolerance <= distanceAlong2)
			{
				points.Add(lineToTrim.Points[i]);
			}
		}
		Polyline polyline = new Polyline(points);
		if (polyline.Points[0].DistanceTo(aIntersection.Point) > tolerance)
		{
			polyline.Insert(0, aIntersection.Point);
		}
		else
		{
			polyline.Points[0] = aIntersection.Point;
		}
		if (polyline.Points[polyline.PointCount - 1].DistanceTo(aIntersection2.Point) > tolerance)
		{
			polyline.Add(aIntersection2.Point);
		}
		else
		{
			polyline.Points[polyline.Points.Count - 1] = aIntersection2.Point;
		}
		return polyline;
	}
}
