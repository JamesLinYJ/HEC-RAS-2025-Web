using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Vectors;

public class OffsetPolylineFactory
{
	public const double MinimumOffset = 0.1;

	public const double MinimumFilterTolerance = 0.001;

	public const double ColinearTolerance = 1E-06;

	public const double MaximumArcTolRatio = 0.9999;

	public static Polygon BufferedPolygonEdgesToPolygon(Polyline leftOffset, Polyline rightOffset)
	{
		Polygon polygon = new Polygon(leftOffset.Count + rightOffset.Count);
		for (int i = 0; i < leftOffset.Count; i++)
		{
			polygon.Add(leftOffset[i]);
		}
		for (int countOffset = rightOffset.Count - 1; countOffset >= 0; countOffset--)
		{
			polygon.Add(rightOffset[countOffset]);
		}
		return polygon;
	}

	public static void ComputeSimpleOffset(double width, Polyline centerLine, out Polyline leftOffset, out Polyline rightOffset, bool filterLine = true)
	{
		leftOffset = new Polyline();
		rightOffset = new Polyline();
		if (centerLine.HasZ)
		{
			leftOffset.TrackZ();
			rightOffset.TrackZ();
		}
		if (centerLine.HasM)
		{
			leftOffset.TrackM();
			rightOffset.TrackM();
		}
		double scaledWidth = width * 0.5;
		if (filterLine)
		{
			centerLine = FilterLine(centerLine, scaledWidth);
		}
		for (int i = 0; i < centerLine.SegmentCount; i++)
		{
			Segment segment = centerLine.Segments[i];
			Vector vec = segment.PerpendicularUnitVector().Ratio(scaledWidth);
			Vector vec2 = vec.Negate();
			double naN = (centerLine.HasZ ? centerLine.Z[i] : double.NaN);
			double naN2 = (centerLine.HasM ? centerLine.M[i] : double.NaN);
			Point translate = segment.A.Translate(vec);
			Point translate2 = segment.A.Translate(vec2);
			double naN3 = (centerLine.HasZ ? centerLine.Z[i + 1] : double.NaN);
			double naN4 = (centerLine.HasM ? centerLine.M[i + 1] : double.NaN);
			Point translate3 = segment.B.Translate(vec);
			Point translate4 = segment.B.Translate(vec2);
			leftOffset.Add(translate);
			leftOffset.Add(translate3);
			rightOffset.Add(translate2);
			rightOffset.Add(translate4);
			if (centerLine.HasZ)
			{
				leftOffset.Z[leftOffset.PointCount - 2] = naN;
				leftOffset.Z[leftOffset.PointCount - 1] = naN3;
				rightOffset.Z[rightOffset.PointCount - 2] = naN;
				rightOffset.Z[rightOffset.PointCount - 1] = naN3;
			}
			if (centerLine.HasM)
			{
				leftOffset.M[leftOffset.PointCount - 2] = naN2;
				leftOffset.M[leftOffset.PointCount - 1] = naN4;
				rightOffset.M[rightOffset.PointCount - 2] = naN2;
				rightOffset.M[rightOffset.PointCount - 1] = naN4;
			}
		}
		leftOffset.TrimSelfIntersections();
		rightOffset.TrimSelfIntersections();
	}

	private static Polyline FilterLine(Polyline stream, double offsetDistance)
	{
		return stream.CloneFiltered(offsetDistance / 16.0);
	}

