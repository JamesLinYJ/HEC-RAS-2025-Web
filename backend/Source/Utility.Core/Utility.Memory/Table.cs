using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Utility.Extensions;
using Utility.Systems;

namespace Utility.Memory;

public class Table : IStreamableObject
{
	private readonly List<TableRow> _rows;

	private readonly List<TableColumn> _columns;

	private int _columnNamesVersion;

	private int _reverseLookupCachedVersion = -1;

	private Dictionary<string, int> _columnNameReverseLookup;

	public IReadOnlyList<TableRow> Rows => _rows;

	public IReadOnlyList<TableColumn> Columns => _columns;

	public bool InitValueTypesToDefault { get; set; }

	public Action<TableRow> OnNewRow { get; set; }

	public Func<TableRow, bool> IsDefault { get; set; }

	public TableRow this[int index] => _rows[index];

	public Table()
	{
		_rows = new List<TableRow>();
		_columns = new List<TableColumn>();
	}

	public bool TryGetColumnIndex(string colName, out int idx)
	{
		if (_reverseLookupCachedVersion != _columnNamesVersion)
		{
			_columnNameReverseLookup = ComputeNameLookup(_columns);
			_reverseLookupCachedVersion = _columnNamesVersion;
		}
		return _columnNameReverseLookup.TryGetValue(colName, out idx);
	}

	private static Dictionary<string, int> ComputeNameLookup(IReadOnlyList<TableColumn> columns)
	{
		Dictionary<string, int> nameLookup = new Dictionary<string, int>();
		for (int i = 0; i < columns.Count; i++)
		{
			nameLookup[columns[i].Name] = i;
		}
		return nameLookup;
	}

	public Table CloneStructure(bool copyNewRowEvent = true)
	{
		Table table = new Table();
		foreach (TableColumn column in Columns)
		{
			table.AddColumn(column.Clone());
		}
		table.InitValueTypesToDefault = InitValueTypesToDefault;
		if (copyNewRowEvent)
		{
			table.OnNewRow = OnNewRow;
		}
		return table;
	}

	public bool MatchesStructure(Table other)
	{
		if (other.Columns.Count != Columns.Count)
		{
			return false;
		}
		for (int i = 0; i < Columns.Count; i++)
		{
			TableColumn tableColumn = Columns[i];
			TableColumn tableColumn2 = other.Columns[i];
			if (tableColumn.Name != tableColumn2.Name)
			{
				return false;
			}
			if (tableColumn.Type != tableColumn2.Type)
			{
				return false;
			}
		}
		return true;
	}

	public TableColumn GetColumn(string name)
	{
		return Columns.FirstOrDefault((TableColumn c) => c.Name == name);
	}

	public int GetColumnIndex(string columnName)
	{
		return Columns.FirstIndexOf((TableColumn tc) => tc.Name == columnName);
	}

	public int GetColumnIndex(TableColumn column)
	{
		return Columns.IndexOf(column);
	}

	public int AddColumn(string name, Type t)
	{
		return AddColumn(new TableColumn(name, t, isReadOnly: false));
	}

	public int AddColumn(TableColumn column)
	{
		return InsertColumn(_columns.Count, column);
	}

	public int InsertColumn(int insertAt, string name, Type t)
	{
		return InsertColumn(insertAt, new TableColumn(name, t, isReadOnly: false));
	}

	public int InsertColumn(int insertAt, TableColumn column)
	{
		if (insertAt < 0 || insertAt > _columns.Count)
		{
			throw new ArgumentOutOfRangeException("insertAt", "Column index is out of range.");
		}
		_columns.Insert(insertAt, column);
		_columnNamesVersion++;
		for (int i = 0; i < _rows.Count; i++)
		{
			_rows[i].AlertColumnAdded(insertAt);
		}
		return insertAt;
	}

	public bool RemoveColumn(string name)
	{
		int columnIndex = GetColumnIndex(name);
		if (columnIndex == -1)
		{
			return false;
		}
		RemoveColumn(columnIndex);
		return true;
	}

	public void ChangeIntColumnToEnum<T>(string colName) where T : Enum
	{
		ChangeIntColumnToEnum(colName, typeof(T));
	}

	public void ChangeIntColumnToEnum(string colName, Type enumType)
	{
		if (enumType == null || !enumType.IsEnum)
		{
			throw new ArgumentException("Target type must be an enum: " + enumType?.FullName);
		}
		int columnIndex = GetColumnIndex(colName);
		if (columnIndex == -1)
		{
			throw new ArgumentException("Column name does not exist: " + colName);
		}
		TableColumn tableColumn = Columns[columnIndex];
		if (tableColumn.Type != typeof(int))
		{
			throw new Exception("Only intended to convert integer types to Enum");
		}
		tableColumn.DebugSetType(enumType);
		for (int i = 0; i < _rows.Count; i++)
		{
			object row = _rows[i][columnIndex];
			if (row != null)
			{
				int row2 = (int)row;
				_rows[i][columnIndex] = Enum.ToObject(enumType, row2);
			}
		}
	}

