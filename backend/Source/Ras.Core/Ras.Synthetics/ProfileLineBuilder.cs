using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Ras.Synthetics;

internal static class ProfileLineBuilder
{
	private const double StationEpsilonFraction = 1E-09;

	public static void Build(Polyline definitionLine, IReadOnlyList<double> fractions, out Polyline resolvedLine, out int[] vertexSampleIndex)
	{
		double length = definitionLine.Length;
		double scaledLength = Math.Max(1E-12, length * 1E-09);
		int pointCountOffset = definitionLine.PointCount - 1;
		List<(double, Point, int)> pointCountPlusCount = new List<(double, Point, int)>(definitionLine.PointCount + fractions.Count);
		double[] cumulativePointStations = definitionLine.CumulativePointStations();
		for (int i = 0; i < definitionLine.PointCount; i++)
		{
			pointCountPlusCount.Add((cumulativePointStations[i], definitionLine[i], -1));
		}
		for (int j = 0; j < fractions.Count; j++)
		{
			double fractionsTerm = Math.Clamp(fractions[j], 0.0, 1.0);
			PointOnPolyline pointOnPolyline = definitionLine.PointAtFraction(fractionsTerm);
			Point point = (pointOnPolyline.IsValid() ? pointOnPolyline.Point : definitionLine[(!(fractionsTerm <= 0.0)) ? pointCountOffset : 0]);
			pointCountPlusCount.Add((fractionsTerm * length, point, j));
		}
		pointCountPlusCount.Sort(((double Station, Point Point, int SampleIndex) a, (double Station, Point Point, int SampleIndex) b) =>
		{
			int stationTerm = a.Station.CompareTo(b.Station);
			return (stationTerm != 0) ? stationTerm : ((b.SampleIndex >= 0) ? 1 : 0).CompareTo((a.SampleIndex >= 0) ? 1 : 0);
		});
		List<Point> points = new List<Point>(pointCountPlusCount.Count);
		List<int> intList = new List<int>(pointCountPlusCount.Count);
		double negativeInfinity = double.NegativeInfinity;
		foreach (var pointCountPlusCount2 in pointCountPlusCount)
		{
			if (points.Count > 0 && pointCountPlusCount2.Item1 - negativeInfinity <= scaledLength)
			{
				if (intList[intList.Count - 1] < 0 && pointCountPlusCount2.Item3 >= 0)
				{
					intList[intList.Count - 1] = pointCountPlusCount2.Item3;
					points[points.Count - 1] = pointCountPlusCount2.Item2;
				}
			}
			else
			{
				points.Add(pointCountPlusCount2.Item2);
				intList.Add(pointCountPlusCount2.Item3);
				(negativeInfinity, _, _) = pointCountPlusCount2;
			}
		}
		resolvedLine = new Polyline(points);
		vertexSampleIndex = intList.ToArray();
	}
}
