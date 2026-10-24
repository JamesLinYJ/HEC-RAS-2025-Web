using System.Collections.Generic;
using Utility.Filtering;
using Utility.Memory;

namespace Ras.Process.GeometryCommands;

internal sealed class TopologyElement : ISelectorElement, IFilterRow
{
	private readonly TopologyFieldSchema _schema;

	private readonly Dictionary<string, object> _derived;

	private readonly TableRow _readRow;

	private readonly TableRow _writeRow;

	private readonly string _writeUnavailableReason;

	public TopologyElement(TopologyFieldSchema schema, Dictionary<string, object> derived, TableRow readRow, TableRow writeRow, string writeUnavailableReason)
	{
		_schema = schema;
		_derived = derived;
		_readRow = readRow;
		_writeRow = writeRow;
		_writeUnavailableReason = writeUnavailableReason;
	}

	public bool TryGetValue(string normalizedName, out object localValue2)
	{
		if (_derived.TryGetValue(normalizedName, out localValue2))
		{
			return true;
		}
		if (_schema.MetaCanonical.TryGetValue(normalizedName, out var localValue))
		{
			if (_readRow != null && _readRow.Table.TryGetColumnIndex(localValue, out var idx))
			{
				localValue2 = _readRow[idx];
			}
			else
			{
				localValue2 = null;
			}
			return true;
		}
		localValue2 = null;
		return false;
	}

	public bool TrySetValue(string normalizedName, object writeRow, out string error)
	{
		error = null;
		if (_schema.DerivedNormalized.Contains(normalizedName))
		{
			error = "'" + _schema.DisplayOf(normalizedName) + "' is read-only.";
			return false;
		}
		if (!_schema.MetaCanonical.TryGetValue(normalizedName, out var localValue))
		{
			error = "Unknown field '" + normalizedName + "'.";
			return false;
		}
		if (_writeRow == null)
		{
			error = _writeUnavailableReason ?? "field can't be set on this element.";
			return false;
		}
		if (!_writeRow.Table.TryGetColumnIndex(localValue, out var idx))
		{
			error = "Column '" + localValue + "' is missing.";
			return false;
		}
		_writeRow[idx] = writeRow;
		return true;
	}
}
