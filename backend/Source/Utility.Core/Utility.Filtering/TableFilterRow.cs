using System.Collections.Generic;
using Utility.Memory;

namespace Utility.Filtering;

public class TableFilterRow : IFilterRow, IFilterFieldSource
{
	private readonly Dictionary<string, int> _columnsByNormalizedName = new Dictionary<string, int>();

	private readonly List<string> _knownNames = new List<string>();

	public TableRow Row { get; set; }

	public IReadOnlyList<string> KnownNames => _knownNames;

	public TableFilterRow(Table table, IReadOnlyDictionary<string, string> aliases = null)
	{
		for (int i = 0; i < table.Columns.Count; i++)
		{
			_columnsByNormalizedName[FilterNames.Normalize(table.Columns[i].Name)] = i;
			_knownNames.Add(table.Columns[i].Name);
		}
		if (aliases == null)
		{
			return;
		}
		foreach (KeyValuePair<string, string> alias in aliases)
		{
			if (table.TryGetColumnIndex(alias.Value, out var idx))
			{
				_columnsByNormalizedName[FilterNames.Normalize(alias.Key)] = idx;
				_knownNames.Add(alias.Key);
			}
		}
	}

	public bool HasField(string normalizedName)
	{
		return _columnsByNormalizedName.ContainsKey(normalizedName);
	}

	public bool TryGetValue(string normalizedName, out object localValue2)
	{
		localValue2 = null;
		if (Row == null)
		{
			return false;
		}
		if (!_columnsByNormalizedName.TryGetValue(normalizedName, out var localValue))
		{
			return false;
		}
		localValue2 = Row[localValue];
		return true;
	}
}
