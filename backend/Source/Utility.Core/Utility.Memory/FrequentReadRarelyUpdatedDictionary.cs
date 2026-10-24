using System;
using System.Collections.Generic;

namespace Utility.Memory;

public class FrequentReadRarelyUpdatedDictionary<TKey, TValue>
{
	private Dictionary<TKey, TValue> _live;

	private readonly Func<TKey, TValue> _genFunc;

	public TValue this[TKey key]
	{
		get
		{
			return TryGet(key);
		}
		set
		{
			lock (_genFunc)
			{
				Dictionary<TKey, TValue> live = new Dictionary<TKey, TValue>(_live);
				live[key] = value;
				_live = live;
			}
		}
	}

	public int Count => _live.Count;

	public FrequentReadRarelyUpdatedDictionary(Func<TKey, TValue> generatorFunc)
	{
		_live = new Dictionary<TKey, TValue>();
		_genFunc = generatorFunc;
	}

	public TValue TryGet(TKey key)
	{
		if (_live.TryGetValue(key, out var genFunc))
		{
			return genFunc;
		}
		lock (_genFunc)
		{
			if (_live.TryGetValue(key, out genFunc))
			{
				return genFunc;
			}
			genFunc = _genFunc(key);
			Dictionary<TKey, TValue> live = new Dictionary<TKey, TValue>(_live);
			live[key] = genFunc;
			_live = live;
			return genFunc;
		}
	}
}
