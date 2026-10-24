using System.Collections.Generic;

namespace Utility.DataStructures;

public class Map<T1, T2> : IReadOnlyMap<T1, T2>
{
	private readonly Dictionary<T1, T2> _forward = new Dictionary<T1, T2>();

	private readonly Dictionary<T2, T1> _backward = new Dictionary<T2, T1>();

	public IReadOnlyDictionary<T1, T2> Forward => _forward;

	public IReadOnlyDictionary<T2, T1> Backward => _backward;

	public IEnumerable<T1> Keys => Forward.Keys;

	public IEnumerable<T2> Values => Backward.Keys;

	public int Count => Forward.Count;

	public T1 this[T2 index] => Backward[index];

	public T2 this[T1 index] => Forward[index];

	public Map()
	{
	}

	public Map(Dictionary<T1, T2> t2ByT1)
	{
		foreach (KeyValuePair<T1, T2> localValue in t2ByT1)
		{
			_forward.Add(localValue.Key, localValue.Value);
			_backward.Add(localValue.Value, localValue.Key);
		}
	}

	public void Add(T1 t1, T2 t2)
	{
		_forward[t1] = t2;
		_backward[t2] = t1;
	}

	public bool TryAdd(T1 t1, T2 t2)
	{
		if (_forward.TryAdd(t1, t2))
		{
			if (_backward.TryAdd(t2, t1))
			{
				return true;
			}
			_forward.Remove(t1);
		}
		return false;
	}

	public void Remove(T1 t1)
	{
		T2 key = this[t1];
		_forward.Remove(t1);
		_backward.Remove(key);
	}

	public bool ContainsKey(T1 key)
	{
		return _forward.ContainsKey(key);
	}

	public bool ContainsValue(T2 t2)
	{
		return _backward.ContainsKey(t2);
	}

	public bool TryGetValue(T1 key, out T2 t2)
	{
		return _forward.TryGetValue(key, out t2);
	}

	public bool TryGetKey(T2 t2, out T1 key)
	{
		return _backward.TryGetValue(t2, out key);
	}

	public void Clear()
	{
		_forward.Clear();
		_backward.Clear();
	}

	public Map<T1, T2> ToMap()
	{
		return Clone();
	}

	public Map<T1, T2> Clone()
	{
		return new Map<T1, T2>(_forward);
	}
}
