using System;
using System.Collections.Generic;
using System.Threading;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public static class SnapHelpers
{
	private static ThreadLocal<List<FeatureSegmentSubset>> _segScratchSpace = new ThreadLocal<List<FeatureSegmentSubset>>(() => new List<FeatureSegmentSubset>());

	private static ThreadLocal<List<PointOnPolyline>> _ptScratchSpace = new ThreadLocal<List<PointOnPolyline>>(() => new List<PointOnPolyline>());

	public static bool TrySnapToInternalVertice(PointOnPolyline pop, double cellSize, Polyline pl, out SplitLineResult splitRes)
	{
		Point point = pop.Point;
		int segmentIndex = pop.SegmentIndex;
		Point pt = pl[segmentIndex];
		int segmentIndexOffset = segmentIndex + 1;
		Point pt2 = pl[segmentIndexOffset];
		CompactList<int> snappedVertices = default;
		if (EditableVectorHelpers.IsHitTestWithinDuplicatePointTolerance(pt, point, cellSize))
		{
			snappedVertices.Add(segmentIndex);
		}
		if (EditableVectorHelpers.IsHitTestWithinDuplicatePointTolerance(pt2, point, cellSize))
		{
			snappedVertices.Add(segmentIndexOffset);
		}
		splitRes = new SplitLineResult
		{
			Point = pop,
			SnappedVertices = snappedVertices
		};
		return snappedVertices.Count > 0;
	}

	public static SnapResult TrySnapIntersection(HitTestArgs location, IList<FeatureSegmentSubset> candidates, Func<int, Polyline> getLine)
	{
		SnapMode snapMode = TrySnapIntersection(location, candidates, getLine, out var snappedIntersection);
		if (snapMode == SnapMode.NoSnap || snapMode == SnapMode.FailedLineIntersection)
		{
			return SnapResult.NoSnap;
		}
		LineIntersection intersection = snappedIntersection.Intersection;
		Polyline pl = getLine(snappedIntersection.APolylineIndex);
		Polyline pl2 = getLine(snappedIntersection.BPolylineIndex);
		double cellSize = location.Window.CellSize;
		TrySnapToInternalVertice(intersection.AIntersection, cellSize, pl, out var splitRes);
		TrySnapToInternalVertice(intersection.BIntersection, cellSize, pl2, out var splitRes2);
		SnapResult result = new SnapResult();
		result.Result = snapMode;
		result.SnapPoint = snappedIntersection.Intersection.Point;
		CompactList<SplitLineResult> splitLines = default;
		SplitLineResult splitRes3 = splitRes;
		splitRes3.FID = snappedIntersection.APolylineIndex;
		splitLines.Add(splitRes3);
		splitRes3 = splitRes2;
		splitRes3.FID = snappedIntersection.BPolylineIndex;
		splitLines.Add(splitRes3);
		result.SplitLines = splitLines;
		return result;
	}

	public static SnapMode TrySnapIntersection(HitTestArgs location, IList<FeatureSegmentSubset> candidates, Func<int, Polyline> getLine, out LineCollectionIntersection snappedIntersection)
	{
		snappedIntersection = default;
		if (candidates.Count == 0)
		{
			return SnapMode.NoSnap;
		}
		if (candidates.Count == 1 && candidates[0].Ranges.Count == 1 && candidates[0].Ranges[0].Count < 3)
		{
			return SnapMode.NoSnap;
		}
		_ = location.QueryPoint;
		snappedIntersection = LineCollectionIntersection.NullIntersection;
		IList<LineCollectionIntersection> allIntersections = PolylineCollection.GetAllIntersections(candidates, getLine, (Func<int, Polyline> getLineFunc, int idx) => getLineFunc(idx), includeEndptIntersections: false);
		if (allIntersections.Count == 0)
		{
			return SnapMode.NoSnap;
		}
		if (allIntersections.Count > 1)
		{
			return SnapMode.FailedLineIntersection;
		}
		snappedIntersection = allIntersections[0];
		return SnapMode.LineIntersection;
	}

	public static SnapMode TrySnapToVertexOnly(HitTestArgs location, Polyline polyline, out SplitLineResult splitRes)
	{
		if (polyline.Count < 3)
		{
			splitRes = SplitLineResult.NullResult;
			return SnapMode.NoSnap;
		}
		int nearestPoint = polyline.NearestPoint(location.QueryPoint);
		if (nearestPoint <= 0 || nearestPoint >= polyline.Count - 1)
		{
			splitRes = SplitLineResult.NullResult;
			return SnapMode.NoSnap;
		}
		double scaledCellSize = location.Window.CellSize * 100.0;
		if (polyline[nearestPoint].DistanceTo(location.QueryPoint) > scaledCellSize)
		{
			splitRes = SplitLineResult.NullResult;
			return SnapMode.NoSnap;
		}
		splitRes = new SplitLineResult
		{
			Point = new PointOnPolyline(polyline[nearestPoint], nearestPoint - 1, 1.0),
			SnappedVertices = new CompactList<int>(nearestPoint)
		};
		return SnapMode.ToLinePoint;
	}

	public static SnapMode TrySnapToVertexOnly(HitTestArgs location, IList<FeatureSegmentSubset> candidates, Func<int, Polyline> getLine, out SplitLineResult splitRes)
	{
		if (candidates.Count == 0)
		{
			splitRes = SplitLineResult.NullResult;
			return SnapMode.NoSnap;
		}
		SplitLineResult splitLineResult = SplitLineResult.NullResult;
		double maxValue = double.MaxValue;
		Point queryPoint = location.QueryPoint;
		for (int i = 0; i < candidates.Count; i++)
		{
			int fid = candidates[i].Fid;
			Polyline polyline = getLine(fid);
			if (TrySnapToVertexOnly(location, polyline, out var splitRes2) != SnapMode.NoSnap)
			{
				double distanceSquaredTo = splitRes2.Point.Point.DistanceSquaredTo(queryPoint);
				if (distanceSquaredTo < maxValue)
				{
					maxValue = distanceSquaredTo;
					SplitLineResult splitLineResult2 = splitRes2;
					splitLineResult2.FID = fid;
					splitLineResult = splitLineResult2;
				}
			}
		}
		splitRes = splitLineResult;
		if (!splitLineResult.Point.IsNull())
		{
			return SnapMode.ToLinePoint;
		}
		return SnapMode.NoSnap;
	}

	public static SnapMode TrySnapAndSlideToNearestPoint(HitTestArgs location, Polyline arcPolyline, IList<Range1D> candidateSegments, out SplitLineResult splitRes)
	{
		Point queryPoint = location.QueryPoint;
		PointOnPolyline pointOnPolyline = arcPolyline.NearestPointOnPolyline(queryPoint, candidateSegments, out var _);
		if (pointOnPolyline.IsNull())
		{
			splitRes = SplitLineResult.NullResult;
			return SnapMode.NoSnap;
		}
		if (TrySnapToInternalVertice(pointOnPolyline, location.Window.CellSize, arcPolyline, out var splitRes2))
		{
			splitRes = splitRes2;
			return SnapMode.ToLinePoint;
		}
		splitRes = new SplitLineResult
		{
			Point = pointOnPolyline
		};
		return SnapMode.ToLine;
	}

	public static SnapMode TrySnapAndSlideToNearestPoint(HitTestArgs location, IList<FeatureSegmentSubset> candidates, Func<int, Polyline> getLine, out SplitLineResult splitRes)
	{
		Point queryPoint = location.QueryPoint;
		if (candidates.Count == 0)
		{
			splitRes = SplitLineResult.NullResult;
			return SnapMode.NoSnap;
		}
		SnapMode result = SnapMode.NoSnap;
		SplitLineResult splitLineResult = SplitLineResult.NullResult;
		double maxValue = double.MaxValue;
		for (int i = 0; i < candidates.Count; i++)
		{
			FeatureSegmentSubset featureSegmentSubset = candidates[i];
			int fid = featureSegmentSubset.Fid;
			Polyline arcPolyline = getLine(fid);
			SnapMode snapMode = TrySnapAndSlideToNearestPoint(location, arcPolyline, featureSegmentSubset.Ranges, out var splitRes2);
			if (snapMode != SnapMode.NoSnap)
			{
				double distanceSquaredTo = splitRes2.Point.Point.DistanceSquaredTo(queryPoint);
				if (distanceSquaredTo < maxValue)
				{
					maxValue = distanceSquaredTo;
					result = snapMode;
					SplitLineResult splitLineResult2 = splitRes2;
					splitLineResult2.FID = fid;
					splitLineResult = splitLineResult2;
				}
			}
		}
		splitRes = splitLineResult;
		return result;
	}

	public static SnapResult TrySnap(HitTestArgs location, IList<FeatureSegmentSubset> candidates, Func<int, Polyline> getLine)
	{
		_ = location.QueryPoint;
		if (candidates.Count == 0)
		{
			return SnapResult.NoSnap;
		}
		FeatureSegmentSubset featureSegmentSubset = candidates[0];
		if (candidates.Count == 1 && featureSegmentSubset.Ranges.Count == 1 && featureSegmentSubset.Ranges[0].Count < 3)
		{
			int fid = candidates[0].Fid;
			SnapMode result = TrySnapAndSlideToNearestPoint(location, getLine(fid), featureSegmentSubset.Ranges, out var splitRes);
			SplitLineResult splitLineResult = splitRes;
			splitLineResult.FID = fid;
			return new SnapResult(result, splitLineResult);
		}
		SnapResult result2 = TrySnapIntersection(location, candidates, getLine);
		if (result2.Success)
		{
			return result2;
		}
		SnapMode snapMode = TrySnapAndSlideToNearestPoint(location, candidates, getLine, out var splitRes2);
		if (snapMode != SnapMode.NoSnap)
		{
			return new SnapResult(snapMode, splitRes2);
		}
		return SnapResult.NoSnap;
	}
}