	public static void ComputeOffset(double width, Polyline centerLine, out Polyline leftOffset, out Polyline rightOffset, bool filterLine, double arcTolRatio = 0.95, bool colinearMsAtBends = false)
	{
		if (arcTolRatio > 0.9999)
		{
			throw new ArgumentOutOfRangeException("arcTolRatio", $"Error computing offset lines. The arc tolerance ratio cannot be greater than {0.9999}.");
		}
		leftOffset = new Polyline();
		rightOffset = new Polyline();
		leftOffset.TrackM();
		rightOffset.TrackM();
		if (centerLine.HasZ)
		{
			leftOffset.TrackZ();
			rightOffset.TrackZ();
		}
		double scaledWidth = width * 0.5;
		if (filterLine)
		{
			centerLine = FilterLine(centerLine, scaledWidth);
		}
		Segment4D prevSeg = default;
		Segment4D prevSeg2 = default;
		bool condition = false;
		double lengthSum = 0.0;
		for (int i = 0; i < centerLine.SegmentCount; i++)
		{
			Segment segment = centerLine.Segments[i];
			double length = segment.Length;
			if (length != 0.0)
			{
				Vector vec = segment.PerpendicularUnitVector().Ratio(scaledWidth);
				Vector vec2 = vec.Negate();
				double z = (centerLine.HasZ ? centerLine.Z[i] : double.NaN);
				double z2 = (centerLine.HasZ ? centerLine.Z[i + 1] : double.NaN);
				Segment4D segment4D = new Segment4D(new Point4D(segment.A.Translate(vec), z, lengthSum), new Point4D(segment.B.Translate(vec), z2, lengthSum + length));
				Segment4D segment4D2 = new Segment4D(new Point4D(segment.A.Translate(vec2), z, lengthSum), new Point4D(segment.B.Translate(vec2), z2, lengthSum + length));
				lengthSum += length;
				if (!condition)
				{
					AddOffsetPoint(leftOffset, segment4D.A);
					AddOffsetPoint(rightOffset, segment4D2.A);
					condition = true;
				}
				else
				{
					AddBendJoint(prevSeg, segment4D, segment.A, scaledWidth, arcTolRatio, colinearMsAtBends, leftOffset);
					AddBendJoint(prevSeg2, segment4D2, segment.A, scaledWidth, arcTolRatio, colinearMsAtBends, rightOffset);
				}
				prevSeg = segment4D;
				prevSeg2 = segment4D2;
			}
		}
		if (condition)
		{
			AddOffsetPoint(leftOffset, prevSeg.B);
			AddOffsetPoint(rightOffset, prevSeg2.B);
			leftOffset.TrimSelfIntersections(allowRingComplete: false, colinearMsAtBends);
			rightOffset.TrimSelfIntersections(allowRingComplete: false, colinearMsAtBends);
			if (colinearMsAtBends)
			{
				AddSmoothOutsideBends(leftOffset, rightOffset);
				AddSmoothOutsideBends(rightOffset, leftOffset);
			}
		}
	}

	private static void AddOffsetPoint(Polyline offset, Point4D pt)
	{
		offset.Add(pt.AsPoint2D(), pt.Z, pt.M);
	}

	private static void AddBendJoint(Segment4D prevSeg, Segment4D seg, Point vertex, double distance, double arcTolRatio, bool colinearMsAtBends, Polyline offset)
	{
		if (prevSeg.AsSegment2D().Intersects(seg.AsSegment2D(), out var x, out var y, out var t, out var t2))
		{
			Point4D point4D = prevSeg.PointAtFraction(t);
			Point4D point4D2 = seg.PointAtFraction(t2);
			Point pt = new Point(x, y);
			double z = (point4D.Z + point4D2.Z) * 0.5;
			if (colinearMsAtBends)
			{
				offset.Add(pt, z, point4D.M);
				offset.Add(pt, z, point4D2.M);
			}
			else
			{
				offset.Add(pt, z, (point4D.M + point4D2.M) * 0.5);
			}
		}
		else
		{
			AddOffsetPoint(offset, prevSeg.B);
			AddOffsetArcPoints(distance, vertex, prevSeg.B, seg.A, offset, arcTolRatio, 0.0);
		}
	}

	public static void AddOffsetArcPoints(double radius, Point center, Point4D startArc, Point4D endArc, Polyline arcPoints, double arcTolRatio, double lastRatio)
	{
		Segment segment = new Segment(center, new Segment(startArc.AsPoint2D(), endArc.AsPoint2D()).Midpoint());
		double lengthOverRadius = segment.Length / radius;
		if (lengthOverRadius != 0.0 && lastRatio != lengthOverRadius)
		{
			if (lengthOverRadius < arcTolRatio)
			{
				Point4D point4D = new Point4D(segment.PointAtFraction(1.0 / lengthOverRadius), (startArc.Z + endArc.Z) * 0.5, (startArc.M + endArc.M) * 0.5);
				AddOffsetArcPoints(radius, center, startArc, point4D, arcPoints, arcTolRatio, lengthOverRadius);
				AddOffsetArcPoints(radius, center, point4D, endArc, arcPoints, arcTolRatio, lengthOverRadius);
			}
			AddOffsetPoint(arcPoints, endArc);
		}
	}

