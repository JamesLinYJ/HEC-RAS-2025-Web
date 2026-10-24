using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class InsertPointOperation : PointOperationBase
{
	private readonly Point _pt;

	private readonly int _insertIdx;

	public override string Description => $"Insert Point ({_pt.X},{_pt.Y})";

	public InsertPointOperation(Func<IPointCollection> getPc, int insertIdx, HitTestArgs args, SelectedSet selectedPoints)
		: base(selectedPoints, getPc, args)
	{
		_pt = _location.QueryPoint;
		_insertIdx = insertIdx;
	}

	protected override bool ApplyInternal()
	{
		if (!base.ApplyInternal())
		{
			return false;
		}
		_getPointCollection().Insert(_insertIdx, _pt);
		_selectedSet.Clear();
		_selectedSet.Add(_insertIdx);
		return true;
	}

	protected override bool UndoInternal()
	{
		if (!base.UndoInternal())
		{
			return false;
		}
		_getPointCollection().RemoveAt(_insertIdx);
		return true;
	}
}
