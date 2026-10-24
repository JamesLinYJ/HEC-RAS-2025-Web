using System;
using Utility.Memory;

namespace Testing.Core;

public static class InterestPointNaming
{
	private static readonly string[] NameColumnCandidates = new string[7] { "Name", "NAME", "name", "Label", "LABEL", "Title", "Desc" };

	public static bool TryResolveNameColumn(Table attributes, out string columnName)
	{
		columnName = null;
		if (attributes == null)
		{
			return false;
		}
		string[] nameColumnCandidates = NameColumnCandidates;
		foreach (string nameColumnCandidate in nameColumnCandidates)
		{
			if (attributes.TryGetColumnIndex(nameColumnCandidate, out var _))
			{
				columnName = nameColumnCandidate;
				return true;
			}
		}
		foreach (TableColumn column in attributes.Columns)
		{
			if (column.Type == typeof(string))
			{
				columnName = column.Name;
				return true;
			}
		}
		return false;
	}

	public static string ReadName(Table attributes, string columnName, int rowIndex)
	{
		if (attributes == null || string.IsNullOrEmpty(columnName))
		{
			return "";
		}
		if (rowIndex < 0 || rowIndex >= attributes.Rows.Count)
		{
			return "";
		}
		if (!attributes.TryGetColumnIndex(columnName, out var idx))
		{
			return "";
		}
		return attributes.Rows[rowIndex][idx]?.ToString().Trim() ?? "";
	}

	public static (long X, long Y) CoordinateKey(double x, double y)
	{
		return (X: BitConverter.DoubleToInt64Bits(x), Y: BitConverter.DoubleToInt64Bits(y));
	}
}
