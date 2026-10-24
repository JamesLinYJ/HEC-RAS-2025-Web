using System;
using System.Collections.Generic;
using Utility.Memory;

namespace Ras.Process.H5Commands.Explorer;

public class ExplorerState
{
	public ExplorerMode Mode { get; set; }

	public H5TreeNode Root { get; set; }

	public List<H5TreeNode> VisibleNodes { get; set; } = new List<H5TreeNode>();

	public int CursorIndex { get; set; }

	public int ScrollOffset { get; set; }

	public string CurrentDatasetPath { get; set; }

	public Array CurrentData { get; set; }

	public Table CurrentTable { get; set; }

	public int DataRowOffset { get; set; }

	public int DataColOffset { get; set; }

	public bool IsInteractiveMode { get; set; }

	public int CellCursorRow { get; set; }

	public int CellCursorCol { get; set; }

	public bool ShowColumnStats { get; set; }

	public int SelectedStatsColumn { get; set; } = -1;

	public ColumnStatistics CachedColumnStats { get; set; }

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

	public string ErrorMessage { get; set; }

	public bool ShowingDeletePrompt { get; set; }

	public string DeletePromptMessage { get; set; }

	public bool ShowInfoPanel { get; set; } = true;

	public bool FocusOnInfoPanel { get; set; }

	public int SelectedAttributeIndex { get; set; }

	public NodeInfoData SelectedNodeInfo { get; set; }

	public H5TreeNode SelectedNode
	{
		get
		{
			if (CursorIndex < 0 || CursorIndex >= VisibleNodes.Count)
			{
				return null;
			}
			return VisibleNodes[CursorIndex];
		}
	}

	public void RefreshVisibleNodes()
	{
		if (Root != null)
		{
			VisibleNodes = H5TreeNode.GetVisibleNodes(Root);
		}
	}

	public void MoveCursor(int delta, int visibleHeight)
	{
		CursorIndex = Math.Clamp(CursorIndex + delta, 0, Math.Max(0, VisibleNodes.Count - 1));
		AdjustScroll(visibleHeight);
	}

	private void AdjustScroll(int visibleHeight)
	{
		if (CursorIndex < ScrollOffset)
		{
			ScrollOffset = CursorIndex;
		}
		if (CursorIndex >= ScrollOffset + visibleHeight)
		{
			ScrollOffset = CursorIndex - visibleHeight + 1;
		}
	}

	public void MoveDataView(int rowDelta, int colDelta, int maxRows, int maxCols)
	{
		DataRowOffset = Math.Clamp(DataRowOffset + rowDelta, 0, Math.Max(0, maxRows - 1));
		DataColOffset = Math.Clamp(DataColOffset + colDelta, 0, Math.Max(0, maxCols - 1));
	}

	public void MoveCellCursor(int rowDelta, int colDelta, int maxRows, int maxCols, int visibleRows, int visibleCols)
	{
		CellCursorRow = Math.Clamp(CellCursorRow + rowDelta, -1, Math.Max(0, maxRows - 1));
		CellCursorCol = Math.Clamp(CellCursorCol + colDelta, -1, Math.Max(0, maxCols - 1));
		if (CellCursorRow >= 0)
		{
			if (CellCursorRow < DataRowOffset)
			{
				DataRowOffset = CellCursorRow;
			}
			if (CellCursorRow >= DataRowOffset + visibleRows)
			{
				DataRowOffset = CellCursorRow - visibleRows + 1;
			}
		}
		if (CellCursorCol >= 0)
		{
			if (CellCursorCol < DataColOffset)
			{
				DataColOffset = CellCursorCol;
			}
			if (CellCursorCol >= DataColOffset + visibleCols)
			{
				DataColOffset = CellCursorCol - visibleCols + 1;
			}
		}
	}

	public void ResetInteractiveState()
	{
		IsInteractiveMode = false;
		IsEditingCell = false;
		EditBuffer = "";
		EditError = null;
		PendingChanges.Clear();
		DeletedRows.Clear();
		ShowingSavePrompt = false;
	}
}
