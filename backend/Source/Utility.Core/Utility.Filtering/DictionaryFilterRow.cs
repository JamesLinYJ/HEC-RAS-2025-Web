using System.Collections.Generic;

namespace Utility.Filtering;

public class DictionaryFilterRow : IFilterRow, IFilterFieldSource
{
	private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

	private readonly List<string> _knownNames = new List<string>();

	public IReadOnlyList<string> KnownNames => _knownNames;

	public void Set(string name, object localValue)
	{
		string key = FilterNames.Normalize(name);
		if (!_values.ContainsKey(key))
		{
			_knownNames.Add(name);
		}
		_values[key] = localValue;
	}

	public bool HasField(string normalizedName)
	{
		return _values.ContainsKey(normalizedName);
	}

	public bool TryGetValue(string normalizedName, out object localValue)
	{
		return _values.TryGetValue(normalizedName, out localValue);
	}
}
