using System;
using System.Collections.Generic;
using Utility.Filtering;
using Utility.Memory;

namespace Ras.Process.GeometryCommands;

internal sealed class TopologyFieldSchema
{
	private readonly Dictionary<string, (Type Type, bool Settable)> _fieldTypes = new Dictionary<string, (Type, bool)>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> _displayByNormalized = new Dictionary<string, string>(StringComparer.Ordinal);

	public List<string> ProjectionFields { get; } = new List<string>();

	public List<string> KnownFieldNames { get; } = new List<string>();

	public HashSet<string> DerivedNormalized { get; } = new HashSet<string>(StringComparer.Ordinal);

	public Dictionary<string, string> MetaCanonical { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

	public TopologyFieldSchema(IReadOnlyList<(string Name, Type Type)> derived, Table metaTable, IReadOnlyDictionary<string, string> aliases)
	{
		foreach (var derived2 in derived)
		{
			string displayByNormalized = derived2.Name;
			Type derived2Term = derived2.Type;
			string normalize = FilterNames.Normalize(displayByNormalized);
			DerivedNormalized.Add(normalize);
			_fieldTypes[normalize] = (derived2Term, false);
			_displayByNormalized[normalize] = displayByNormalized;
			ProjectionFields.Add(displayByNormalized);
			KnownFieldNames.Add(displayByNormalized);
		}
		foreach (TableColumn column in metaTable.Columns)
		{
			if (!column.Name.StartsWith("COMPUTED_", StringComparison.Ordinal))
			{
				string key = FilterNames.Normalize(column.Name);
				MetaCanonical[key] = column.Name;
				_fieldTypes[key] = (column.Type, true);
				_displayByNormalized[key] = column.Name;
				ProjectionFields.Add(column.Name);
				KnownFieldNames.Add(column.Name);
			}
		}
		foreach (KeyValuePair<string, string> alias in aliases)
		{
			if (metaTable.TryGetColumnIndex(alias.Value, out var idx))
			{
				string key2 = FilterNames.Normalize(alias.Key);
				MetaCanonical[key2] = alias.Value;
				_fieldTypes[key2] = (metaTable.Columns[idx].Type, true);
				_displayByNormalized[key2] = alias.Key;
				KnownFieldNames.Add(alias.Key);
			}
		}
	}

	public bool TryGetField(string normalizedName, out Type type, out bool settable)
	{
		if (_fieldTypes.TryGetValue(normalizedName, out (Type, bool) valueTuple))
		{
			(type, settable) = valueTuple;
			return true;
		}
		type = null;
		settable = false;
		return false;
	}

	public string DisplayOf(string normalizedName)
	{
		if (!_displayByNormalized.TryGetValue(normalizedName, out var localValue))
		{
			return normalizedName;
		}
		return localValue;
	}
}
