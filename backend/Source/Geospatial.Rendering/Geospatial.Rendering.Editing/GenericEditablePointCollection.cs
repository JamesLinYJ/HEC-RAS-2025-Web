using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Helpers;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.DataStructures;
using Utility.Logging;

namespace Geospatial.Rendering.Editing;

public abstract class GenericEditablePointCollection<T> : EditableVector<T>, IEditablePointCollection where T : IPointCollection, IReadOnlyList<Point>, ISpatiallyIndexedPointCollection
{
	private static readonly ILogger _logger = LogHelper.GetLogger("GenericEditablePointCollection");

	protected VectorID _selectedPointsVectorId;

	private ExtentSelector _selector;

	protected Polygon _selectionPreviewPolygon = new Polygon
	{
		Point.NullPoint,
		Point.NullPoint,
		Point.NullPoint,
		Point.NullPoint
	};

	protected long _lastInvalidatedVersion = -1L;

	protected long _lastSelectionSelectedSetVersion = -1L;

	protected long _lastSelectionVectorVersion = -1L;

	public SelectedSet SelectedPoints { get; }

	protected virtual long VersionXY => Vector.VersionXY;

	public int Count => Vector.Count;

	public bool IsSelectingPoints => _selector.IsActive;

	public GenericEditablePointCollection(T vector, IVectorManager vectorManager, VectorType vectorType, Func<EditOperation, bool, bool> executeEditOperation, SelectedSet selectionCache, OverlayChannels overlayChannels)
		: base(vector, vectorManager, vectorType, executeEditOperation, overlayChannels)
	{
		SelectedPoints = selectionCache ?? new SelectedSet();
		_selector = new ExtentSelector(vectorManager, (Extent ext, SelectMode mode) =>
		{
			TrySelectPoints(ext, mode);
		});
	}

	protected void ExecuteOperation(EditOperation op, bool silent = false)
	{
		ExecuteEditOperation?.Invoke(op, silent);
	}

	public override void ClearAllVectors()
	{
		base.ClearAllVectors();
		VectorManager.TryDeleteVector(ref _selectedPointsVectorId);
	}

	protected bool IsHitTestDuplicateOfNeighborsInsertAppend(HitTestArgs args, int index)
	{
		if (index < 0 || index > Count)
		{
			return false;
		}
		if (IsWithinDuplicatePointTolerance(args, index - 1))
		{
			return true;
		}
		if (IsWithinDuplicatePointTolerance(args, index))
		{
			return true;
		}
		return false;
	}

	protected bool IsWithinDuplicatePointTolerance(HitTestArgs args, int checkIndex)
	{
		if (checkIndex < 0 || checkIndex >= Vector.Count)
		{
			return false;
		}
		return EditableVectorHelpers.IsHitTestWithinDuplicatePointTolerance(args.QueryPoint, ((IReadOnlyList<Point>)Vector)[checkIndex], args.Window.CellSize);
	}

	protected bool ValidateAppendPoint(HitTestArgs args, bool allowDuplicatePoints)
	{
		if (!allowDuplicatePoints && IsHitTestDuplicateOfNeighborsInsertAppend(args, Vector.Count))
		{
			return false;
		}
		return true;
	}

	public virtual bool TryAppendPoint(HitTestArgs args, bool allowDuplicatePoints = false)
	{
		if (!ValidateAppendPoint(args, allowDuplicatePoints))
		{
			return false;
		}
		AppendPoint(args);
		return true;
	}

	public virtual void AppendPoint(HitTestArgs args, bool allowDuplicatePoints = false)
	{
		if (!ValidateAppendPoint(args, allowDuplicatePoints))
		{
			throw new InvalidOperationException("Tried to append a point in an invalid position.");
		}
		ExecuteOperation(new AppendPointOperation(() => Vector, args, SelectedPoints));
	}

	public virtual void PrependPoint(HitTestArgs hitTest)
	{
		InsertPoint(hitTest, 0);
	}

