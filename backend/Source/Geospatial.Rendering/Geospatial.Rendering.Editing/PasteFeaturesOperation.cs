using System;
using System.Collections.Generic;

namespace Geospatial.Rendering.Editing;

public class PasteFeaturesOperation<T> : EditOperation
{
	private readonly IReadOnlyList<T> _features;

	private readonly Func<T, int, int> _addFeature;

	private readonly Action<T, int> _removeFeature;

	private readonly List<(T Feature, int Index)> _added = new List<(T, int)>();

	public override string Description => "Paste features";

	public PasteFeaturesOperation(IEnumerable<T> features, Func<T, int, int> addFeature, Action<T, int> removeFeature)
		: base(null)
	{
		IReadOnlyList<T> features2;
		if (!(features is IReadOnlyList<T> readOnlyList))
		{
			List<T> items = new List<T>();
			items.AddRange(features);
			IReadOnlyList<T> readOnlyList2 = new _003C_003Ez__ReadOnlyList<T>(items);
			features2 = readOnlyList2;
		}
		else
		{
			features2 = readOnlyList;
		}
		_features = features2;
		_addFeature = addFeature;
		_removeFeature = removeFeature;
	}

	protected override bool ApplyInternal()
	{
		_added.Clear();
		for (int i = 0; i < _features.Count; i++)
		{
			T feature = _features[i];
			int addFeature = _addFeature(feature, i);
			_added.Add((feature, addFeature));
		}
		return true;
	}

	protected override bool UndoInternal()
	{
		for (int countOffset = _added.Count - 1; countOffset >= 0; countOffset--)
		{
			var (localValue, localValue2) = _added[countOffset];
			_removeFeature(localValue, localValue2);
		}
		return true;
	}
}
