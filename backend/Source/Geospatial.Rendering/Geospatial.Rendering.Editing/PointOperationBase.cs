using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public abstract class PointOperationBase : EditOperation
{
	protected readonly SelectedSet _selectedSet;

	protected SelectedSet _cachedSelection;

	private long _cachedSelectionVersion;

	protected Func<IPointCollection> _getPointCollection;

	public PointOperationBase(SelectedSet selectedPoints, Func<IPointCollection> getPointCollection, HitTestArgs args)
		: base(args)
	{
		_selectedSet = selectedPoints;
		_cachedSelection = new SelectedSet(_selectedSet);
		_cachedSelectionVersion = _selectedSet.VersionXY;
		_getPointCollection = getPointCollection;
	}

	protected override bool ApplyInternal()
	{
		return true;
	}

	protected override bool UndoInternal()
	{
		_selectedSet.Clear();
		foreach (int element in _cachedSelection.Elements)
		{
			_selectedSet.Add(element);
		}
		_selectedSet.ForceSetVersion(_cachedSelectionVersion);
		return true;
	}
}