	public void ChangeBoolColumnToEnum<T>(string colName, Func<bool?, T> converter) where T : struct, Enum
	{
		int columnIndex = GetColumnIndex(colName);
		if (columnIndex == -1)
		{
			throw new ArgumentException("Column name does not exist: " + colName);
		}
		TableColumn tableColumn = Columns[columnIndex];
		if (tableColumn.Type != typeof(bool) && tableColumn.Type != typeof(bool?))
		{
			throw new Exception("Only intended to convert bool/bool? types to Enum");
		}
		tableColumn.DebugSetType(typeof(T));
		for (int i = 0; i < _rows.Count; i++)
		{
			bool? row = _rows[i][columnIndex] as bool?;
			_rows[i][columnIndex] = converter(row);
		}
	}

	public void ChangeStringColumnToEnum<T>(string colName, Func<string, T> converter) where T : struct, Enum
	{
		int columnIndex = GetColumnIndex(colName);
		if (columnIndex == -1)
		{
			throw new ArgumentException("Column name does not exist: " + colName);
		}
		TableColumn tableColumn = Columns[columnIndex];
		if (tableColumn.Type != typeof(string))
		{
			throw new Exception("Only intended to convert string types to Enum");
		}
		tableColumn.DebugSetType(typeof(T));
		for (int i = 0; i < _rows.Count; i++)
		{
			string row = _rows[i][columnIndex] as string;
			_rows[i][columnIndex] = converter(row);
		}
	}

	public bool TryEnsureColumn(string name, Type t, DuplicateColumnBehavior behavior, out int idx)
	{
		for (int i = 0; i < Columns.Count; i++)
		{
			TableColumn tableColumn = Columns[i];
			if (tableColumn.Name == name)
			{
				if (tableColumn.Type == t)
				{
					idx = i;
					return false;
				}
				switch (behavior)
				{
				case DuplicateColumnBehavior.Throw:
					throw new Exception("Column already exists with different type.");
				case DuplicateColumnBehavior.Skip:
					idx = -1;
					return false;
				case DuplicateColumnBehavior.Replace:
					RemoveColumn(i);
					break;
				default:
					throw new NotImplementedException();
				}
			}
		}
		idx = AddColumn(name, t);
		return true;
	}

	public void Presize(int nRows)
	{
		int count = nRows - _rows.Count;
		for (int i = 0; i < count; i++)
		{
			AddNewRow();
		}
	}

	public void PresizeNull(int nRows)
	{
		int count = nRows - _rows.Count;
		for (int i = 0; i < count; i++)
		{
			_rows.Add(null);
		}
	}

	public TableRow InsertRow(int rowIdx)
	{
		TableRow tableRow = new TableRow(this);
		_rows.Insert(rowIdx, tableRow);
		return tableRow;
	}

	public TableRow RemoveRow(int rowIdx)
	{
		TableRow result = _rows[rowIdx];
		_rows.RemoveAt(rowIdx);
		return result;
	}

	public void RemoveRowRange(int start, int count)
	{
		_rows.RemoveRange(start, count);
	}

	public void ClearRows()
	{
		_rows.Clear();
	}

	public TableColumn RemoveColumn(int colIdx)
	{
		if (colIdx < 0 || colIdx >= _columns.Count)
		{
			throw new ArgumentOutOfRangeException("colIdx", "Column index is out of range.");
		}
		TableColumn result = _columns[colIdx];
		_columns.RemoveAt(colIdx);
		_columnNamesVersion++;
		for (int i = 0; i < _rows.Count; i++)
		{
			_rows[i].AlertColumnRemoved(colIdx);
		}
		return result;
	}

	public bool TryRenameColumn(int colIdx, string newName)
	{
		if (colIdx < 0 || colIdx >= _columns.Count)
		{
			throw new ArgumentOutOfRangeException("colIdx", "Column index is out of range.");
		}
		if (string.IsNullOrWhiteSpace(newName))
		{
			return false;
		}
		if (_columns[colIdx].Name == newName)
		{
			return false;
		}
		if (TryGetColumnIndex(newName, out var _))
		{
			return false;
		}
		_columns[colIdx].Rename(newName);
		_columnNamesVersion++;
		return true;
	}

	public TableRow AddNewRow()
	{
		int fid;
		return AddNewRow(out fid);
	}

	public TableRow AddNewRow(out int fid)
	{
		TableRow tableRow = new TableRow(this);
		_rows.Add(tableRow);
		fid = _rows.Count - 1;
		return tableRow;
	}

	public TableRow AddNewRow(object[] data)
	{
		TableRow tableRow = new TableRow(this, data);
		_rows.Add(tableRow);
		return tableRow;
	}

	public void SetRow(int rowIdx, object[] data)
	{
		TableRow rows = new TableRow(this, data);
		_rows[rowIdx] = rows;
	}

	public object[] CloneRow(int rowIdx)
	{
		return _rows[rowIdx].ItemArray.ToArray();
	}

	public bool TrySetRow(int rowIdx, object[] data)
	{
		if (data == null || data.Length != _columns.Count)
		{
			return false;
		}
		for (int i = 0; i < _columns.Count; i++)
		{
			object localValue = data[i];
			if (localValue != null && !_columns[i].Type.IsInstanceOfType(localValue))
			{
				return false;
			}
		}
		TableRow rows = new TableRow(this, data);
		_rows[rowIdx] = rows;
		return true;
	}

	public void AddIndex(string columName, bool isUnique, bool isImmutable)
	{
		throw new NotImplementedException();
	}

	public void StreamOut(BinaryWriter bw)
	{
		bw.Write(Columns.Count);
		bw.Write(Rows.Count);
		throw new NotImplementedException();
	}

	public void StreamIn(BinaryReader br)
	{
		throw new NotImplementedException();
	}
}
