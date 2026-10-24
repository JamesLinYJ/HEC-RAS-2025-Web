using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class DeleteFeatureOperation<T> : EditOperation
{
	private readonly IVectorCollection<T> _pc;

	private int _idx;

	private SelectedSet _set;

	private T _removed;

	private bool _wasSelected;

	public override string Description => "Delete feature";

	public DeleteFeatureOperation(IVectorCollection<T> pc, SelectedSet set, int idx)
		: base(null)
	{
		_idx = idx;
		_pc = pc;
		_set = set;
	}

	protected override bool ApplyInternal()
	{
		_wasSelected = _set.Contains(_idx);
		_set.Clear();
		_removed = _pc[_idx];
		_pc.RemoveAt(_idx);
		return true;
	}

	protected override bool UndoInternal()
	{
		if (_idx >= _pc.Count)
		{
			_pc.Add(_removed);
		}
		else
		{
			_pc.Insert(_idx, _removed);
		}
		if (_wasSelected)
		{
			_set.Add(_idx);
		}
		return true;
	}
}
