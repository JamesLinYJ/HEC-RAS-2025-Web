using System;
using System.Collections.Generic;

namespace Ras.Process.RasCommands.Explorer.FeatureTable;

public class FeatureTableState
{
	public IReadOnlyList<object> Features { get; set; }

	public List<ColumnInfo> Columns { get; set; }

	public string LayerName { get; set; }

	public int FeatureCount => Features?.Count ?? 0;

	public int ColumnCount => Columns?.Count ?? 0;

	public int RowScrollOffset { get; set; }

	public int ColScrollOffset { get; set; }

	public bool IsInteractiveMode { get; set; }

	public int CellCursorRow { get; set; }

	public int CellCursorCol { get; set; }

	public bool IsEditingCell { get; set; }

	public string EditBuffer { get; set; } = "";

	public string EditError { get; set; }

	public Dictionary<(int row, int col), object> PendingChanges { get; set; } = new Dictionary<(int, int), object>();

	public HashSet<int> DeletedRows { get; set; } = new HashSet<int>();

	public bool HasUnsavedChanges
	{
		get
		{
			if (PendingChanges.Count <= 0)
			{
				return DeletedRows.Count > 0;
			}
			return true;
		}
	}

	public bool ShowingSavePrompt { get; set; }

	public bool ShowingDeleteConfirm { get; set; }

	public string StatusMessage { get; set; }

	public void MoveScroll(int rowDelta, int colDelta, int visibleRows, int visibleCols)
	{
		RowScrollOffset = Math.Clamp(RowScrollOffset + rowDelta, 0, Math.Max(0, FeatureCount - 1));
		ColScrollOffset = Math.Clamp(ColScrollOffset + colDelta, 0, Math.Max(0, ColumnCount - 1));
	}

	public void MoveCellCursor(int rowDelta, int colDelta, int visibleRows, int visibleCols)
	{
		int max = Math.Max(0, FeatureCount - 1);
		int max2 = Math.Max(0, ColumnCount - 1);
		CellCursorRow = Math.Clamp(CellCursorRow + rowDelta, 0, max);
		CellCursorCol = Math.Clamp(CellCursorCol + colDelta, -1, max2);
		if (CellCursorRow < RowScrollOffset)
		{
			RowScrollOffset = CellCursorRow;
		}
		if (CellCursorRow >= RowScrollOffset + visibleRows)
		{
			RowScrollOffset = CellCursorRow - visibleRows + 1;
		}
		if (CellCursorCol >= 0)
		{
			if (CellCursorCol < ColScrollOffset)
			{
				ColScrollOffset = CellCursorCol;
			}
			if (CellCursorCol >= ColScrollOffset + visibleCols)
			{
				ColScrollOffset = CellCursorCol - visibleCols + 1;
			}
		}
	}

	public object GetCellValue(int row, int col)
	{
		if (PendingChanges.TryGetValue((row, col), out var localValue))
		{
			return localValue;
		}
		if (row < 0 || row >= FeatureCount || col < 0 || col >= ColumnCount)
		{
			return null;
		}
		object feature = Features[row];
		ColumnInfo column = Columns[col];
		return FeatureTableDataAccessor.GetValue(feature, column);
	}

	public bool SetCellValue(int row, int col, object cellValue)
	{
		if (row < 0 || row >= FeatureCount || col < 0 || col >= ColumnCount)
		{
			return false;
		}
		if (Columns[col].IsReadOnly)
		{
			return false;
		}
		PendingChanges[(row, col)] = cellValue;
		return true;
	}

	public void ToggleRowDeletion(int row)
	{
		if (row >= 0 && row < FeatureCount)
		{
			if (DeletedRows.Contains(row))
			{
				DeletedRows.Remove(row);
			}
			else
			{
				DeletedRows.Add(row);
			}
		}
	}

	public void ResetInteractiveState()
	{
		IsInteractiveMode = false;
		IsEditingCell = false;
		EditBuffer = "";
		EditError = null;
		ShowingSavePrompt = false;
		ShowingDeleteConfirm = false;
	}

	public void ClearPendingChanges()
	{
		PendingChanges.Clear();
		DeletedRows.Clear();
		EditError = null;
	}

	public void EnterInteractiveMode()
	{
		IsInteractiveMode = true;
		CellCursorRow = RowScrollOffset;
		CellCursorCol = ColScrollOffset;
	}
}
