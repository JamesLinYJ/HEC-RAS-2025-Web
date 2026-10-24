using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Rendering.Layers;

public class SelectedSet : IMutableSpatialVersion, ISpatialVersion, IEnumerable<int>, IEnumerable
{
	public delegate void ElementAddedEvent(SelectedSet owner, int id);

	public delegate void ElementRemovedEvent(SelectedSet owner, int id);

	public delegate void ClearedEvent(SelectedSet owner);

	private long _version;

	private readonly HashSet<int> _hs = new HashSet<int>();

	public long VersionXY => _version;

	public int Count => _hs.Count;

	public bool IsEmpty => Count == 0;

	public IEnumerable<int> Elements => _hs;

	public event ElementAddedEvent ElementAdded;

	public event ElementRemovedEvent ElementRemoved;

	public event ClearedEvent Cleared;

	public void FreezeOne()
	{
	}

	public void ThawOne()
	{
	}

	public SelectedSet()
	{
	}

	public SelectedSet(SelectedSet cloneFrom)
	{
		foreach (int element in cloneFrom.Elements)
		{
			Add(element);
		}
	}

	private SelectedSet(HashSet<int> hs)
	{
		_hs = hs.ToHashSet();
	}

	private void IncrementVersion()
	{
		_version++;
	}

	public bool Contains(int idx)
	{
		return _hs.Contains(idx);
	}

	public bool ContainsAll(IEnumerable<int> ids)
	{
		foreach (int id in ids)
		{
			if (!_hs.Contains(id))
			{
				return false;
			}
		}
		return true;
	}

	public void Add(int idx)
	{
		if (_hs.Add(idx))
		{
			IncrementVersion();
			ElementAdded?.Invoke(this, idx);
		}
	}

	public void AddRange(int start, int count)
	{
		for (int i = start; i < start + count; i++)
		{
			if (_hs.Add(i))
			{
				ElementAdded?.Invoke(this, i);
			}
		}
		IncrementVersion();
	}

	public void Remove(int idx)
	{
		if (_hs.Remove(idx))
		{
			IncrementVersion();
			ElementRemoved?.Invoke(this, idx);
		}
	}

	public void Clear()
	{
		if (Count != 0)
		{
			_hs.Clear();
			IncrementVersion();
			Cleared?.Invoke(this);
		}
	}

	public SelectedSet Clone()
	{
		return new SelectedSet(_hs);
	}

	public void ForceSetVersion(long version)
	{
		_version = version;
	}

	public IEnumerator<int> GetEnumerator()
	{
		return Elements.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return Elements.GetEnumerator();
	}
}