	private static void AddSmoothOutsideBends(Polyline inside, Polyline outside)
	{
		for (int i = 1; i < inside.PointCount; i++)
		{
			if (!(inside[i] != inside[i - 1]))
			{
				int ensurePointAtM = EnsurePointAtM(outside, inside.M[i - 1]);
				int ensurePointAtM2 = EnsurePointAtM(outside, inside.M[i]);
				if (ensurePointAtM >= 0 && ensurePointAtM2 > ensurePointAtM)
				{
					SmoothMValuesRange(outside, ensurePointAtM, ensurePointAtM2);
				}
			}
		}
	}

	private static int EnsurePointAtM(Polyline line, double m)
	{
		if (!line.HasM || line.PointCount == 0)
		{
			return -1;
		}
		if (m < line.M[0] || m > line.M[line.PointCount - 1])
		{
			return -1;
		}
		for (int i = 0; i < line.PointCount; i++)
		{
			if (line.M[i] == m)
			{
				return i;
			}
			if (!(line.M[i] < m))
			{
				if (i == 0)
				{
					return -1;
				}
				double lineTerm = line.M[i] - line.M[i - 1];
				if (lineTerm <= 0.0)
				{
					return -1;
				}
				double lineTerm2 = (m - line.M[i - 1]) / lineTerm;
				line.Insert(i, new Segment(line[i - 1], line[i]).PointAtFraction(lineTerm2));
				if (line.HasZ)
				{
					line.Z[i] = line.Z[i - 1] + lineTerm2 * (line.Z[i + 1] - line.Z[i - 1]);
				}
				line.M[i] = m;
				return i;
			}
		}
		return -1;
	}

	private static void SmoothMValuesRange(Polyline line, int startIndex, int endIndex)
	{
		double lineStartIndexTerm = line.M[startIndex];
		double lineEndIndexTerm = line.M[endIndex] - lineStartIndexTerm;
		line.M[startIndex] = 0.0;
		for (int i = startIndex + 1; i <= endIndex; i++)
		{
			line.M[i] = line.M[i - 1] + line[i].DistanceTo(line[i - 1]);
		}
		double lineEndIndexTerm2 = line.M[endIndex];
		if (!(lineEndIndexTerm2 <= 0.0))
		{
			double computed = lineEndIndexTerm / lineEndIndexTerm2;
			for (int j = startIndex; j <= endIndex; j++)
			{
				line.M[j] = lineStartIndexTerm + line.M[j] * computed;
			}
		}
	}

	public static bool TryComputeOffsetParts(Polyline streamLine, double width, OffsetSides sides, bool includeEndpoints, List<Polyline> results)
	{
		if (streamLine == null || results == null)
		{
			return false;
		}
		if (streamLine.Count < 2 || width <= 0.0)
		{
			return false;
		}
		ComputeSimpleOffset(width, streamLine, out var leftOffset, out var rightOffset);
		bool sidesRightTerm = sides != OffsetSides.Right;
		bool sidesLeftTerm = sides != OffsetSides.Left;
		if (sidesRightTerm && !IsUsableOffset(leftOffset))
		{
			return false;
		}
		if (sidesLeftTerm && !IsUsableOffset(rightOffset))
		{
			return false;
		}
		if (sidesRightTerm)
		{
			results.Add(leftOffset);
		}
		if (sidesLeftTerm)
		{
			results.Add(rightOffset);
		}
		if (includeEndpoints)
		{
			Point a = (sidesRightTerm ? leftOffset.First() : streamLine.First());
			Point b = (sidesLeftTerm ? rightOffset.First() : streamLine.First());
			Point a2 = (sidesRightTerm ? leftOffset.Last() : streamLine.Last());
			Point b2 = (sidesLeftTerm ? rightOffset.Last() : streamLine.Last());
			results.Add(NewCap(a, b));
			results.Add(NewCap(a2, b2));
		}
		return true;
	}

	private static bool IsUsableOffset(Polyline offset)
	{
		if (offset != null)
		{
			return offset.Count >= 2;
		}
		return false;
	}

