using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class DeletePointOperation : PointOperationBase
{
	private Dictionary<int, Point> _deletedPoints;

	public override string Description => "Delete point";

	public DeletePointOperation(Func<IPointCollection> getPc, SelectedSet selectedPoints)
		: base(selectedPoints, getPc, null)
	{
		_deletedPoints = new Dictionary<int, Point>();
	}

	protected override bool ApplyInternal()
	{
		if (!base.ApplyInternal())
		{
			return false;
		}
		IPointCollection pointCollection = _getPointCollection();
		List<int> orderByDescending = new List<int>(_cachedSelection.Elements.OrderByDescending((int idx) => idx));
		foreach (int orderByDescending2 in orderByDescending)
		{
			Point pointCollection2 = pointCollection[orderByDescending2];
			_deletedPoints[orderByDescending2] = pointCollection2;
			pointCollection.RemoveAt(orderByDescending2);
		}
		_selectedSet.Clear();
		if (pointCollection.Count > 2 && orderByDescending.Count != 0)
		{
			int orderByDescendingPointCollectionTerm = Math.Min(orderByDescending[orderByDescending.Count - 1], pointCollection.Count - 1);
			if (orderByDescendingPointCollectionTerm >= 0)
			{
				_selectedSet.Add(orderByDescendingPointCollectionTerm);
			}
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
		_selectedSet.Clear();
		foreach (int orderBy in _deletedPoints.Keys.OrderBy((int a) => a))
		{
			Point deletedPoint = _deletedPoints[orderBy];
			if (orderBy >= pointCollection.Count)
			{
				pointCollection.Add(deletedPoint);
			}
			else
			{
				pointCollection.Insert(orderBy, deletedPoint);
			}
			_selectedSet.Add(orderBy);
		}
		return true;
	}
}
