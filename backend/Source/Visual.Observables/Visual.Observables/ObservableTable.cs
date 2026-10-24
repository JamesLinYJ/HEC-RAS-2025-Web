using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.Memory;

namespace Visual.Observables;

public class ObservableTable : ObservableObject
{
	public delegate void OnRowColumnAddedEvent(ObservableTable sender, int idx);

	public delegate void OnRowRemovedEvent(ObservableTable sender, int idx);

	public delegate void OnColumnRemovedEvent(ObservableTable sender, int removedIdx, string columnName);

	public delegate void OnColumnRenamedEvent(ObservableTable sender, int colIdx, string oldName, string newName);

	public delegate void OnCellValueChangedEvent(ObservableTable sender, int rowIdx, int colIdx, object localValue);

	private Table _table;

	private bool _needsSave;

	public Table Table => _table;

	public IReadOnlyList<TableColumn> Columns => _table.Columns;

	public IReadOnlyList<TableRow> Rows => _table.Rows;

	public int ColumnCount => Columns.Count;

	public int RowCount => Rows.Count;

	public bool NeedsSave => _needsSave;

	public event OnRowColumnAddedEvent RowAdded;

	public event OnRowColumnAddedEvent ColumnAdded;

	public event OnRowRemovedEvent RowRemoved;

	public event OnColumnRemovedEvent ColumnRemoved;

	public event OnColumnRenamedEvent ColumnRenamed;

	public event OnCellValueChangedEvent CellValueChanged;

	public ObservableTable(Table table)
	{
		_table = table;
	}

	public bool TryGetColumnIndex(string colName, out int idx)
	{
		return _table.TryGetColumnIndex(colName, out idx);
	}

	public bool TryEnsureColumn(string colName, Type colType, out int idx)
	{
		if (string.IsNullOrEmpty(colName))
		{
			throw new ArgumentException("Invalid column name: cannot be null or empty.", "colName");
		}
		bool tryEnsureColumn = _table.TryEnsureColumn(colName, colType, DuplicateColumnBehavior.Throw, out idx);
		if (tryEnsureColumn)
		{
			_needsSave = true;
			ColumnAdded?.Invoke(this, idx);
			OnPropertyChanged("ColumnCount");
		}
		return tryEnsureColumn;
	}

	public void SetCellValue(int rowIdx, int colIdx, object cellValue)
	{
		_needsSave = true;
		_table[rowIdx][colIdx] = cellValue;
		CellValueChanged?.Invoke(this, rowIdx, colIdx, cellValue);
	}

	public void SetCellValue(int rowIdx, string colName, object cellValue)
	{
		if (!_table.TryGetColumnIndex(colName, out var idx))
		{
			throw new ArgumentException("Column " + colName + " doesn't exist in the table.");
		}
		_needsSave = true;
		_table[rowIdx][idx] = cellValue;
		CellValueChanged?.Invoke(this, rowIdx, idx, cellValue);
	}

	public int AppendRow()
	{
		_table.AddNewRow();
		int countOffset = _table.Rows.Count - 1;
		_needsSave = true;
		RowAdded?.Invoke(this, countOffset);
		OnPropertyChanged("RowCount");
		return countOffset;
	}

	public void InsertRow(int idx)
	{
		if (idx < 0 || idx > RowCount)
		{
			throw new ArgumentOutOfRangeException("idx", "Row index is out of range.");
		}
		_table.InsertRow(idx);
		_needsSave = true;
		RowAdded?.Invoke(this, idx);
		OnPropertyChanged("RowCount");
	}

	public void RemoveRow(int idx)
	{
		if (idx < 0 || idx >= RowCount)
		{
			throw new ArgumentOutOfRangeException("idx", "Row index is out of range.");
		}
		_table.RemoveRow(idx);
		_needsSave = true;
		RowRemoved?.Invoke(this, idx);
		OnPropertyChanged("RowCount");
	}

	public void ClearRows()
	{
		if (RowCount != 0)
		{
			_table.ClearRows();
			_needsSave = true;
			RowRemoved?.Invoke(this, -1);
			OnPropertyChanged("RowCount");
		}
	}

	public void RemoveColumn(int colIdx)
	{
		if (colIdx < 0 || colIdx >= ColumnCount)
		{
			throw new ArgumentOutOfRangeException("colIdx", "Column index is out of range.");
		}
		string name = _table.Columns[colIdx].Name;
		_table.RemoveColumn(colIdx);
		_needsSave = true;
		ColumnRemoved?.Invoke(this, colIdx, name);
		OnPropertyChanged("ColumnCount");
	}

	public bool TryRenameColumn(int colIdx, string newName)
	{
		if (colIdx < 0 || colIdx >= ColumnCount)
		{
			throw new ArgumentOutOfRangeException("colIdx", "Column index is out of range.");
		}
		string name = _table.Columns[colIdx].Name;
		if (!_table.TryRenameColumn(colIdx, newName))
		{
			return false;
		}
		_needsSave = true;
		ColumnRenamed?.Invoke(this, colIdx, name, newName);
		return true;
	}
}