	private static Polyline NewCap(Point a, Point b)
	{
		return new Polyline(2) { a, b };
	}

	public static void ComputeOffsetWithCrossSections(double width, Polyline streamLine, out Polyline leftOffset, out Polyline rightOffset, out List<Polyline> crossSections)
	{
		leftOffset = new Polyline();
		rightOffset = new Polyline();
		crossSections = new List<Polyline>();
		double scaledWidth = width * 0.5;
		double tolerance = scaledWidth / 16.0;
		Polyline polyline = streamLine.Clone();
		polyline.Filter(tolerance);
		for (int i = 0; i < polyline.SegmentCount; i++)
		{
			Segment segment = polyline.Segments[i];
			Vector vec = segment.PerpendicularUnitVector().Ratio(scaledWidth);
			Vector vec2 = vec.Negate();
			Point point = segment.A.Translate(vec);
			Point point2 = segment.A.Translate(vec2);
			Point point3 = segment.B.Translate(vec);
			Point point4 = segment.B.Translate(vec2);
			leftOffset.Add(point);
			leftOffset.Add(point3);
			rightOffset.Add(point2);
			rightOffset.Add(point4);
			Polyline polyline2 = new Polyline(3);
			polyline2.Add(new Segment(point, point3).Midpoint());
			polyline2.Add(segment.Midpoint());
			polyline2.Add(new Segment(point2, point4).Midpoint());
			crossSections.Add(polyline2);
		}
		leftOffset.TrimSelfIntersections();
		rightOffset.TrimSelfIntersections();
	}

	public static void ComputeDirectionalOffset(double distanceLeft, double distanceRight, Polyline streamLine, out Polyline leftOffset, out Polyline rightOffset, bool tieShoelaces = false)
	{
		leftOffset = new Polyline();
		rightOffset = new Polyline();
		double tolerance = (distanceLeft + distanceRight) / 2.0 / 16.0;
		streamLine = streamLine.Clone();
		streamLine.Filter(tolerance);
		if (tieShoelaces)
		{
			leftOffset.Add(streamLine.First());
			rightOffset.Add(streamLine.First());
		}
		for (int i = 0; i < streamLine.SegmentCount; i++)
		{
			Segment segment = streamLine.Segments[i];
			Vector vec = segment.PerpendicularUnitVector().Ratio(distanceLeft);
			Vector vec2 = segment.PerpendicularUnitVector().Ratio(-1.0 * distanceRight);
			Point translate = segment.A.Translate(vec);
			Point translate2 = segment.A.Translate(vec2);
			Point translate3 = segment.B.Translate(vec);
			Point translate4 = segment.B.Translate(vec2);
			leftOffset.Add(translate);
			leftOffset.Add(translate3);
			rightOffset.Add(translate2);
			rightOffset.Add(translate4);
		}
		if (tieShoelaces)
		{
			leftOffset.Points.Last();
			rightOffset.Points.Last();
			_ = leftOffset.Points[1];
			_ = rightOffset[1];
			leftOffset.Add(streamLine.Last());
			rightOffset.Add(streamLine.Last());
			leftOffset.TrimSelfIntersections();
			rightOffset.TrimSelfIntersections();
			leftOffset.RemoveAt(0);
			leftOffset.RemoveAt(leftOffset.Count - 1);
			rightOffset.RemoveAt(0);
			rightOffset.RemoveAt(leftOffset.Count - 1);
		}
		else
		{
			leftOffset.TrimSelfIntersections();
			rightOffset.TrimSelfIntersections();
		}
	}

	public static void ComputeOffset(Polyline streamPart, PointOnPolyline usStreamPt, PointOnPolyline dsStreamPt, Polyline usxsPolyline, PointOnPolyline usxsPt, Polyline dsxsPolyline, PointOnPolyline dsxsPt, Polyline offset)
	{
		new Polyline();
		new List<LineIntersection>();
		Polyline usxs = usxsPolyline.SubsectionDirected(usStreamPt, usxsPt);
		Polyline dsxs = dsxsPolyline.SubsectionDirected(dsStreamPt, dsxsPt);
		ComputeOffset(streamPart, usxs, dsxs);
	}

