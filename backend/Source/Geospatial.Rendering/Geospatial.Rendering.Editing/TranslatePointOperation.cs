using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class TranslatePointOperation : PointOperationBase
{
	private readonly Vector _vec;

	private Dictionary<int, bool> _combinedPtIndices;

	public override string Description => $"Translate Points ({_vec.X},{_vec.Y})";

	public Vector Delta => _vec;

	public TranslatePointOperation(Func<IPointCollection> getPc, SelectedSet set, HitTestArgs location, Vector vec)
		: base(set, getPc, location)
	{
		_vec = vec;
	}

	private Dictionary<int, bool> GetCombinedPtIndices()
	{
		if (_combinedPtIndices == null)
		{
			_combinedPtIndices = new Dictionary<int, bool>();
		}
		return _combinedPtIndices;
	}

	protected override bool ApplyInternal()
	{
		if (!base.ApplyInternal())
		{
			return false;
		}
		IPointCollection pointCollection = _getPointCollection();
		if (_combinedPtIndices != null)
		{
			_combinedPtIndices.Clear();
		}
		foreach (int orderDescending in _cachedSelection.Elements.OrderDescending())
		{
			Point pt = (pointCollection[orderDescending] = pointCollection[orderDescending].Translate(_vec));
			int orderDescendingOffset = orderDescending - 1;
			int orderDescendingOffset2 = orderDescending + 1;
			if (orderDescending != 0 && !_cachedSelection.Contains(orderDescendingOffset) && EditableVectorHelpers.IsHitTestWithinDuplicatePointTolerance(pt, pointCollection[orderDescendingOffset], _location.Window.CellSize))
			{
				GetCombinedPtIndices().Add(orderDescending, value: false);
				pointCollection.RemoveAt(orderDescending);
			}
			else if (orderDescending != pointCollection.Count - 1 && !_cachedSelection.Contains(orderDescendingOffset2) && EditableVectorHelpers.IsHitTestWithinDuplicatePointTolerance(pt, pointCollection[orderDescendingOffset2], _location.Window.CellSize))
			{
				GetCombinedPtIndices().Add(orderDescending, value: true);
				pointCollection.RemoveAt(orderDescending);
			}
		}
		if (GetCombinedPtIndices().Count > 0)
		{
			_selectedSet.Clear();
		}
		return true;
	}

	protected override bool UndoInternal()
	{
		if (!base.UndoInternal())
		{
			return false;
		}
		IPointCollection pointCollection = _getPointCollection();
		RebuildCombinedInternalPoints();
		foreach (int element in _cachedSelection.Elements)
		{
			pointCollection[element] = pointCollection[element].Translate(_vec.Negate());
		}
		return true;
	}

	private void RebuildCombinedInternalPoints()
	{
		IPointCollection pointCollection = _getPointCollection();
		foreach (int order in GetCombinedPtIndices().Keys.Order())
		{
			if (GetCombinedPtIndices()[order])
			{
				pointCollection.Insert(order, pointCollection[order]);
			}
			else
			{
				pointCollection.Insert(order, pointCollection[order - 1]);
			}
		}
	}
}