	protected virtual bool ValidateInsertPoint(HitTestArgs args, int index, bool allowDuplicatePoints)
	{
		if (!allowDuplicatePoints && IsHitTestDuplicateOfNeighborsInsertAppend(args, index))
		{
			return false;
		}
		return true;
	}

	public virtual void InsertPoint(HitTestArgs hitTest, int index, bool allowDuplicatePoints = false)
	{
		if (!ValidateInsertPoint(hitTest, index, allowDuplicatePoints))
		{
			throw new InvalidOperationException("Tried to insert a point in an invalid position.");
		}
		ExecuteOperation(new InsertPointOperation(() => Vector, index, hitTest, SelectedPoints));
	}

	public virtual void SelectPoints(int pointIndex)
	{
		SelectPoints(new CompactList<int>(pointIndex));
	}

	public virtual void SelectPoints(IReadOnlyList<int> pointIndices)
	{
		for (int i = 0; i < pointIndices.Count; i++)
		{
			int pointIndex = pointIndices[i];
			if (!IsPointInteractionDisabled(pointIndex))
			{
				SelectedPoints.Add(pointIndex);
			}
		}
		DrawSelectedPoints();
	}

	public virtual bool TrySelectPoints(HitTestArgs args, SelectMode mode = SelectMode.Append)
	{
		if (!VectorRenderLayer.SelectHelperPoint(HitTestHelpers.HitTest(args.QueryPoint, Vector.PointSpatialIndex, (int idx, Point pt, double dist) => PointRenderLayer.HitTest(Vector, idx, pt, dist), 8.0 * args.Window.CellSize, (int idx, Point pt) => Vector[idx].DistanceTo(pt), HitTestSortOrder.Distance), args, mode, SelectedPoints))
		{
			return false;
		}
		DrawSelectedPoints();
		return true;
	}

	public void BeginSelectPoints(Point pt)
	{
		_selector.Begin(pt);
	}

	public void EndSelectPoints(Point pt, SelectMode mode = SelectMode.Replace)
	{
		_selector.End(pt, mode);
	}

	public void PreviewSelectingPoints(Point pt)
	{
		_selector.Render(pt);
	}

	public virtual bool TrySelectPoints(Extent ext, SelectMode mode = SelectMode.Append)
	{
		bool result = VectorRenderLayer.SelectHelper(ext, SelectedPoints, Vector.Count, mode, Vector, ext, (int fid, T model, Extent extent) => PointRenderLayer.HitTest(model, fid, extent), (T vector) => vector.PointSpatialIndex);
		RemoveDisabledSelectedPoints();
		DrawSelectedPoints();
		return result;
	}

	protected void RemoveDisabledSelectedPoints()
	{
		CompactList3<int> compactList = default;
		foreach (int element in SelectedPoints.Elements)
		{
			if (IsPointInteractionDisabled(element))
			{
				compactList.Add(element);
			}
		}
		for (int i = 0; i < compactList.Count; i++)
		{
			SelectedPoints.Remove(compactList[i]);
		}
	}

	public virtual void DeselectPoints(int pointIndex)
	{
		DeselectPoints(new CompactList<int>(pointIndex));
	}

	public virtual void DeselectPoints(IReadOnlyList<int> pointIndices)
	{
		for (int i = 0; i < pointIndices.Count; i++)
		{
			int pointIndex = pointIndices[i];
			if (!IsPointInteractionDisabled(pointIndex))
			{
				SelectedPoints.Remove(pointIndex);
			}
		}
		DrawSelectedPoints();
	}

	public virtual void ToggleSelectedPoints(int pointIndex)
	{
		ToggleSelectedPoints(new CompactList<int>(pointIndex));
	}

	public virtual void ToggleSelectedPoints(IReadOnlyList<int> pointIndices)
	{
		for (int i = 0; i < pointIndices.Count; i++)
		{
			int pointIndex = pointIndices[i];
			if (!IsPointInteractionDisabled(pointIndex))
			{
				if (SelectedPoints.Contains(pointIndex))
				{
					SelectedPoints.Remove(pointIndex);
				}
				else
				{
					SelectedPoints.Add(pointIndex);
				}
			}
		}
		DrawSelectedPoints();
	}

