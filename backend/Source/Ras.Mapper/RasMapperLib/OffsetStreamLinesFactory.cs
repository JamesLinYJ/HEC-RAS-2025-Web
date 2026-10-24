using System;
using System.Collections.Generic;
using System.Linq;

namespace RasMapperLib;

public class OffsetStreamLinesFactory
{
	public bool DebugMode;

	private double _minimumOffset;

	private PointMs _stream;

	private PointMs _usxs;

	private PointMs _dsxs;

	private PointMs _resampledStream;

	private List<PointIntersect> _intersections;

	public OffsetStreamLinesFactory(double minimumOffset = 0.1)
	{
		DebugMode = false;
		_minimumOffset = 0.1;
		_stream = new PointMs();
		_usxs = new PointMs();
		_dsxs = new PointMs();
		_resampledStream = new PointMs();
		_intersections = new List<PointIntersect>();
		_minimumOffset = minimumOffset;
	}

	public static void ComputeSimpleOffset(double width, PointMs stream, PointMs leftOffset, PointMs rightOffset, bool useFiltering = true, double ArcTolRatio = 0.95)
	{
		double scaledWidth = width * 0.5;
		double tolerance = scaledWidth / 16.0;
		double unity = 0.9999;
		if (ArcTolRatio > unity)
		{
			throw new Exception("Error computing offset lines. The arc tolerance ratio cannot be greater than " + unity);
		}
		if (useFiltering)
		{
			stream.FilterXY(tolerance);
		}
		double lengthSum = 0.0;
		checked
		{
			int count = stream.SegmentCount() - 1;
			SegmentM segmentM2 = default;
			SegmentM segmentM3 = default;
			SegmentM segmentM4 = default;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			PointM pointM = default;
			SegmentM segmentM5 = default;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = stream.Segment(i);
				PointM delta = segmentM.PerpendicularUnitVector().Ratio(scaledWidth);
				PointM delta2 = delta.NegateXY();
				segmentM2.A = segmentM.A.Translate(delta);
				segmentM3.A = segmentM.A.Translate(delta2);
				segmentM2.B = segmentM.B.Translate(delta);
				segmentM3.B = segmentM.B.Translate(delta2);
				segmentM2.A.M = lengthSum;
				segmentM3.A.M = lengthSum;
				double length = segmentM.Length;
				lengthSum += length;
				segmentM2.B.M = lengthSum;
				segmentM3.B.M = lengthSum;
				if (i == 0)
				{
					leftOffset.Add(segmentM2.A);
					rightOffset.Add(segmentM3.A);
				}
				else if (length != 0.0)
				{
					if (segmentM4.Intersects(segmentM2, ref x, ref y, ref t, ref t2))
					{
						pointM.X = x;
						pointM.Y = y;
						pointM.Z = (segmentM4.PointAtFraction(t).Z + segmentM2.PointAtFraction(t2).Z) * 0.5;
						pointM.M = (segmentM4.PointAtFraction(t).M + segmentM2.PointAtFraction(t2).M) * 0.5;
						leftOffset.Add(pointM);
					}
					else
					{
						leftOffset.Add(segmentM4.B);
						ComputeSimpleOffsetArc(scaledWidth, segmentM.A, segmentM4.B, segmentM2.A, leftOffset, ArcTolRatio);
					}
					if (segmentM5.Intersects(segmentM3, ref x, ref y, ref t, ref t2))
					{
						pointM.X = x;
						pointM.Y = y;
						pointM.Z = (segmentM5.PointAtFraction(t).Z + segmentM3.PointAtFraction(t2).Z) * 0.5;
						pointM.M = (segmentM5.PointAtFraction(t).M + segmentM3.PointAtFraction(t2).M) * 0.5;
						rightOffset.Add(pointM);
					}
					else
					{
						rightOffset.Add(segmentM5.B);
						ComputeSimpleOffsetArc(scaledWidth, segmentM.A, segmentM5.B, segmentM3.A, rightOffset, ArcTolRatio);
					}
				}
				segmentM4 = segmentM2;
				segmentM5 = segmentM3;
			}
			leftOffset.Add(segmentM2.B);
			rightOffset.Add(segmentM3.B);
			leftOffset.TrimSelfIntersections();
			rightOffset.TrimSelfIntersections();
		}
	}

	public static void ComputeSimpleOffsetArc(double r, PointM center, PointM startArc, PointM endArc, PointMs arcpoints, double ArcTolRatio)
	{
		SegmentM segmentM = default;
		segmentM.A = startArc;
		segmentM.B = endArc;
		PointM b = segmentM.MidPoint();
		SegmentM segmentM2 = default;
		segmentM2.A = center;
		segmentM2.B = b;
		double segmentM2Term = segmentM2.Length / r;
		if (segmentM2Term < ArcTolRatio)
		{
			PointM pointM = segmentM2.PointAtFraction(1.0 / segmentM2Term);
			ComputeSimpleOffsetArc(r, center, startArc, pointM, arcpoints, ArcTolRatio);
			ComputeSimpleOffsetArc(r, center, pointM, endArc, arcpoints, ArcTolRatio);
		}
		arcpoints.Add(endArc);
	}

	public static void ComputeDirectionalOffset(double distanceLeft, double distanceRight, PointMs stream, PointMs leftOffset, PointMs rightOffset, bool tieShoelaces = false)
	{
		double tolerance = (distanceLeft + distanceRight) / 2.0 / 16.0;
		stream.FilterXY(tolerance);
		if (tieShoelaces)
		{
			leftOffset.Add(stream.FirstPointM());
			rightOffset.Add(stream.FirstPointM());
		}
		checked
		{
			int count = stream.SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = stream.Segment(i);
				PointM delta = segmentM.PerpendicularUnitVector().Ratio(distanceLeft);
				PointM delta2 = segmentM.PerpendicularUnitVector().Ratio(-1.0 * distanceRight);
				PointM translate = segmentM.A.Translate(delta);
				PointM translate2 = segmentM.A.Translate(delta2);
				PointM translate3 = segmentM.B.Translate(delta);
				PointM translate4 = segmentM.B.Translate(delta2);
				leftOffset.Add(translate);
				leftOffset.Add(translate3);
				rightOffset.Add(translate2);
				rightOffset.Add(translate4);
			}
			if (tieShoelaces)
			{
				leftOffset.LastPointM();
				rightOffset.LastPointM();
				_ = leftOffset[1];
				_ = rightOffset[1];
				leftOffset.Add(stream.LastPointM());
				rightOffset.Add(stream.LastPointM());
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
	}

	public void ComputeOffset(PointMs streamPart, PointOnPolyline usStreamPt, PointOnPolyline dsStreamPt, Polyline usxsPolyline, PointOnPolyline usxsPt, Polyline dsxsPolyline, PointOnPolyline dsxsPt, PointMs offset)
	{
		if (usStreamPt.LessThan(usxsPt))
		{
			usxsPolyline.PointSubsection(usStreamPt, usxsPt, _usxs);
		}
		else
		{
			usxsPolyline.PointSubsection(usxsPt, usStreamPt, _usxs);
			_usxs.Reverse();
		}
		if (dsStreamPt.LessThan(dsxsPt))
		{
			dsxsPolyline.PointSubsection(dsStreamPt, dsxsPt, _dsxs);
		}
		else
		{
			dsxsPolyline.PointSubsection(dsxsPt, dsStreamPt, _dsxs);
			_dsxs.Reverse();
		}
		ComputeOffset(streamPart, _usxs, _dsxs, offset);
	}

	public void ComputeOffset(PointMs stream, PointMs usxs, PointMs dsxs, PointMs offset)
	{
		offset.Clear();
		stream.CopyTo(_stream);
		if (usxs != _usxs)
		{
			usxs.CopyTo(_usxs);
		}
		if (dsxs != _dsxs)
		{
			dsxs.CopyTo(_dsxs);
		}
		if (_usxs.Count == 0)
		{
			_usxs.Add(_stream.First());
		}
		if (_dsxs.Count == 0)
		{
			_dsxs.Add(_stream.Last());
		}
		double length = _stream.ComputeLength();
		double length2 = _usxs.ComputeLength();
		double length3 = _dsxs.ComputeLength();
		double length2Length3Term = 0.5 * (length2 + length3);
		if (length2 < _minimumOffset && length3 < _minimumOffset)
		{
			_stream.CopyTo(offset);
			return;
		}
		int ordinal = 4;
		double computed = length2Length3Term / (double)ordinal;
		checked
		{
			int lengthTerm = (int)Math.Round(length / computed);
			while (!((lengthTerm >= 8) | (ordinal == 16)))
			{
				ordinal++;
				computed = length2Length3Term / (double)ordinal;
				lengthTerm = (int)Math.Round(length / computed);
			}
			double computed2 = length2Length3Term / 64.0;
			if (computed2 < 0.001)
			{
				computed2 = 0.001;
			}
			double colinearTolerance = 1E-06;
			_stream.FilterXY(computed2);
			_usxs.FilterXY(computed2);
			_dsxs.FilterXY(computed2);
			int count = ordinal;
			for (int i = 1; i <= count; i++)
			{
				double fraction = 1.0 / (double)(ordinal - i + 1);
				PointOnPolyline pointOnPolyline = _usxs.PointAtFraction(fraction);
				PointOnPolyline pointOnPolyline2 = _dsxs.PointAtFraction(fraction);
				double distanceTo = pointOnPolyline.PointM.DistanceTo(_stream.First());
				double distanceTo2 = pointOnPolyline2.PointM.DistanceTo(_stream.Last());
				double approximateSpacing = (distanceTo + distanceTo2) * 0.5;
				_stream.Resample(approximateSpacing, _resampledStream);
				double distanceTo3 = distanceTo;
				double distanceTo4 = distanceTo2;
				if (_resampledStream.SegmentCount() > 0)
				{
					SegmentM segmentM = _resampledStream.SegmentFirst();
					SegmentM segmentM2 = _resampledStream.SegmentLast();
					distanceTo3 = segmentM.PerpendicularSignedDistance(pointOnPolyline.PointM);
					distanceTo4 = segmentM2.PerpendicularSignedDistance(pointOnPolyline2.PointM);
				}
				double deltaDistance = distanceTo4 - distanceTo3;
				offset.Clear();
				offset.Add(pointOnPolyline.PointM);
				int count2 = _resampledStream.Count - 3;
				for (int j = 2; j <= count2; j++)
				{
					SegmentM segmentM3 = new SegmentM(_resampledStream[j - 1], _resampledStream[j + 1]);
					double fractionAlong = (double)j / (double)_resampledStream.Count;
					double factor = SmoothOffset(fractionAlong, distanceTo3, deltaDistance);
					PointM delta = segmentM3.PerpendicularUnitVector().Ratio(factor);
					PointM point = segmentM3.MidPoint().Translate(delta);
					offset.Add(point, colinearTolerance);
				}
				offset.Add(pointOnPolyline2.PointM, colinearTolerance);
				if (DebugMode)
				{
					offset.ToImmediateWindow("Lateral Step " + i);
				}
				_usxs.TrimBeginning(pointOnPolyline);
				_dsxs.TrimBeginning(pointOnPolyline2);
				offset.TrimSelfIntersections();
				offset.Intersections(_usxs, _intersections);
				if (_intersections.Count > 1)
				{
					_intersections.Sort(PointIntersect.CompareB);
					offset.TrimBeginning(_intersections.Last().APointOnPolyline);
					_usxs.TrimBeginning(_intersections.Last().BPointOnPolyline);
				}
				offset.Intersections(_dsxs, _intersections);
				if (_intersections.Count > 1)
				{
					_intersections.Sort(PointIntersect.CompareB);
					offset.TrimEnd(_intersections.Last().APointOnPolyline);
					_dsxs.TrimBeginning(_intersections.Last().BPointOnPolyline);
				}
				if (i == ordinal)
				{
					offset.FilterXY(computed2);
				}
				else
				{
					offset.CopyTo(_stream);
				}
			}
		}
	}

	private double SmoothOffset(double fractionAlong, double startingDistance, double DeltaDistance)
	{
		double fractionAlongTerm = -2.0 * Math.Pow(fractionAlong, 3.0) + 3.0 * Math.Pow(fractionAlong, 2.0);
		return startingDistance + DeltaDistance * fractionAlongTerm;
	}
}