	private static Polyline ComputeOffset(Polyline stream, Polyline usxs, Polyline dsxs)
	{
		Polyline polyline = new Polyline();
		Polyline polyline2 = new Polyline();
		List<LineIntersection> lineIntersections = new List<LineIntersection>();
		stream = stream.Clone();
		usxs = usxs.Clone();
		dsxs = dsxs.Clone();
		if (usxs.Count == 0)
		{
			usxs.Add(stream.First());
		}
		if (dsxs.Count == 0)
		{
			dsxs.Add(stream.Last());
		}
		double length = stream.Length;
		double length2 = usxs.Length;
		double length3 = dsxs.Length;
		double length2Length3Term = 0.5 * (length2 + length3);
		if (length2 < 0.1 && length3 < 0.1)
		{
			return stream;
		}
		int count = 4;
		double countTerm = length2Length3Term / (double)count;
		int lengthTerm = (int)(length / countTerm);
		while (lengthTerm < 8 && count != 16)
		{
			count++;
			countTerm = length2Length3Term / (double)count;
			lengthTerm = (int)(length / countTerm);
		}
		double computed = length2Length3Term / 64.0;
		if (computed < 0.001)
		{
			computed = 0.001;
		}
		stream.Filter(computed);
		usxs.Filter(computed);
		dsxs.Filter(computed);
		for (int i = 1; i <= count; i++)
		{
			double fractionAlongLine = 1.0 / (double)(count - i + 1);
			PointOnPolyline pop = usxs.PointAtFraction(fractionAlongLine);
			PointOnPolyline pop2 = dsxs.PointAtFraction(fractionAlongLine);
			double distanceTo = pop.Point.DistanceTo(stream.First());
			double distanceTo2 = pop2.Point.DistanceTo(stream.Last());
			double targetSegmentLength = (distanceTo + distanceTo2) * 0.5;
			polyline2 = stream.DiscretizeLine(targetSegmentLength);
			double distanceTo3 = distanceTo;
			double distanceTo4 = distanceTo2;
			if (polyline2.SegmentCount > 0)
			{
				Segment segment = polyline2.Segments.First();
				Segment segment2 = polyline2.Segments.Last();
				distanceTo3 = segment.PerpendicularSignedDistance(pop.Point);
				distanceTo4 = segment2.PerpendicularSignedDistance(pop2.Point);
			}
			double deltaDistance = distanceTo4 - distanceTo3;
			polyline.Clear();
			polyline.Add(pop.Point);
			for (int j = 2; j < polyline2.Count - 2; j++)
			{
				Segment segment3 = new Segment(polyline2[j - 1], polyline2[j + 1]);
				double factor = SmoothOffset((double)j / (double)polyline2.Count, distanceTo3, deltaDistance);
				Vector vec = segment3.PerpendicularUnitVector().Ratio(factor);
				Point point = segment3.Midpoint().Translate(vec);
				polyline.AddWithinTolerance(point, 1E-06);
			}
			polyline.AddWithinTolerance(pop2.Point, 1E-06);
			usxs.TrimBefore(pop);
			dsxs.TrimBefore(pop2);
			polyline.TrimSelfIntersections();
			lineIntersections = polyline.GetAllIntersections(usxs);
			if (lineIntersections.Count > 1)
			{
				lineIntersections.Sort(LineIntersection.CompareB);
				polyline.TrimBefore(lineIntersections.Last().AIntersection);
				usxs.TrimBefore(lineIntersections.Last().BIntersection);
			}
			lineIntersections = polyline.GetAllIntersections(dsxs);
			if (lineIntersections.Count > 1)
			{
				lineIntersections.Sort(LineIntersection.CompareB);
				polyline.TrimAfter(lineIntersections.Last().AIntersection);
				dsxs.TrimBefore(lineIntersections.Last().BIntersection);
			}
			if (i == count)
			{
				polyline.Filter(computed);
			}
			else
			{
				polyline.CopyTo(stream);
			}
		}
		return polyline;
	}

	private static double SmoothOffset(double fractionAlong, double startingDistance, double DeltaDistance)
	{
		double fractionAlongPolynomial = -2.0 * fractionAlong * fractionAlong * fractionAlong + 3.0 * fractionAlong * fractionAlong;
		return startingDistance + DeltaDistance * fractionAlongPolynomial;
	}
}
