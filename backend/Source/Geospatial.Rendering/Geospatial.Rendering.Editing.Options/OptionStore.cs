using System;
using System.Collections.Generic;

namespace Geospatial.Rendering.Editing.Options;

public sealed class OptionStore
{
	private readonly Dictionary<OptionKey, object> _values;

	public static OptionStore Empty { get; } = new OptionStore();

	public IReadOnlyList<OptionKey> Keys { get; }

	public int Count => Keys.Count;

	public OptionStore(params OptionKey[] keys)
	{
		_values = new Dictionary<OptionKey, object>();
		Keys = keys ?? Array.Empty<OptionKey>();
	}

	private OptionStore(Dictionary<OptionKey, object> values, OptionKey[] keys)
	{
		_values = values;
		Keys = keys;
	}

	public OptionStore View(params OptionKey[] keys)
	{
		return new OptionStore(_values, keys ?? Array.Empty<OptionKey>());
	}

	public T Get<T>(OptionKey<T> key)
	{
		if (key == null)
		{
			throw new ArgumentNullException("key");
		}
		if (_values.TryGetValue(key, out var localValue))
		{
			return (T)localValue;
		}
		return key.DefaultValue;
	}

	public bool TryGet<T>(OptionKey<T> key, out T localValue2)
	{
		if (key == null)
		{
			throw new ArgumentNullException("key");
		}
		if (_values.TryGetValue(key, out var localValue))
		{
			localValue2 = (T)localValue;
			return true;
		}
		localValue2 = default;
		return false;
	}

	public void Set<T>(OptionKey<T> key, T localValue)
	{
		if (key == null)
		{
			throw new ArgumentNullException("key");
		}
		_values[key] = localValue;
	}
}
