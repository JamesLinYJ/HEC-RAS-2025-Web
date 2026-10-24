using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class NewFeatureOperation<T> : EditOperation
{
	private readonly IVectorCollection<T> _collection;

	private readonly SelectedSet _selection;

	private readonly Func<T> _factory;

	private SelectedSet _previousSelection;

	private T _feature;

	public override string Description => "Add feature";

	public NewFeatureOperation(IVectorCollection<T> collection, SelectedSet selection, Func<T> factory)
		: base(null)
	{
		_collection = collection ?? throw new ArgumentNullException("collection");
		_selection = selection ?? new SelectedSet();
		_factory = factory ?? throw new ArgumentNullException("factory");
		_previousSelection = new SelectedSet(_selection);
		_feature = _factory();
	}

	protected override bool ApplyInternal()
	{
		_collection.Add(_feature);
		return true;
	}

	protected override bool UndoInternal()
	{
		_collection.RemoveAt(_collection.Count - 1);
		_selection.Clear();
		foreach (int element in _previousSelection.Elements)
		{
			_selection.Add(element);
		}
		return true;
	}
}