	protected virtual bool ValidateMoveSelectedPoints(HitTestArgs location)
	{
		return true;
	}

	public virtual bool TryMoveSelectedPoints(HitTestArgs location, Vector deltaOffset, bool silently = false)
	{
		AssertSelectedPointsArentDisabled();
		if (!ValidateMoveSelectedPoints(location))
		{
			return false;
		}
		MoveSelectedPoints(location, deltaOffset, silently);
		return true;
	}

	public virtual void MoveSelectedPoints(HitTestArgs location, Vector deltaOffset, bool silently = false)
	{
		AssertSelectedPointsArentDisabled();
		if (!ValidateMoveSelectedPoints(location))
		{
			throw new InvalidOperationException("Moving points to once removed neighbors, causing overlapping segments");
		}
		ExecuteOperation(new TranslatePointOperation(() => Vector, SelectedPoints, location, deltaOffset), silently);
		VectorManager.AddOrUpdateVector(ref _openFeatureSegmentsVectorId, Vector, DefaultEditSymbology.OpenSymbology);
		VectorManager.AddOrUpdateVector(ref _openFeaturePointsVectorId, Vector, DefaultEditSymbology.InternalPointSymbology);
	}

	public virtual void DeleteSelectedPoints()
	{
		AssertSelectedPointsArentDisabled();
		ExecuteOperation(new DeletePointOperation(() => Vector, SelectedPoints));
		Redraw();
	}

	public virtual void ClearSelectedPoints()
	{
		AssertSelectedPointsArentDisabled();
		SelectedPoints.Clear();
		DrawSelectedPoints();
	}

	public virtual bool IsPointInteractionDisabled(int ptIndex)
	{
		return false;
	}

	protected void AssertSelectedPointsArentDisabled()
	{
		AssertPointsArentDisabled(SelectedPoints.Elements);
	}

	protected void AssertPointsArentDisabled(IEnumerable<int> pointIndices)
	{
		foreach (int pointIndex in pointIndices)
		{
			AssertPointIsntDisabled(pointIndex);
		}
	}

	protected void AssertPointIsntDisabled(int idx)
	{
		if (IsPointInteractionDisabled(idx))
		{
			throw new InvalidOperationException("Can't interact with a disabled point.");
		}
	}

	private void DrawSelectedPoints()
	{
		List<Point> points = SelectedPoints.Elements.Select((int a) => Vector[a]).ToList();
		VectorManager.AddOrUpdateVector(ref _selectedPointsVectorId, points, DefaultEditSymbology.SelectedPointSymbology);
		VectorManager.TrySetVectorZIndex(_selectedPointsVectorId, 10);
	}

	public virtual void Redraw()
	{
		try
		{
			RedrawSelection();
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to redraw the point collection / line selection.");
		}
	}

	private void RedrawSelection()
	{
		if (_lastSelectionSelectedSetVersion != SelectedPoints.VersionXY || _lastSelectionVectorVersion != VersionXY)
		{
			_lastSelectionSelectedSetVersion = SelectedPoints.VersionXY;
			_lastSelectionVectorVersion = VersionXY;
			VectorManager.AddOrUpdateVector(ref _selectedPointsVectorId, (from a in SelectedPoints.Elements
				where !IsPointInteractionDisabled(a)
				select Vector[a]).ToList(), DefaultEditSymbology.SelectedPointSymbology);
			VectorManager.TrySetVectorZIndex(_selectedPointsVectorId, 10);
		}
	}

	protected bool IsDuplicatePoint(Point pt, params int[] indicesToCheck)
	{
		if (Count == 0)
		{
			return false;
		}
		IReadOnlyList<Point> readOnlyList = Vector;
		foreach (int indicesToCheck2 in indicesToCheck)
		{
			if (indicesToCheck2 >= 0 && indicesToCheck2 < Count && pt.EqualXY(readOnlyList[indicesToCheck2]))
			{
				return true;
			}
		}
		return false;
	}
}
