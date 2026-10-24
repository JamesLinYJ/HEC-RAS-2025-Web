using System;
using System.Collections.Generic;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class AppendPointsOperation : PointOperationBase
{
	private readonly IReadOnlyList<Point> _pts;

	public override string Description => $"Add {_pts.Count} Points";

	public AppendPointsOperation(Func<IPointCollection> getPc, IReadOnlyList<Point> points, HitTestArgs args, SelectedSet selectedPoints)
		: base(selectedPoints, getPc, args)
	{
		_pts = points;
	}

	protected override bool ApplyInternal()
	{
		if (!base.ApplyInternal())
		{
			return false;
		}
		IPointCollection pointCollection = _getPointCollection();
		for (int i = 0; i < _pts.Count; i++)
		{
			pointCollection.Add(_pts[i]);
		}
		_selectedSet.Clear();
		_selectedSet.Add(pointCollection.Count - 1);
		return true;
	}

	protected override bool UndoInternal()
	{
		if (!base.UndoInternal())
		{
			return false;
		}
		IPointCollection pointCollection = _getPointCollection();
		for (int i = 0; i < _pts.Count; i++)
		{
			pointCollection.RemoveAt(pointCollection.Count - 1);
		}
		return true;
	}
}
