using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public abstract class GenericEditableLine<T> : GenericEditablePointCollection<T>, IEditableLine<T>, IEditablePointCollection where T : IPointCollection, IReadOnlyList<Point>, ISpatiallyIndexedPointCollection
{
	public GenericEditableLine(T line, IVectorManager vectorManager, VectorType vectorType, Func<EditOperation, bool, bool> executeEditOperation, SelectedSet selectionCache, OverlayChannels overlayChannels)
		: base(line, vectorManager, vectorType, executeEditOperation, selectionCache, overlayChannels)
	{
	}

	public virtual bool TryPreviewAttachEndPoint(HitTestArgs hitTest)
	{
		Point queryPoint = hitTest.QueryPoint;
		if (Polyline.NearestEndpoint(Vector, queryPoint).Point.DistanceTo(queryPoint) > hitTest.Window.CellSize * 64.0)
		{
			return false;
		}
		PreviewAttachEndPoint(hitTest);
		return true;
	}

	public virtual void PreviewAttachEndPoint(HitTestArgs hitTest)
	{
		_overlay.TryGetVector(_previewPointChannel, out var id);
		_overlay.TryGetVector(_previewSegmentChannel, out var id2);
		_overlay.TryGetVector(_previewSegmentBackgroundChannel, out var id3);
		EditFunctions.RenderPointAttachOperation(VectorManager, ref id, ref id2, ref id3, Polyline.NearestEndpoint(Vector, hitTest.QueryPoint).Point, hitTest.QueryPoint);
		_overlay.RegisterVector(_previewPointChannel, id);
		_overlay.RegisterVector(_previewSegmentChannel, id2);
		_overlay.RegisterVector(_previewSegmentBackgroundChannel, id3);
	}

	public virtual bool TryAttachEndPoint(HitTestArgs hitTest, out int insertedIndex)
	{
		Point queryPoint = hitTest.QueryPoint;
		PointOnPolyline pointOnPolyline = Polyline.NearestEndpoint(Vector, queryPoint);
		if (pointOnPolyline.Point.DistanceTo(queryPoint) > hitTest.Window.CellSize * 64.0)
		{
			insertedIndex = -1;
			return false;
		}
		AttachEndPoint(hitTest);
		insertedIndex = ((pointOnPolyline.SegmentIndex != 0) ? (Vector.Count - 1) : 0);
		return true;
	}

	public virtual void AttachEndPoint(HitTestArgs hitTest)
	{
		PointOnPolyline pointOnPolyline = Polyline.NearestEndpoint(Vector, hitTest.QueryPoint);
		ref Point point = ref pointOnPolyline.Point;
		PointOnPolyline pointOnPolyline2 = Polyline.FirstPointOnPolyline(Vector);
		if (point.EqualXY(in pointOnPolyline2.Point))
		{
			PrependPoint(hitTest);
		}
		else
		{
			AppendPoint(hitTest);
		}
	}

	public virtual bool TryPreviewAppendPoint()
	{
		throw new NotImplementedException();
	}

	public virtual void PreviewAppendPoint(Point point, bool removeUnusedPreviewVectors = true, bool isOpenEnded = true)
	{
		if (isOpenEnded || Vector.Count <= 1)
		{
			_overlay.TryGetVector(_previewPointChannel, out var id);
			_overlay.TryGetVector(_previewSegmentChannel, out var id2);
			_overlay.TryGetVector(_previewSegmentBackgroundChannel, out var id3);
			EditFunctions.RenderPointAttachOperation(VectorManager, ref id, ref id2, ref id3, Polyline.LastPointOnPolyline(Vector).Point, point);
			_overlay.RegisterVector(_previewPointChannel, id);
			_overlay.RegisterVector(_previewSegmentChannel, id2);
			_overlay.RegisterVector(_previewSegmentBackgroundChannel, id3);
		}
		else
		{
			PreviewInsertPoint(point, Vector.Count - 1, isOpenEnded);
		}
	}

	public virtual bool TryAppendPoints(IReadOnlyList<Point> points)
	{
		if (points == null || points.Count == 0)
		{
			return false;
		}
		HitTestArgs args = HitTestArgs.Generate(points[points.Count - 1], 1.0);
		ExecuteEditOperation?.Invoke(new AppendPointsOperation(() => Vector, points, args, SelectedPoints), arg2: false);
		return true;
	}

	public virtual void PreviewSmartPath(Point lastPt, IReadOnlyList<Point> pathPoints)
	{
		if (pathPoints == null || pathPoints.Count == 0)
		{
			ClearSmartPathPreview();
			return;
		}
		Segment[] segments = new Segment[pathPoints.Count];
		Point ptA = lastPt;
		for (int i = 0; i < pathPoints.Count; i++)
		{
			segments[i] = new Segment(ptA, pathPoints[i]);
			ptA = pathPoints[i];
		}
		Point point = pathPoints[pathPoints.Count - 1];
		_overlay.TryGetVector(_previewPointChannel, out var id);
		_overlay.TryGetVector(_previewSegmentChannel, out var id2);
		_overlay.TryGetVector(_previewSegmentBackgroundChannel, out var id3);
		VectorManager.AddOrUpdateVector(ref id3, segments, DefaultEditSymbology.AppendSegmentBackgroundSymbology);
		VectorManager.AddOrUpdateVector(ref id2, segments, DefaultEditSymbology.AppendSegmentSymbology);
		VectorManager.AddOrUpdateVector(ref id, point, DefaultEditSymbology.AppendPointSymbology);
		VectorManager.SetVectorZIndex(id2, 2);
		VectorManager.SetVectorZIndex(id, 3);
		_overlay.RegisterVector(_previewPointChannel, id);
		_overlay.RegisterVector(_previewSegmentChannel, id2);
		_overlay.RegisterVector(_previewSegmentBackgroundChannel, id3);
	}

	public virtual void ClearSmartPathPreview()
	{
		_overlay.TryGetVector(_previewPointChannel, out var id);
		_overlay.TryGetVector(_previewSegmentChannel, out var id2);
		_overlay.TryGetVector(_previewSegmentBackgroundChannel, out var id3);
		Segment[] segments = Array.Empty<Segment>();
		VectorManager.AddOrUpdateVector(ref id3, segments, DefaultEditSymbology.AppendSegmentBackgroundSymbology);
		VectorManager.AddOrUpdateVector(ref id2, segments, DefaultEditSymbology.AppendSegmentSymbology);
		VectorManager.AddOrUpdateVector(ref id, Point.NullPoint, DefaultEditSymbology.AppendPointSymbology);
		_overlay.RegisterVector(_previewPointChannel, id);
		_overlay.RegisterVector(_previewSegmentChannel, id2);
		_overlay.RegisterVector(_previewSegmentBackgroundChannel, id3);
	}

	public virtual bool TryPreviewPrependPoint()
	{
		throw new NotImplementedException();
	}

	public virtual void PreviewPrependPoint(HitTestArgs hitTest)
	{
		_overlay.TryGetVector(_previewPointChannel, out var id);
		_overlay.TryGetVector(_previewSegmentChannel, out var id2);
		_overlay.TryGetVector(_previewSegmentBackgroundChannel, out var id3);
		EditFunctions.RenderPointAttachOperation(VectorManager, ref id, ref id2, ref id3, Polyline.FirstPointOnPolyline(Vector).Point, hitTest.QueryPoint);
		_overlay.RegisterVector(_previewPointChannel, id);
		_overlay.RegisterVector(_previewSegmentChannel, id2);
		_overlay.RegisterVector(_previewSegmentBackgroundChannel, id3);
	}

	public virtual bool TryPrependPoint()
	{
		throw new NotImplementedException();
	}

	public bool AreInsertAnglesValid(HitTestArgs hitTest, int segmentIndex)
	{
		int index = (segmentIndex + 1) % Vector.Count;
		Vector vec = new Vector(Vector[segmentIndex], Vector[index]);
		Vector vec2 = new Vector(Vector[index], hitTest.QueryPoint);
		Vector vector = new Vector(hitTest.QueryPoint, Vector[segmentIndex]);
		double angleBetween = vec.AngleBetween(in vec2);
		double angleBetween2 = vector.AngleBetween(in vec);
		if (angleBetween < 0.0)
		{
			angleBetween += 360.0;
		}
		if (angleBetween2 < 0.0)
		{
			angleBetween2 += 360.0;
		}
		if (angleBetween > 180.0)
		{
			angleBetween = 360.0 - angleBetween;
		}
		if (angleBetween2 > 180.0)
		{
			angleBetween2 = 360.0 - angleBetween2;
		}
		if ((angleBetween > 90.0 && angleBetween2 < 90.0) || (angleBetween < 90.0 && angleBetween2 > 90.0))
		{
			return false;
		}
		return true;
	}

	public virtual bool TryPreviewInsertPoint(HitTestArgs hitTest)
	{
		if (!HitTestClosestPart(hitTest, out var el, out var pointOrSegmentIndex))
		{
			return false;
		}
		if (el == PointOrSegment.Point)
		{
			return false;
		}
		if (!AreInsertAnglesValid(hitTest, pointOrSegmentIndex))
		{
			return false;
		}
		PreviewInsertPoint(hitTest.QueryPoint, pointOrSegmentIndex);
		return true;
	}

	public virtual void PreviewInsertPoint(Point queryPt, int segmentIndex, bool isOpenEnded = true)
	{
		PreviewInsertPointInternal(queryPt, segmentIndex, isOpenEnded);
	}

	protected virtual void PreviewInsertPointInternal(Point queryPt, int segmentIndex, bool isOpenEnded = true)
	{
		int index = (segmentIndex + 1) % Vector.Count;
		Point ptA = Vector[segmentIndex];
		Point ptA2 = Vector[index];
		Segment segment = new Segment(ptA, queryPt);
		Segment segment2 = new Segment(ptA2, queryPt);
		_overlay.TryGetVector(_previewSegmentChannel, out var id);
		_overlay.TryGetVector(_previewSegmentBackgroundChannel, out var id2);
		_overlay.TryGetVector(_previewPointChannel, out var id3);
		VectorManager.AddOrUpdateVector(ref id, new Segment[2] { segment, segment2 }, DefaultEditSymbology.AppendSegmentBackgroundSymbology);
		VectorManager.AddOrUpdateVector(ref id2, new Segment[2] { segment, segment2 }, DefaultEditSymbology.AppendSegmentSymbology);
		VectorManager.AddOrUpdateVector(ref id3, queryPt, DefaultEditSymbology.AppendPointSymbology);
		_overlay.RegisterVector(_previewSegmentChannel, id);
		_overlay.RegisterVector(_previewSegmentBackgroundChannel, id2);
		_overlay.RegisterVector(_previewPointChannel, id3);
	}

	public virtual bool TryInsertPoint(HitTestArgs args, out int insertedIndex, bool allowDuplicatePoints = false)
	{
		if (!HitTestClosestPart(args, out var el, out insertedIndex))
		{
			return false;
		}
		if (el == PointOrSegment.Point)
		{
			return false;
		}
		if (!AreInsertAnglesValid(args, insertedIndex))
		{
			return false;
		}
		if (!ValidateInsertPoint(args, insertedIndex, allowDuplicatePoints))
		{
			return false;
		}
		insertedIndex++;
		InsertPoint(args, insertedIndex);
		return true;
	}

	public virtual bool TryPreviewSelectPoints(HitTestArgs hitTest)
	{
		if (!HitTestClosestPart(hitTest, out var el, out var pointOrSegmentIndex))
		{
			return false;
		}
		if (el == PointOrSegment.Segment)
		{
			return false;
		}
		if (SelectedPoints.Contains(pointOrSegmentIndex))
		{
			return false;
		}
		if (IsPointInteractionDisabled(pointOrSegmentIndex))
		{
			return false;
		}
		PreviewSelectPoints(new CompactList<int>(pointOrSegmentIndex));
		return true;
	}

	public virtual void PreviewSelectPoints(HitTestArgs hitTest)
	{
		PointOnPolyline pointOnPolyline = Polyline.NearestPointOnPolyline(Vector, hitTest.QueryPoint, out var _, VectorType != VectorType.Polygon);
		int initialValue = ((pointOnPolyline.Fraction > 0.5) ? (pointOnPolyline.SegmentIndex + 1) : pointOnPolyline.SegmentIndex);
		PreviewSelectPoints(new CompactList<int>(initialValue));
	}

	public virtual void PreviewSelectPoints(IReadOnlyList<int> pointIndices)
	{
		AssertPointsArentDisabled(pointIndices);
		List<Point> points = new List<Point>();
		foreach (int pointIndex in pointIndices)
		{
			points.Add(Vector[pointIndex]);
		}
		_overlay.TryGetVector(_previewPointChannel, out var id);
		VectorManager.AddOrUpdateVector(ref id, points, DefaultEditSymbology.SelectPointSymbology);
		_overlay.RegisterVector(_previewPointChannel, id);
	}

	public virtual bool TryPreviewDeselectPoints(HitTestArgs hitTest)
	{
		if (!HitTestClosestPart(hitTest, out var el, out var pointOrSegmentIndex))
		{
			return false;
		}
		if (el == PointOrSegment.Segment)
		{
			return false;
		}
		if (!SelectedPoints.Contains(pointOrSegmentIndex))
		{
			return false;
		}
		PreviewDeselectPoints(new CompactList<int>(pointOrSegmentIndex));
		return true;
	}

	public virtual void PreviewDeselectPoints(HitTestArgs hitTest)
	{
		PointOnPolyline pointOnPolyline = Polyline.NearestPointOnPolyline(Vector, hitTest.QueryPoint, out var _, VectorType != VectorType.Polygon);
		int initialValue = ((pointOnPolyline.Fraction > 0.5) ? (pointOnPolyline.SegmentIndex + 1) : pointOnPolyline.SegmentIndex);
		PreviewDeselectPoints(new CompactList<int>(initialValue));
	}

	public virtual void PreviewDeselectPoints(IReadOnlyList<int> pointIndices)
	{
		AssertPointsArentDisabled(pointIndices);
		List<Point> points = new List<Point>();
		foreach (int pointIndex in pointIndices)
		{
			points.Add(Vector[pointIndex]);
		}
		_overlay.TryGetVector(_previewPointChannel, out var id);
		VectorManager.AddOrUpdateVector(ref id, points, DefaultEditSymbology.SelectPointSymbology);
		_overlay.RegisterVector(_previewPointChannel, id);
	}

	public virtual bool TryPreviewToggleSelectedPoints(HitTestArgs hitTest)
	{
		if (TryPreviewSelectPoints(hitTest))
		{
			return true;
		}
		if (TryPreviewDeselectPoints(hitTest))
		{
			return true;
		}
		return false;
	}

	public virtual void PreviewToggleSelectedPoints(HitTestArgs hitTest)
	{
		PointOnPolyline pointOnPolyline = Polyline.NearestPointOnPolyline(Vector, hitTest.QueryPoint, out var _, VectorType != VectorType.Polygon);
		int segmentIndex = ((pointOnPolyline.Fraction > 0.5) ? (pointOnPolyline.SegmentIndex + 1) : pointOnPolyline.SegmentIndex);
		if (SelectedPoints.Contains(segmentIndex))
		{
			PreviewDeselectPoints(new CompactList<int>(segmentIndex));
		}
		else
		{
			PreviewSelectPoints(new CompactList<int>(segmentIndex));
		}
	}

	public virtual bool TryToggleSelectedPoints(HitTestArgs hitTest)
	{
		if (!HitTestClosestPart(hitTest, out var el, out var pointOrSegmentIndex))
		{
			return false;
		}
		if (el == PointOrSegment.Segment)
		{
			return false;
		}
		ToggleSelectedPoints(pointOrSegmentIndex);
		return true;
	}

	protected override bool ValidateMoveSelectedPoints(HitTestArgs location)
	{
		foreach (int element in SelectedPoints.Elements)
		{
			if (element < 0 || element > Count)
			{
				return false;
			}
			if (IsWithinDuplicatePointTolerance(location, element - 2))
			{
				return false;
			}
			if (IsWithinDuplicatePointTolerance(location, element + 2))
			{
				return false;
			}
		}
		return true;
	}

	public virtual bool TryPreviewMoveSelectedPoints()
	{
		throw new NotImplementedException();
	}

	public virtual void PreviewMoveSelectedPoints(HitTestArgs hitTest, Point interactionStartPt, bool removeUnusedPreviewVectors = true)
	{
		PreviewMoveSelectedPointsInternal(hitTest.QueryPoint, interactionStartPt, isOpenEnded: true, removeUnusedPreviewVectors);
	}

	protected virtual void PreviewMoveSelectedPointsInternal(Point endPoint, Point interactionStartPt, bool isOpenEnded = true, bool removeUnusedPreviewVectors = true)
	{
		PreviewMovePoints(SelectedPoints.ToList(), endPoint - interactionStartPt, isOpenEnded, removeUnusedPreviewVectors);
	}

	public void PreviewMovePoints(List<int> pointIndices, Vector offsetDelta, bool isOpenEnded = true, bool removeUnusedPreviewVectors = true)
	{
		_overlay.TryGetVector(_previewSegmentChannel, out var id);
		_overlay.TryGetVector(_previewPointChannel, out var id2);
		EditFunctions.RenderPointMoveOperation(VectorManager, ref id, ref id2, Vector, offsetDelta, pointIndices, isOpenEnded && VectorType != VectorType.Polygon);
		_overlay.RegisterVector(_previewSegmentChannel, id);
		_overlay.RegisterVector(_previewPointChannel, id2);
	}

	public bool HitTestClosestPart(HitTestArgs args, out PointOrSegment el, out int pointOrSegmentIndex)
	{
		return HitTestClosestPart(Vector, args, out el, out pointOrSegmentIndex, VectorType != VectorType.Polygon);
	}

	public static bool HitTestClosestPart<K>(K vector, HitTestArgs args, out PointOrSegment el, out int pointOrSegmentIndex, bool isOpenEnded) where K : IPointCollection
	{
		double cellSize = args.Window.CellSize;
		double scaledCellSize = cellSize * 8.0;
		double scaledCellSize2 = cellSize * 16.0;
		int pointOrSegmentIndex2 = Polyline.NearestPoint(vector, args.QueryPoint);
		if (pointOrSegmentIndex2 == -1)
		{
			el = PointOrSegment.Point;
			pointOrSegmentIndex = -1;
			return false;
		}
		if (vector[pointOrSegmentIndex2].DistanceTo(args.QueryPoint) < scaledCellSize)
		{
			el = PointOrSegment.Point;
			pointOrSegmentIndex = pointOrSegmentIndex2;
			return true;
		}
		Point queryPoint = args.QueryPoint;
		PointOnPolyline pointOnPolyline = Polyline.NearestPointOnPolyline(vector, queryPoint, out var actualDistance, isOpenEnded);
		if (actualDistance < scaledCellSize2)
		{
			pointOrSegmentIndex = pointOnPolyline.SegmentIndex;
			el = PointOrSegment.Segment;
			return true;
		}
		el = PointOrSegment.Point;
		pointOrSegmentIndex = -1;
		return false;
	}

	public virtual void DrawSegmentsAndPoints(int start = 0, int end = int.MaxValue, bool forceRedraw = false)
	{
		if (_lastInvalidatedVersion != VersionXY || forceRedraw)
		{
			_lastInvalidatedVersion = VersionXY;
			if (start == 0 && end == int.MaxValue)
			{
				VectorManager.AddOrUpdateVector(ref _openFeatureSegmentsVectorId, Vector, DefaultEditSymbology.OpenSymbology, VectorType);
				VectorManager.AddOrUpdateVector(ref _openFeaturePointsVectorId, Vector, DefaultEditSymbology.InternalPointSymbology);
			}
			else
			{
				VectorManager.AddOrUpdateVector(ref _openFeatureSegmentsVectorId, Vector.Skip(start).Take(end + 1).ToList(), DefaultEditSymbology.OpenSymbology, VectorType);
				VectorManager.AddOrUpdateVector(ref _openFeaturePointsVectorId, Vector.Skip(start).Take(end + 1).ToList(), DefaultEditSymbology.InternalPointSymbology);
			}
		}
	}

	public void Redraw(bool forceRedraw = false)
	{
		base.Redraw();
		DrawSegmentsAndPoints(0, int.MaxValue, forceRedraw);
	}

	public override void Redraw()
	{
		Redraw();
	}

	public override bool IsValid()
	{
		if (Polyline.GetAllSelfIntersections_Slow(Vector).Count > 0)
		{
			return false;
		}
		if (Vector.Count < 2)
		{
			return false;
		}
		return true;
	}
}
