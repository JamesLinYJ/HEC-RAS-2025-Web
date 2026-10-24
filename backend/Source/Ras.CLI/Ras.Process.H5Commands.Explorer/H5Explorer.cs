using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using H5Assist;
using H5Assist.Identifiers;
using Spectre.Console;
using Spectre.Console.Rendering;
using Utility.Memory;
using Ras.Process.Platform;

namespace Ras.Process.H5Commands.Explorer;

public class H5Explorer
{
	private H5Reader _reader;

	private readonly string _filename;

	private readonly ExplorerState _state = new ExplorerState();

	private readonly TreeRenderer _treeRenderer = new TreeRenderer();

	private readonly DataViewRenderer _dataRenderer = new DataViewRenderer();

	private string _lastSelectedPath;

	public H5Explorer(string filename)
	{
		_filename = filename;
		_reader = new H5Reader(filename);
	}

	public bool Run()
	{
		Console.CursorVisible = false;
		try
		{
			InitializeTree();
			AnsiConsole.Live(Render()).AutoClear(enabled: false).Start((LiveDisplayContext ctx) =>
			{
				ctx.Refresh();
				while (true)
				{
					ConsoleKeyInfo key = Console.ReadKey(intercept: true);
					if (!ProcessInput(key))
					{
						break;
					}
					ctx.UpdateTarget(Render());
					ctx.Refresh();
				}
			});
			return true;
		}
		finally
		{
			_reader?.Dispose();
			Console.CursorVisible = true;
			AnsiConsole.Clear();
		}
	}

	private void InitializeTree()
	{
		_state.Root = new H5TreeNode("/", "/", NodeType.Group);
		_state.Root.IsExpanded = true;
		LoadChildren(_state.Root);
		_state.RefreshVisibleNodes();
		UpdateSelectedNodeInfo();
	}

