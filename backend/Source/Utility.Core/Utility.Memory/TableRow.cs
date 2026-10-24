using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;
using Utility.Systems;

namespace Utility.Memory;

[DebuggerDisplay("{DebuggerDisplay()}")]
public class TableRow : IStreamableObject
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TableRow");

	public Table Table { get; }

	public object[] ItemArray { get; private set; }

	public object this[int index]
	{
		get
		{
			return ItemArray[index];
		}
		set
		{
			ItemArray[index] = value;
		}
	}

	public object this[string colName]
	{
		get
		{
			return ItemArray[ColumnIndexOrThrow(colName)];
		}
		set
		{
			ItemArray[ColumnIndexOrThrow(colName)] = value;
		}
	}

	public bool IsDefault
	{
		get
		{
			if (Table != null && Table.IsDefault != null)
			{
				return Table.IsDefault(this);
			}
			return false;
		}
	}

	public TableRow(Table parent)
	{
		Table = parent ?? throw new ArgumentNullException("parent");
		ItemArray = new object[Table.Columns.Count];
		if (parent.InitValueTypesToDefault)
		{
			for (int i = 0; i < Table.Columns.Count; i++)
			{
				TableColumn tableColumn = Table.Columns[i];
				if (tableColumn.Type.IsValueType)
				{
					ItemArray[i] = Activator.CreateInstance(tableColumn.Type);
				}
			}
		}
		if (parent.OnNewRow != null)
		{
			parent.OnNewRow(this);
		}
	}

	public TableRow(Table parent, object[] data)
	{
		Table = parent ?? throw new ArgumentNullException("parent");
		ItemArray = data ?? throw new ArgumentNullException("data");
		if (data.Length != parent.Columns.Count)
		{
			throw new ArgumentException("data incorrect length. Expected: " + parent.Columns.Count + ", Actual: " + data.Length);
		}
	}

	private int ColumnIndexOrThrow(string columnName)
	{
		if (!Table.TryGetColumnIndex(columnName, out var idx))
		{
			throw new Exception("Column not found: " + columnName);
		}
		if (idx >= ItemArray.Length)
		{
			throw new IndexOutOfRangeException($"Column \"{columnName}\" is at index {idx}, but this row only holds {ItemArray.Length} of the table's {Table.Columns.Count} columns.");
		}
		return idx;
	}

	public object Value(string columnName)
	{
		return ItemArray[ColumnIndexOrThrow(columnName)];
	}

	internal void AlertColumnAdded(int newColIdx)
	{
		int itemArrayLength = ItemArray.Length;
		if (newColIdx > itemArrayLength)
		{
			throw new ArgumentOutOfRangeException("Existing array had too few elements to add this column.");
		}
		object[] itemArray = new object[itemArrayLength + 1];
		for (int i = 0; i < itemArray.Length; i++)
		{
			if (i < newColIdx)
			{
				itemArray[i] = ItemArray[i];
			}
			else if (i > newColIdx)
			{
				itemArray[i] = ItemArray[i - 1];
			}
		}
		TableColumn tableColumn = Table.Columns[newColIdx];
		if (Table.InitValueTypesToDefault && tableColumn.Type.IsValueType)
		{
			itemArray[newColIdx] = Activator.CreateInstance(tableColumn.Type);
		}
		ItemArray = itemArray;
	}

	internal void AlertColumnRemoved(int removedColIdx)
	{
		int itemArrayLength = ItemArray.Length;
		if (removedColIdx >= itemArrayLength || removedColIdx < 0)
		{
			throw new ArgumentOutOfRangeException("removedColIdx", "Removed column index is out of range.");
		}
		object[] itemArray = new object[itemArrayLength - 1];
		for (int i = 0; i < itemArrayLength; i++)
		{
			if (i < removedColIdx)
			{
				itemArray[i] = ItemArray[i];
			}
			else if (i > removedColIdx)
			{
				itemArray[i - 1] = ItemArray[i];
			}
		}
		ItemArray = itemArray;
	}

	public T ValueAs<T>(string columnName, T fallbackValue = default(T))
	{
		object columnNameTerm = Value(columnName);
		if (columnNameTerm == null)
		{
			return fallbackValue;
		}
		return (T)columnNameTerm;
	}

	public T ValueAs<T>(int colIdx, T fallbackValue = default(T))
	{
		object itemArray = ItemArray[colIdx];
		if (itemArray == null)
		{
			return fallbackValue;
		}
		return (T)itemArray;
	}

	public bool TryGetEnum<T>(string columnName, out T fallbackValue2, T fallbackValue) where T : struct, Enum
	{
		if (Table.TryGetColumnIndex(columnName, out var idx) && ItemArray[idx] is string textValue && Enum.TryParse<T>(textValue, ignoreCase: true, out fallbackValue2))
		{
			return true;
		}
		fallbackValue2 = fallbackValue;
		return false;
	}

	public void CopyTo(TableRow other, bool assertIdenticalTableStructure = false)
	{
		if ((other.Table == Table) | assertIdenticalTableStructure)
		{
			other.ItemArray = ItemArray.ToArray();
			return;
		}
		foreach (TableColumn column in Table.Columns)
		{
			other.Table.GetColumnIndex(column.Name);
			_ = -1;
		}
		throw new NotImplementedException();
	}

	public void SetValue(string columnName, object assignedValue)
	{
		if (GetEnsureColumn(columnName).IsReadOnly)
		{
			throw new ReadOnlyException("columnName");
		}
		this[columnName] = assignedValue;
	}

	private TableColumn GetEnsureColumn(string columnName)
	{
		TableColumn column = Table.GetColumn(columnName);
		if (column == null)
		{
			throw new Exception("Column " + columnName.InDoubleQuotes() + " not found.");
		}
		return column;
	}

	private string DebuggerDisplay()
	{
		return string.Join(", ", ItemArray);
	}

	public void StreamOut(BinaryWriter bw)
	{
		for (int i = 0; i < ItemArray.Length; i++)
		{
			_ = Table.Columns[i].Type == typeof(string);
		}
		throw new NotImplementedException();
	}

	public void StreamIn(BinaryReader br)
	{
		throw new NotImplementedException();
	}
}
