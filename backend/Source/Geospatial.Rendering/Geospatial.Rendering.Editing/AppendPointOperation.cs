using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class AppendPointOperation : PointOperationBase
{
	private readonly Point _pt;

	public override string Description => $"Add Point ({_pt.X},{_pt.Y})";

	public Point Point => _pt;

	public AppendPointOperation(Func<IPointCollection> getPc, HitTestArgs args, SelectedSet selectedPoints)
		: base(selectedPoints, getPc, args)
	{
		_pt = args.QueryPoint;
	}

	protected override bool ApplyInternal()
	{
		if (!base.ApplyInternal())
		{
			return false;
		}
		IPointCollection pointCollection = _getPointCollection();
		pointCollection.Add(_pt);
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
		pointCollection.RemoveAt(pointCollection.Count - 1);
		return true;
	}
}