	private void UpdateSelectedNodeInfo()
	{
		H5TreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null)
		{
			_state.SelectedNodeInfo = null;
			_state.FocusOnInfoPanel = false;
			_lastSelectedPath = null;
			return;
		}
		if (_lastSelectedPath != selectedNode.FullPath)
		{
			_state.SelectedAttributeIndex = 0;
			_state.FocusOnInfoPanel = false;
			_lastSelectedPath = selectedNode.FullPath;
		}
		try
		{
			NodeInfoData nodeInfoData = new NodeInfoData();
			nodeInfoData.Attributes = _reader.ReadAllAttributes(selectedNode.FullPath);
			if (selectedNode.Type == NodeType.Dataset)
			{
				long[] datasetDimensions = _reader.GetDatasetDimensions(selectedNode.FullPath);
				long[] chunkSize = _reader.GetChunkSize(selectedNode.FullPath);
				Type datasetType = _reader.GetDatasetType(selectedNode.FullPath);
				long uncompressedSize = 0L;
				if (datasetDimensions != null && datasetDimensions.Length != 0 && datasetType != null)
				{
					long aggregate = datasetDimensions.Aggregate(1L, (long a, long b) => a * b);
					int typeSize = GetTypeSize(datasetType);
					uncompressedSize = aggregate * typeSize;
				}
				long compressedSize = 0L;
				try
				{
					if (chunkSize != null && chunkSize.Length != 0)
					{
						compressedSize = _reader.CompressedSize(selectedNode.FullPath);
					}
				}
				catch
				{
				}
				nodeInfoData.DatasetInfo = new DatasetProperties
				{
					Dimensions = datasetDimensions,
					ChunkDimensions = chunkSize,
					ElementType = datasetType,
					CompressedSize = compressedSize,
					UncompressedSize = uncompressedSize,
					IsVariableLength = _reader.DatasetIsVariableLength(selectedNode.FullPath)
				};
			}
			_state.SelectedNodeInfo = nodeInfoData;
		}
		catch
		{
			_state.SelectedNodeInfo = null;
		}
	}

	private static int GetTypeSize(Type t)
	{
		if (t == typeof(float))
		{
			return 4;
		}
		if (t == typeof(double))
		{
			return 8;
		}
		if (t == typeof(int))
		{
			return 4;
		}
		if (t == typeof(uint))
		{
			return 4;
		}
		if (t == typeof(long))
		{
			return 8;
		}
		if (t == typeof(ulong))
		{
			return 8;
		}
		if (t == typeof(short))
		{
			return 2;
		}
		if (t == typeof(ushort))
		{
			return 2;
		}
		if (t == typeof(byte))
		{
			return 1;
		}
		if (t == typeof(sbyte))
		{
			return 1;
		}
		if (t == typeof(bool))
		{
			return 1;
		}
		return 4;
	}

	private void LoadChildren(H5TreeNode node)
	{
		if (node.ChildrenLoaded)
		{
			return;
		}
		foreach (string groupName in _reader.GetGroupNames(node.FullPath))
		{
			string groupIds = ((node.FullPath == "/") ? ("/" + groupName) : (node.FullPath + "/" + groupName));
			H5TreeNode h5TreeNode = new H5TreeNode(groupName, groupIds, NodeType.Group, node);
			h5TreeNode.AttributeCount = _reader.GetAttributeNames(groupIds).Count;
			node.Children.Add(h5TreeNode);
		}
		foreach (string datasetName in _reader.GetDatasetNames(node.FullPath))
		{
			string datasetIds = ((node.FullPath == "/") ? ("/" + datasetName) : (node.FullPath + "/" + datasetName));
			H5TreeNode h5TreeNode2 = new H5TreeNode(datasetName, datasetIds, NodeType.Dataset, node);
			h5TreeNode2.AttributeCount = _reader.GetAttributeNames(datasetIds).Count;
			h5TreeNode2.Dimensions = _reader.GetDatasetDimensions(datasetIds);
			h5TreeNode2.DataType = _reader.GetDatasetType(datasetIds);
			node.Children.Add(h5TreeNode2);
		}
		node.ChildrenLoaded = true;
	}

	private IRenderable Render()
	{
		int windowHeight = Console.WindowHeight;
		int windowWidth = Console.WindowWidth;
		return _state.Mode switch
		{
			ExplorerMode.Tree => _treeRenderer.Render(_state, _filename, windowWidth, windowHeight), 
			ExplorerMode.DataView => _dataRenderer.Render(_state, windowWidth, windowHeight), 
			_ => _treeRenderer.Render(_state, _filename, windowWidth, windowHeight), 
		};
	}

	private bool ProcessInput(ConsoleKeyInfo key)
	{
		return _state.Mode switch
		{
			ExplorerMode.Tree => ProcessTreeInput(key), 
			ExplorerMode.DataView => ProcessDataViewInput(key), 
			_ => true, 
		};
	}

	private bool ProcessTreeInput(ConsoleKeyInfo key)
	{
		if (_state.ShowingDeletePrompt)
		{
			return ProcessDeletePromptInput(key);
		}
		if (_state.FocusOnInfoPanel)
		{
			return ProcessInfoPanelInput(key);
		}
		int windowHeightOffset = Console.WindowHeight - 4;
		switch (key.Key)
		{
		case ConsoleKey.UpArrow:
			_state.MoveCursor(-1, windowHeightOffset);
			break;
		case ConsoleKey.DownArrow:
			_state.MoveCursor(1, windowHeightOffset);
			break;
		case ConsoleKey.PageUp:
			_state.MoveCursor(-windowHeightOffset, windowHeightOffset);
			break;
		case ConsoleKey.PageDown:
			_state.MoveCursor(windowHeightOffset, windowHeightOffset);
			break;
		case ConsoleKey.Home:
			_state.CursorIndex = 0;
			_state.ScrollOffset = 0;
			break;
		case ConsoleKey.End:
			_state.CursorIndex = _state.VisibleNodes.Count - 1;
			_state.MoveCursor(0, windowHeightOffset);
			break;
		case ConsoleKey.RightArrow:
			if (!TryExpandOrOpen() && _state.ShowInfoPanel)
			{
				_state.FocusOnInfoPanel = true;
				_state.SelectedAttributeIndex = 0;
			}
			break;
		case ConsoleKey.Enter:
			TryExpandOrOpen();
			break;
		case ConsoleKey.LeftArrow:
			CollapseOrGoToParent();
			break;
		case ConsoleKey.A:
			_state.ShowInfoPanel = !_state.ShowInfoPanel;
			break;
		case ConsoleKey.C:
			if (_state.SelectedNode != null)
			{
				CopyToClipboard(_state.SelectedNode.FullPath);
			}
			break;
		case ConsoleKey.D:
			PromptDeleteNode();
			break;
		case ConsoleKey.Escape:
		case ConsoleKey.Q:
			return false;
		}
		UpdateSelectedNodeInfo();
		return true;
	}

	private bool ProcessDeletePromptInput(ConsoleKeyInfo key)
	{
		switch (char.ToUpper(key.KeyChar))
		{
		case 'Y':
			PerformNodeDeletion();
			_state.ShowingDeletePrompt = false;
			_state.DeletePromptMessage = null;
			UpdateSelectedNodeInfo();
			break;
		case '\u001b':
		case 'N':
			_state.ShowingDeletePrompt = false;
			_state.DeletePromptMessage = null;
			break;
		}
		return true;
	}

	private bool ProcessInfoPanelInput(ConsoleKeyInfo key)
	{
		int valueOrDefault = (_state.SelectedNodeInfo?.Attributes?.Count).GetValueOrDefault();
		switch (key.Key)
		{
		case ConsoleKey.UpArrow:
			if (_state.SelectedAttributeIndex > 0)
			{
				_state.SelectedAttributeIndex--;
			}
			break;
		case ConsoleKey.DownArrow:
			if (_state.SelectedAttributeIndex < valueOrDefault - 1)
			{
				_state.SelectedAttributeIndex++;
			}
			break;
		case ConsoleKey.Escape:
		case ConsoleKey.LeftArrow:
			_state.FocusOnInfoPanel = false;
			break;
		case ConsoleKey.A:
			_state.ShowInfoPanel = !_state.ShowInfoPanel;
			_state.FocusOnInfoPanel = false;
			break;
		case ConsoleKey.C:
			CopySelectedAttribute();
			break;
		case ConsoleKey.Q:
			return false;
		}
		return true;
	}

	private void CopySelectedAttribute()
	{
		NodeInfoData selectedNodeInfo = _state.SelectedNodeInfo;
		if (selectedNodeInfo?.Attributes != null && selectedNodeInfo.Attributes.Count != 0)
		{
			List<KeyValuePair<string, object>> attributes = selectedNodeInfo.Attributes.ToList();
			if (_state.SelectedAttributeIndex >= 0 && _state.SelectedAttributeIndex < attributes.Count)
			{
				KeyValuePair<string, object> attribute = attributes[_state.SelectedAttributeIndex];
				CopyToClipboard(attribute.Key + ": " + FormatValueForCopy(attribute.Value));
			}
		}
	}

	private static string FormatValueForCopy(object localValue)
	{
		if (localValue == null)
		{
			return "null";
		}
		if (localValue is Array values)
		{
			List<string> stringList = new List<string>();
			foreach (object localValue2 in values)
			{
				stringList.Add(localValue2?.ToString() ?? "null");
			}
			return "[" + string.Join(", ", stringList) + "]";
		}
		return localValue.ToString() ?? "";
	}

	private static void CopyToClipboard(string textValue)
	{
		if (!DesktopClipboard.TryCopy(textValue)) Console.Error.WriteLine("Clipboard is unavailable.");
	}

	private void PromptDeleteNode()
	{
		H5TreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null || selectedNode.FullPath == "/")
		{
			_state.ErrorMessage = "Cannot delete root group";
			return;
		}
		_state.ErrorMessage = null;
		if (selectedNode.Type == NodeType.Dataset)
		{
			_state.DeletePromptMessage = "Delete dataset '" + selectedNode.Name + "'?";
		}
		else
		{
			var (localValue, localValue2) = CountGroupContents(selectedNode.FullPath);
			if (localValue == 0 && localValue2 == 0)
			{
				_state.DeletePromptMessage = "Delete empty group '" + selectedNode.Name + "'?";
			}
			else
			{
				_state.DeletePromptMessage = $"Delete group '{selectedNode.Name}'? Contains {localValue2} dataset(s) and {localValue} subgroup(s).";
			}
		}
		_state.ShowingDeletePrompt = true;
	}

	private (int groups, int datasets) CountGroupContents(string path)
	{
		int ordinal = 0;
		int ordinal2 = 0;
		List<string> groupNames = _reader.GetGroupNames(path);
		List<string> datasetNames = _reader.GetDatasetNames(path);
		ordinal += groupNames.Count;
		ordinal2 += datasetNames.Count;
		foreach (string groupName in groupNames)
		{
			string path2 = ((path == "/") ? ("/" + groupName) : (path + "/" + groupName));
			(int groups, int datasets) tuple = CountGroupContents(path2);
			int groups2 = tuple.groups;
			int datasets2 = tuple.datasets;
			ordinal += groups2;
			ordinal2 += datasets2;
		}
		return (groups: ordinal, datasets: ordinal2);
	}

	private void PerformNodeDeletion()
	{
		H5TreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null)
		{
			return;
		}
		try
		{
			_reader.Dispose();
			_reader = null;
			using (H5Writer h5Writer = new H5Writer(_filename))
			{
				h5Writer.Delete(selectedNode.FullPath);
			}
			if (selectedNode.Parent != null)
			{
				selectedNode.Parent.Children.Remove(selectedNode);
				_state.RefreshVisibleNodes();
				if (_state.CursorIndex >= _state.VisibleNodes.Count)
				{
					_state.CursorIndex = Math.Max(0, _state.VisibleNodes.Count - 1);
				}
			}
		}
		catch (Exception ex)
		{
			_state.ErrorMessage = "Delete failed: " + ex.Message;
		}
		finally
		{
			_reader = new H5Reader(_filename);
		}
	}

	private bool TryExpandOrOpen()
	{
		H5TreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null)
		{
			return false;
		}
		if (selectedNode.Type == NodeType.Group)
		{
			if (!selectedNode.IsExpanded)
			{
				LoadChildren(selectedNode);
				selectedNode.IsExpanded = true;
				_state.RefreshVisibleNodes();
				return true;
			}
			return false;
		}
		if (selectedNode.Type == NodeType.Dataset)
		{
			OpenDataView(selectedNode);
			return true;
		}
		return false;
	}

	private void CollapseOrGoToParent()
	{
		H5TreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null)
		{
			return;
		}
		if (selectedNode.Type == NodeType.Group && selectedNode.IsExpanded)
		{
			selectedNode.IsExpanded = false;
			_state.RefreshVisibleNodes();
		}
		else if (selectedNode.Parent != null)
		{
			int cursorIndex = _state.VisibleNodes.IndexOf(selectedNode.Parent);
			if (cursorIndex >= 0)
			{
				_state.CursorIndex = cursorIndex;
				_state.MoveCursor(0, Console.WindowHeight - 4);
			}
		}
	}

	private void OpenDataView(H5TreeNode node)
	{
		if (node.Dimensions == null || node.Dimensions.Length == 0 || node.Dimensions.Length > 2)
		{
			return;
		}
		_state.CurrentDatasetPath = node.FullPath;
		_state.DataRowOffset = 0;
		_state.DataColOffset = 0;
		_state.ErrorMessage = null;
		_state.CurrentData = null;
		_state.CurrentTable = null;
		Type dataType = node.DataType;
		try
		{
			if (dataType == typeof(CompoundFieldDefinition))
			{
				_state.CurrentTable = _reader.ReadCompoundElementsAsTable(node.FullPath);
				_state.Mode = ExplorerMode.DataView;
			}
			else if (IsSupportedType(dataType))
			{
				if (node.Dimensions.Length == 1)
				{
					LoadData1D(node.FullPath, dataType);
				}
				else
				{
					LoadData2D(node.FullPath, dataType);
				}
				if (_state.CurrentData != null)
				{
					_state.Mode = ExplorerMode.DataView;
				}
			}
			else
			{
				_state.ErrorMessage = "Unsupported dataset type: " + dataType.Name;
			}
		}
		catch (Exception ex)
		{
			_state.ErrorMessage = "Error loading dataset: " + ex.Message;
		}
	}

	private static bool IsSupportedType(Type t)
	{
		if (!(t == typeof(float)) && !(t == typeof(double)) && !(t == typeof(int)) && !(t == typeof(uint)) && !(t == typeof(long)) && !(t == typeof(ulong)) && !(t == typeof(short)) && !(t == typeof(ushort)) && !(t == typeof(byte)))
		{
			return t == typeof(sbyte);
		}
		return true;
	}

	private void LoadData1D(string path, Type dataType)
	{
		if (dataType == typeof(float))
		{
			_reader.ReadDataset(path, out float[] values);
			_state.CurrentData = values;
		}
		else if (dataType == typeof(double))
		{
			_reader.ReadDataset(path, out double[] values2);
			_state.CurrentData = values2;
		}
		else if (dataType == typeof(int))
		{
			_reader.ReadDataset(path, out int[] values3);
			_state.CurrentData = values3;
		}
		else if (dataType == typeof(long))
		{
			_reader.ReadDataset(path, out long[] values4);
			_state.CurrentData = values4;
		}
		else if (dataType == typeof(short))
		{
			_reader.ReadDataset(path, out short[] values5);
			_state.CurrentData = values5;
		}
		else if (dataType == typeof(byte))
		{
			_reader.ReadDataset(path, out byte[] values6);
			_state.CurrentData = values6;
		}
		else if (dataType == typeof(uint))
		{
			_reader.ReadDataset(path, out uint[] values7);
			_state.CurrentData = values7;
		}
		else if (dataType == typeof(ulong))
		{
			_reader.ReadDataset(path, out ulong[] values8);
			_state.CurrentData = values8;
		}
		else if (dataType == typeof(sbyte))
		{
			_reader.ReadDataset(path, out sbyte[] values9);
			_state.CurrentData = values9;
		}
		else if (dataType == typeof(ushort))
		{
			_reader.ReadDataset(path, out ushort[] values10);
			_state.CurrentData = values10;
		}
	}

	private void LoadData2D(string path, Type dataType)
	{
		if (dataType == typeof(float))
		{
			_reader.ReadDataset(path, out float[,] values);
			_state.CurrentData = values;
		}
		else if (dataType == typeof(double))
		{
			_reader.ReadDataset(path, out double[,] values2);
			_state.CurrentData = values2;
		}
		else if (dataType == typeof(int))
		{
			_reader.ReadDataset(path, out int[,] values3);
			_state.CurrentData = values3;
		}
		else if (dataType == typeof(long))
		{
			_reader.ReadDataset(path, out long[,] values4);
			_state.CurrentData = values4;
		}
		else if (dataType == typeof(short))
		{
			_reader.ReadDataset(path, out short[,] values5);
			_state.CurrentData = values5;
		}
		else if (dataType == typeof(byte))
		{
			_reader.ReadDataset(path, out byte[,] values6);
			_state.CurrentData = values6;
		}
		else if (dataType == typeof(uint))
		{
			_reader.ReadDataset(path, out uint[,] values7);
			_state.CurrentData = values7;
		}
		else if (dataType == typeof(ulong))
		{
			_reader.ReadDataset(path, out ulong[,] values8);
			_state.CurrentData = values8;
		}
		else if (dataType == typeof(sbyte))
		{
			_reader.ReadDataset(path, out sbyte[,] values9);
			_state.CurrentData = values9;
		}
		else if (dataType == typeof(ushort))
		{
			_reader.ReadDataset(path, out ushort[,] values10);
			_state.CurrentData = values10;
		}
	}

	private bool ProcessDataViewInput(ConsoleKeyInfo key)
	{
		if (_state.CurrentData == null && _state.CurrentTable == null)
		{
			_state.Mode = ExplorerMode.Tree;
			return true;
		}
		if (_state.ShowingSavePrompt)
		{
			return ProcessSavePromptInput(key);
		}
		if (_state.IsEditingCell)
		{
			return ProcessCellEditInput(key);
		}
		if (_state.IsInteractiveMode)
		{
			return ProcessInteractiveInput(key);
		}
		return ProcessScrollInput(key);
	}

	private bool ProcessScrollInput(ConsoleKeyInfo key)
	{
		int windowHeightOffset = Console.WindowHeight - 8;
		int rowsCurrentTableTerm;
		int maxCols;
		if (_state.CurrentTable != null)
		{
			rowsCurrentTableTerm = _state.CurrentTable.Rows.Count;
			maxCols = _state.CurrentTable.Columns.Count;
		}
		else
		{
			rowsCurrentTableTerm = _state.CurrentData.GetLength(0);
			maxCols = ((_state.CurrentData.Rank <= 1) ? 1 : _state.CurrentData.GetLength(1));
		}
		switch (key.Key)
		{
		case ConsoleKey.Escape:
			_state.Mode = ExplorerMode.Tree;
			_state.CurrentData = null;
			_state.CurrentTable = null;
			_state.ResetInteractiveState();
			break;
		case ConsoleKey.UpArrow:
			_state.MoveDataView(-1, 0, rowsCurrentTableTerm, maxCols);
			break;
		case ConsoleKey.DownArrow:
			_state.MoveDataView(1, 0, rowsCurrentTableTerm, maxCols);
			break;
		case ConsoleKey.LeftArrow:
			_state.MoveDataView(0, -1, rowsCurrentTableTerm, maxCols);
			break;
		case ConsoleKey.RightArrow:
			_state.MoveDataView(0, 1, rowsCurrentTableTerm, maxCols);
			break;
		case ConsoleKey.PageUp:
			_state.MoveDataView(-windowHeightOffset, 0, rowsCurrentTableTerm, maxCols);
			break;
		case ConsoleKey.PageDown:
			_state.MoveDataView(windowHeightOffset, 0, rowsCurrentTableTerm, maxCols);
			break;
		case ConsoleKey.Home:
			_state.DataRowOffset = 0;
			_state.DataColOffset = 0;
			break;
		case ConsoleKey.End:
			_state.DataRowOffset = rowsCurrentTableTerm - 1;
			break;
		case ConsoleKey.I:
			_state.IsInteractiveMode = true;
			_state.CellCursorRow = _state.DataRowOffset;
			_state.CellCursorCol = _state.DataColOffset;
			break;
		case ConsoleKey.C:
			CopyEntireDatasetAsCsv(rowsCurrentTableTerm, maxCols);
			break;
		}
		return true;
	}

	private void CopyEntireDatasetAsCsv(int maxRows, int maxCols)
	{
		long maxRowsMaxColsTerm = (long)maxRows * (long)maxCols * 10;
		if (maxRowsMaxColsTerm > 102400)
		{
			_state.ErrorMessage = $"Dataset too large to copy (~{maxRowsMaxColsTerm / 1024}KB, limit 100KB)";
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		if (_state.CurrentTable != null)
		{
			List<string> stringList = new List<string>();
			for (int i = 0; i < _state.CurrentTable.Columns.Count; i++)
			{
				stringList.Add(_state.CurrentTable.Columns[i].Name);
			}
			stringBuilder.AppendLine(string.Join(",", stringList));
			for (int j = 0; j < maxRows; j++)
			{
				Utility.Memory.TableRow tableRow = _state.CurrentTable.Rows[j];
				List<string> stringListList = new List<string>();
				for (int k = 0; k < maxCols; k++)
				{
					stringListList.Add(FormatCsvValue(tableRow[k]));
				}
				stringBuilder.AppendLine(string.Join(",", stringListList));
			}
		}
		else if (_state.CurrentData != null)
		{
			if (_state.CurrentData.Rank == 1)
			{
				for (int l = 0; l < maxRows; l++)
				{
					stringBuilder.AppendLine(FormatCsvValue(_state.CurrentData.GetValue(l)));
				}
			}
			else
			{
				for (int m = 0; m < maxRows; m++)
				{
					List<string> stringList2 = new List<string>();
					for (int n = 0; n < maxCols; n++)
					{
						stringList2.Add(FormatCsvValue(_state.CurrentData.GetValue(m, n)));
					}
					stringBuilder.AppendLine(string.Join(",", stringList2));
				}
			}
		}
		string stringBuilder2 = stringBuilder.ToString();
		if (stringBuilder2.Length > 102400)
		{
			_state.ErrorMessage = $"Dataset too large to copy ({stringBuilder2.Length / 1024}KB, limit 100KB)";
		}
		else
		{
			CopyToClipboard(stringBuilder2);
			_state.ErrorMessage = null;
		}
	}

	private static string FormatCsvValue(object localValue)
	{
		if (localValue == null)
		{
			return "";
		}
		string textValue = localValue.ToString();
		if (textValue.Contains(',') || textValue.Contains('"') || textValue.Contains('\n'))
		{
			return "\"" + textValue.Replace("\"", "\"\"") + "\"";
		}
		return textValue;
	}

	private bool ProcessInteractiveInput(ConsoleKeyInfo key)
	{
		int maxRows;
		int maxCols;
		if (_state.CurrentTable != null)
		{
			maxRows = _state.CurrentTable.Rows.Count;
			maxCols = _state.CurrentTable.Columns.Count;
		}
		else
		{
			maxRows = _state.CurrentData.GetLength(0);
			maxCols = ((_state.CurrentData.Rank <= 1) ? 1 : _state.CurrentData.GetLength(1));
		}
		int visibleRows = Console.WindowHeight - 9;
		int visibleCols = CalculateVisibleCols();
		switch (key.Key)
		{
		case ConsoleKey.UpArrow:
			_state.MoveCellCursor(-1, 0, maxRows, maxCols, visibleRows, visibleCols);
			if (_state.CellCursorRow == -1 && _state.CellCursorCol >= 0)
			{
				CalculateColumnStatistics(_state.CellCursorCol);
				_state.ShowColumnStats = true;
			}
			break;
		case ConsoleKey.DownArrow:
			_state.MoveCellCursor(1, 0, maxRows, maxCols, visibleRows, visibleCols);
			break;
		case ConsoleKey.LeftArrow:
			_state.MoveCellCursor(0, -1, maxRows, maxCols, visibleRows, visibleCols);
			if (_state.CellCursorRow == -1 && _state.CellCursorCol >= 0)
			{
				CalculateColumnStatistics(_state.CellCursorCol);
			}
			break;
		case ConsoleKey.RightArrow:
			_state.MoveCellCursor(0, 1, maxRows, maxCols, visibleRows, visibleCols);
			if (_state.CellCursorRow == -1 && _state.CellCursorCol >= 0)
			{
				CalculateColumnStatistics(_state.CellCursorCol);
			}
			break;
		case ConsoleKey.Enter:
			if (_state.CellCursorCol >= 0 && _state.CellCursorRow >= 0)
			{
				StartCellEdit();
			}
			break;
		case ConsoleKey.Escape:
			ExitInteractiveMode();
			break;
		case ConsoleKey.W:
			if ((key.Modifiers & ConsoleModifiers.Control) != 0)
			{
				SaveChanges();
			}
			break;
		case ConsoleKey.S:
			if (_state.CellCursorRow == -1 && _state.CellCursorCol >= 0)
			{
				_state.ShowColumnStats = !_state.ShowColumnStats;
			}
			break;
		case ConsoleKey.D:
			if (_state.CellCursorCol == -1 && _state.CellCursorRow >= 0)
			{
				ToggleRowDeletion(_state.CellCursorRow);
			}
			break;
		case ConsoleKey.C:
			CopySelectedCell();
			break;
		}
		return true;
	}

	private void CalculateColumnStatistics(int columnIndex)
	{
		if (columnIndex < 0)
		{
			_state.CachedColumnStats = null;
			_state.SelectedStatsColumn = -1;
		}
		else if (_state.SelectedStatsColumn != columnIndex || _state.CachedColumnStats == null || _state.HasUnsavedChanges)
		{
			_state.SelectedStatsColumn = columnIndex;
			_state.CachedColumnStats = ColumnStatistics.Calculate(_state, columnIndex);
		}
	}

	private void CopySelectedCell()
	{
		int cellCursorRow = _state.CellCursorRow;
		int cellCursorCol = _state.CellCursorCol;
		if (cellCursorCol == -1)
		{
			if (_state.CurrentTable != null)
			{
				Utility.Memory.TableRow tableRow = _state.CurrentTable.Rows[cellCursorRow];
				List<string> stringList = new List<string>();
				for (int i = 0; i < _state.CurrentTable.Columns.Count; i++)
				{
					Utility.Memory.TableColumn tableColumn = _state.CurrentTable.Columns[i];
					stringList.Add($"{tableColumn.Name}: {tableRow[tableColumn.Name]}");
				}
				CopyToClipboard(string.Join(", ", stringList));
			}
			else
			{
				if (_state.CurrentData == null)
				{
					return;
				}
				List<string> stringListList = new List<string>();
				if (_state.CurrentData.Rank == 1)
				{
					stringListList.Add(_state.CurrentData.GetValue(cellCursorRow)?.ToString() ?? "");
				}
				else
				{
					int length = _state.CurrentData.GetLength(1);
					for (int j = 0; j < length; j++)
					{
						stringListList.Add(_state.CurrentData.GetValue(cellCursorRow, j)?.ToString() ?? "");
					}
				}
				CopyToClipboard(string.Join(", ", stringListList));
			}
			return;
		}
		object rowsCurrentTableTerm;
		if (_state.CurrentTable != null)
		{
			Utility.Memory.TableColumn tableColumn2 = _state.CurrentTable.Columns[cellCursorCol];
			rowsCurrentTableTerm = _state.CurrentTable.Rows[cellCursorRow][tableColumn2.Name];
		}
		else
		{
			if (_state.CurrentData == null)
			{
				return;
			}
			rowsCurrentTableTerm = ((_state.CurrentData.Rank != 1) ? _state.CurrentData.GetValue(cellCursorRow, cellCursorCol) : _state.CurrentData.GetValue(cellCursorRow));
		}
		CopyToClipboard(rowsCurrentTableTerm?.ToString() ?? "");
	}

	private void ToggleRowDeletion(int row)
	{
		if (_state.DeletedRows.Contains(row))
		{
			_state.DeletedRows.Remove(row);
		}
		else
		{
			_state.DeletedRows.Add(row);
		}
		_state.CachedColumnStats = null;
	}

	private bool ProcessCellEditInput(ConsoleKeyInfo key)
	{
		switch (key.Key)
		{
		case ConsoleKey.Enter:
			CommitCellEdit();
			break;
		case ConsoleKey.Escape:
			CancelCellEdit();
			break;
		case ConsoleKey.Backspace:
			if (_state.EditBuffer.Length > 0)
			{
				ExplorerState state = _state;
				string editBuffer = _state.EditBuffer;
				state.EditBuffer = editBuffer.Substring(0, editBuffer.Length - 1);
			}
			_state.EditError = null;
			break;
		default:
			if (!char.IsControl(key.KeyChar))
			{
				_state.EditBuffer += key.KeyChar;
				_state.EditError = null;
			}
			break;
		}
		return true;
	}

	private bool ProcessSavePromptInput(ConsoleKeyInfo key)
	{
		switch (char.ToUpper(key.KeyChar))
		{
		case 'S':
			SaveChanges();
			_state.ShowingSavePrompt = false;
			_state.IsInteractiveMode = false;
			break;
		case 'D':
			DiscardChanges();
			_state.ShowingSavePrompt = false;
			_state.IsInteractiveMode = false;
			break;
		case 'C':
			_state.ShowingSavePrompt = false;
			break;
		}
		return true;
	}

	private int CalculateVisibleCols()
	{
		int currentTableStateTerm = ((_state.CurrentTable != null) ? 14 : 12);
		int ordinal = 8;
		int windowWidthConsoleTerm = Console.WindowWidth - ordinal - 4;
		return Math.Max(1, windowWidthConsoleTerm / (currentTableStateTerm + 3));
	}

	private void StartCellEdit()
	{
		object cellValue = GetCellValue(_state.CellCursorRow, _state.CellCursorCol);
		_state.EditBuffer = cellValue?.ToString() ?? "";
		_state.IsEditingCell = true;
		_state.EditError = null;
	}

	private object GetCellValue(int row, int col)
	{
		if (_state.PendingChanges.TryGetValue((row, col), out var localValue))
		{
			return localValue;
		}
		if (_state.CurrentTable != null)
		{
			return _state.CurrentTable.Rows[row][col];
		}
		if (_state.CurrentData.Rank == 1)
		{
			return _state.CurrentData.GetValue(row);
		}
		return _state.CurrentData.GetValue(row, col);
	}

	private Type GetCellType(int col)
	{
		if (_state.CurrentTable != null)
		{
			return _state.CurrentTable.Columns[col].Type;
		}
		return _state.CurrentData.GetType().GetElementType();
	}

	private void CommitCellEdit()
	{
		Type cellType = GetCellType(_state.CellCursorCol);
		if (TryParseValue(_state.EditBuffer, cellType, out var result))
		{
			_state.PendingChanges[(_state.CellCursorRow, _state.CellCursorCol)] = result;
			UpdateInMemoryData(_state.CellCursorRow, _state.CellCursorCol, result);
			_state.IsEditingCell = false;
			_state.EditError = null;
			_state.CachedColumnStats = null;
		}
		else
		{
			_state.EditError = "Cannot parse '" + _state.EditBuffer + "' as " + cellType.Name;
		}
	}

	private void CancelCellEdit()
	{
		_state.IsEditingCell = false;
		_state.EditBuffer = "";
		_state.EditError = null;
	}

	private static bool TryParseValue(string input, Type targetType, out object result)
	{
		result = null;
		try
		{
			if (targetType == typeof(float))
			{
				result = float.Parse(input);
				return true;
			}
			if (targetType == typeof(double))
			{
				result = double.Parse(input);
				return true;
			}
			if (targetType == typeof(int))
			{
				result = int.Parse(input);
				return true;
			}
			if (targetType == typeof(uint))
			{
				result = uint.Parse(input);
				return true;
			}
			if (targetType == typeof(long))
			{
				result = long.Parse(input);
				return true;
			}
			if (targetType == typeof(ulong))
			{
				result = ulong.Parse(input);
				return true;
			}
			if (targetType == typeof(short))
			{
				result = short.Parse(input);
				return true;
			}
			if (targetType == typeof(ushort))
			{
				result = ushort.Parse(input);
				return true;
			}
			if (targetType == typeof(byte))
			{
				result = byte.Parse(input);
				return true;
			}
			if (targetType == typeof(sbyte))
			{
				result = sbyte.Parse(input);
				return true;
			}
			if (targetType == typeof(bool))
			{
				result = bool.Parse(input);
				return true;
			}
			if (targetType == typeof(string))
			{
				result = input;
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	private void UpdateInMemoryData(int row, int col, object rows)
	{
		if (_state.CurrentTable != null)
		{
			_state.CurrentTable.Rows[row][col] = rows;
		}
		else if (_state.CurrentData.Rank == 1)
		{
			_state.CurrentData.SetValue(rows, row);
		}
		else
		{
			_state.CurrentData.SetValue(rows, row, col);
		}
	}

	private void ExitInteractiveMode()
	{
		if (_state.HasUnsavedChanges)
		{
			_state.ShowingSavePrompt = true;
		}
		else
		{
			_state.IsInteractiveMode = false;
		}
	}

	private void DiscardChanges()
	{
		H5TreeNode h5TreeNode = FindNodeByPath(_state.CurrentDatasetPath);
		if (h5TreeNode != null)
		{
			_state.PendingChanges.Clear();
			_state.DeletedRows.Clear();
			if (_state.CurrentTable != null)
			{
				_state.CurrentTable = _reader.ReadCompoundElementsAsTable(h5TreeNode.FullPath);
			}
			else if (_state.CurrentData != null)
			{
				Type dataType = h5TreeNode.DataType;
				if (h5TreeNode.Dimensions.Length == 1)
				{
					LoadData1D(h5TreeNode.FullPath, dataType);
				}
				else
				{
					LoadData2D(h5TreeNode.FullPath, dataType);
				}
			}
		}
		_state.EditError = null;
	}

	private H5TreeNode FindNodeByPath(string path)
	{
		foreach (H5TreeNode visibleNode in _state.VisibleNodes)
		{
			if (visibleNode.FullPath == path)
			{
				return visibleNode;
			}
		}
		return null;
	}

	private void SaveChanges()
	{
		if (!_state.HasUnsavedChanges)
		{
			return;
		}
		try
		{
			_reader.Dispose();
			_reader = null;
			using (H5Writer h5Writer = new H5Writer(_filename))
			{
				if (_state.CurrentTable != null)
				{
					if (_state.DeletedRows.Count > 0)
					{
						foreach (int orderByDescending in _state.DeletedRows.OrderByDescending((int r) => r))
						{
							_state.CurrentTable.RemoveRow(orderByDescending);
						}
					}
					h5Writer.WriteCompoundTable(_state.CurrentDatasetPath, _state.CurrentTable);
				}
				else if (_state.CurrentData != null)
				{
					if (_state.DeletedRows.Count > 0)
					{
						RebuildArrayWithoutDeletedRows(h5Writer);
					}
					else
					{
						Type elementType = _state.CurrentData.GetType().GetElementType();
						foreach (KeyValuePair<(int, int), object> pendingChange in _state.PendingChanges)
						{
							(int, int) key = pendingChange.Key;
							int ordinal = key.Item1;
							int ordinal2 = key.Item2;
							object pendingChangeTerm = pendingChange.Value;
							WriteCellByType(h5Writer, ordinal, ordinal2, pendingChangeTerm, elementType);
						}
					}
				}
			}
			_state.PendingChanges.Clear();
			_state.DeletedRows.Clear();
			_state.EditError = null;
		}
		catch (Exception ex)
		{
			_state.EditError = "Save failed: " + ex.Message;
		}
		finally
		{
			_reader = new H5Reader(_filename);
		}
	}

	private void RebuildArrayWithoutDeletedRows(H5Writer writer)
	{
		Type elementType = _state.CurrentData.GetType().GetElementType();
		int length = _state.CurrentData.GetLength(0);
		List<int> keepRows = (from r in Enumerable.Range(0, length)
			where !_state.DeletedRows.Contains(r)
			select r).ToList();
		if (_state.CurrentData.Rank == 1)
		{
			RebuildArray1D(writer, elementType, keepRows);
		}
		else
		{
			RebuildArray2D(writer, elementType, keepRows);
		}
	}

	private void RebuildArray1D(H5Writer writer, Type elementType, List<int> keepRows)
	{
		if (elementType == typeof(float))
		{
			float[] floatValues = new float[keepRows.Count];
			for (int i = 0; i < keepRows.Count; i++)
			{
				floatValues[i] = (float)_state.CurrentData.GetValue(keepRows[i]);
			}
			writer.WriteDataset(_state.CurrentDatasetPath, floatValues);
		}
		else if (elementType == typeof(double))
		{
			double[] doubleValues = new double[keepRows.Count];
			for (int j = 0; j < keepRows.Count; j++)
			{
				doubleValues[j] = (double)_state.CurrentData.GetValue(keepRows[j]);
			}
			writer.WriteDataset(_state.CurrentDatasetPath, doubleValues);
		}
		else if (elementType == typeof(int))
		{
			int[] intValues = new int[keepRows.Count];
			for (int k = 0; k < keepRows.Count; k++)
			{
				intValues[k] = (int)_state.CurrentData.GetValue(keepRows[k]);
			}
			writer.WriteDataset(_state.CurrentDatasetPath, intValues);
		}
		else if (elementType == typeof(uint))
		{
			uint[] uints = new uint[keepRows.Count];
			for (int l = 0; l < keepRows.Count; l++)
			{
				uints[l] = (uint)_state.CurrentData.GetValue(keepRows[l]);
			}
			writer.WriteDataset(_state.CurrentDatasetPath, uints);
		}
		else if (elementType == typeof(long))
		{
			long[] longs = new long[keepRows.Count];
			for (int m = 0; m < keepRows.Count; m++)
			{
				longs[m] = (long)_state.CurrentData.GetValue(keepRows[m]);
			}
			writer.WriteDataset(_state.CurrentDatasetPath, longs);
		}
		else if (elementType == typeof(ulong))
		{
			ulong[] ulongs = new ulong[keepRows.Count];
			for (int n = 0; n < keepRows.Count; n++)
			{
				ulongs[n] = (ulong)_state.CurrentData.GetValue(keepRows[n]);
			}
			writer.WriteDataset(_state.CurrentDatasetPath, ulongs);
		}
		else if (elementType == typeof(short))
		{
			short[] shorts = new short[keepRows.Count];
			for (int intervalIndex = 0; intervalIndex < keepRows.Count; intervalIndex++)
			{
				shorts[intervalIndex] = (short)_state.CurrentData.GetValue(keepRows[intervalIndex]);
			}
			writer.WriteDataset(_state.CurrentDatasetPath, shorts);
		}
		else if (elementType == typeof(ushort))
		{
			ushort[] ushorts = new ushort[keepRows.Count];
			for (int intervalIndex2 = 0; intervalIndex2 < keepRows.Count; intervalIndex2++)
			{
				ushorts[intervalIndex2] = (ushort)_state.CurrentData.GetValue(keepRows[intervalIndex2]);
			}
			writer.WriteDataset(_state.CurrentDatasetPath, ushorts);
		}
		else if (elementType == typeof(byte))
		{
			byte[] byteValues = new byte[keepRows.Count];
			for (int intervalIndex3 = 0; intervalIndex3 < keepRows.Count; intervalIndex3++)
			{
				byteValues[intervalIndex3] = (byte)_state.CurrentData.GetValue(keepRows[intervalIndex3]);
			}
			writer.WriteDataset(_state.CurrentDatasetPath, byteValues);
		}
		else if (elementType == typeof(sbyte))
		{
			sbyte[] sbytes = new sbyte[keepRows.Count];
			for (int intervalIndex4 = 0; intervalIndex4 < keepRows.Count; intervalIndex4++)
			{
				sbytes[intervalIndex4] = (sbyte)_state.CurrentData.GetValue(keepRows[intervalIndex4]);
			}
			writer.WriteDataset(_state.CurrentDatasetPath, sbytes);
		}
	}

	private void RebuildArray2D(H5Writer writer, Type elementType, List<int> keepRows)
	{
		int length = _state.CurrentData.GetLength(1);
		if (elementType == typeof(float))
		{
			float[,] floatValues = new float[keepRows.Count, length];
			for (int i = 0; i < keepRows.Count; i++)
			{
				for (int j = 0; j < length; j++)
				{
					floatValues[i, j] = (float)_state.CurrentData.GetValue(keepRows[i], j);
				}
			}
			writer.WriteDataset(_state.CurrentDatasetPath, floatValues);
		}
		else if (elementType == typeof(double))
		{
			double[,] doubleValues = new double[keepRows.Count, length];
			for (int k = 0; k < keepRows.Count; k++)
			{
				for (int l = 0; l < length; l++)
				{
					doubleValues[k, l] = (double)_state.CurrentData.GetValue(keepRows[k], l);
				}
			}
			writer.WriteDataset(_state.CurrentDatasetPath, doubleValues);
		}
		else if (elementType == typeof(int))
		{
			int[,] intValues = new int[keepRows.Count, length];
			for (int m = 0; m < keepRows.Count; m++)
			{
				for (int n = 0; n < length; n++)
				{
					intValues[m, n] = (int)_state.CurrentData.GetValue(keepRows[m], n);
				}
			}
			writer.WriteDataset(_state.CurrentDatasetPath, intValues);
		}
		else if (elementType == typeof(uint))
		{
			uint[,] uints = new uint[keepRows.Count, length];
			for (int intervalIndex = 0; intervalIndex < keepRows.Count; intervalIndex++)
			{
				for (int intervalIndex2 = 0; intervalIndex2 < length; intervalIndex2++)
				{
					uints[intervalIndex, intervalIndex2] = (uint)_state.CurrentData.GetValue(keepRows[intervalIndex], intervalIndex2);
				}
			}
			writer.WriteDataset(_state.CurrentDatasetPath, uints);
		}
		else if (elementType == typeof(long))
		{
			long[,] longs = new long[keepRows.Count, length];
			for (int intervalIndex3 = 0; intervalIndex3 < keepRows.Count; intervalIndex3++)
			{
				for (int intervalIndex4 = 0; intervalIndex4 < length; intervalIndex4++)
				{
					longs[intervalIndex3, intervalIndex4] = (long)_state.CurrentData.GetValue(keepRows[intervalIndex3], intervalIndex4);
				}
			}
			writer.WriteDataset(_state.CurrentDatasetPath, longs);
		}
		else if (elementType == typeof(ulong))
		{
			ulong[,] ulongs = new ulong[keepRows.Count, length];
			for (int intervalIndex5 = 0; intervalIndex5 < keepRows.Count; intervalIndex5++)
			{
				for (int intervalIndex6 = 0; intervalIndex6 < length; intervalIndex6++)
				{
					ulongs[intervalIndex5, intervalIndex6] = (ulong)_state.CurrentData.GetValue(keepRows[intervalIndex5], intervalIndex6);
				}
			}
			writer.WriteDataset(_state.CurrentDatasetPath, ulongs);
		}
		else if (elementType == typeof(short))
		{
			short[,] shorts = new short[keepRows.Count, length];
			for (int intervalIndex7 = 0; intervalIndex7 < keepRows.Count; intervalIndex7++)
			{
				for (int intervalIndex8 = 0; intervalIndex8 < length; intervalIndex8++)
				{
					shorts[intervalIndex7, intervalIndex8] = (short)_state.CurrentData.GetValue(keepRows[intervalIndex7], intervalIndex8);
				}
			}
			writer.WriteDataset(_state.CurrentDatasetPath, shorts);
		}
		else if (elementType == typeof(ushort))
		{
			ushort[,] ushorts = new ushort[keepRows.Count, length];
			for (int intervalIndex9 = 0; intervalIndex9 < keepRows.Count; intervalIndex9++)
			{
				for (int intervalIndex10 = 0; intervalIndex10 < length; intervalIndex10++)
				{
					ushorts[intervalIndex9, intervalIndex10] = (ushort)_state.CurrentData.GetValue(keepRows[intervalIndex9], intervalIndex10);
				}
			}
			writer.WriteDataset(_state.CurrentDatasetPath, ushorts);
		}
		else if (elementType == typeof(byte))
		{
			byte[,] byteValues = new byte[keepRows.Count, length];
			for (int intervalIndex11 = 0; intervalIndex11 < keepRows.Count; intervalIndex11++)
			{
				for (int intervalIndex12 = 0; intervalIndex12 < length; intervalIndex12++)
				{
					byteValues[intervalIndex11, intervalIndex12] = (byte)_state.CurrentData.GetValue(keepRows[intervalIndex11], intervalIndex12);
				}
			}
			writer.WriteDataset(_state.CurrentDatasetPath, byteValues);
		}
		else
		{
			if (!(elementType == typeof(sbyte)))
			{
				return;
			}
			sbyte[,] sbytes = new sbyte[keepRows.Count, length];
			for (int intervalIndex13 = 0; intervalIndex13 < keepRows.Count; intervalIndex13++)
			{
				for (int intervalIndex14 = 0; intervalIndex14 < length; intervalIndex14++)
				{
					sbytes[intervalIndex13, intervalIndex14] = (sbyte)_state.CurrentData.GetValue(keepRows[intervalIndex13], intervalIndex14);
				}
			}
			writer.WriteDataset(_state.CurrentDatasetPath, sbytes);
		}
	}

	private void WriteCellByType(H5Writer writer, int row, int col, object localValue, Type elementType)
	{
		if (elementType == typeof(float))
		{
			WriteCell(writer, row, col, (float)localValue);
		}
		else if (elementType == typeof(double))
		{
			WriteCell(writer, row, col, (double)localValue);
		}
		else if (elementType == typeof(int))
		{
			WriteCell(writer, row, col, (int)localValue);
		}
		else if (elementType == typeof(uint))
		{
			WriteCell(writer, row, col, (uint)localValue);
		}
		else if (elementType == typeof(long))
		{
			WriteCell(writer, row, col, (long)localValue);
		}
		else if (elementType == typeof(ulong))
		{
			WriteCell(writer, row, col, (ulong)localValue);
		}
		else if (elementType == typeof(short))
		{
			WriteCell(writer, row, col, (short)localValue);
		}
		else if (elementType == typeof(ushort))
		{
			WriteCell(writer, row, col, (ushort)localValue);
		}
		else if (elementType == typeof(byte))
		{
			WriteCell(writer, row, col, (byte)localValue);
		}
		else if (elementType == typeof(sbyte))
		{
			WriteCell(writer, row, col, (sbyte)localValue);
		}
	}

	private void WriteCell<T>(H5Writer writer, int row, int col, T localValue) where T : unmanaged
	{
		if (_state.CurrentData.Rank == 1)
		{
			T[] values = new T[1] { localValue };
			writer.InsertValues(_state.CurrentDatasetPath, values, row);
		}
		else
		{
			T[,] values2 = new T[1, 1] { { localValue } };
			writer.InsertValues(_state.CurrentDatasetPath, values2, row, col);
		}
	}
}
