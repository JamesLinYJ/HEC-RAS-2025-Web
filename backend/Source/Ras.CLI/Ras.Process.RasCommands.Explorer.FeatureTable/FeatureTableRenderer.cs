using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Ras.Process.RasCommands.Explorer.FeatureTable;

public class FeatureTableRenderer
{
	public IRenderable Render(FeatureTableState state, int consoleWidth, int consoleHeight)
	{
		List<IRenderable> iRenderables = new List<IRenderable>();
		string pendingInfo = GetPendingInfo(state);
		iRenderables.Add(new Rule($"[bold]{EscapeMarkup(state.LayerName)}[/] ({state.FeatureCount} features){pendingInfo}").LeftJustified());
		string message = (state.IsInteractiveMode ? $" | Cursor: [cyan]({state.CellCursorRow}, {state.CellCursorCol})[/]" : "");
		iRenderables.Add(new Markup($"Columns: [cyan]{state.ColumnCount}[/] | Position: [cyan]({state.RowScrollOffset}, {state.ColScrollOffset})[/]{message}"));
		iRenderables.Add(new Rule().RuleStyle("dim"));
		int ordinal = 14;
		int ordinal2 = 8;
		int consoleWidthTerm = consoleWidth - ordinal2 - 4;
		int ordinal3 = Math.Max(1, consoleWidthTerm / (ordinal + 3));
		int consoleHeightOffset = consoleHeight - 9;
		Table table = new Table();
		table.Border(TableBorder.Rounded);
		table.AddColumn(new TableColumn("[bold]Row[/]").RightAligned());
		int count = Math.Min(state.ColScrollOffset + ordinal3, state.ColumnCount);
		for (int i = state.ColScrollOffset; i < count; i++)
		{
			ColumnInfo columnInfo = state.Columns[i];
			string header = "[bold]" + EscapeMarkup(columnInfo.Name) + "[/]";
			if (columnInfo.IsReadOnly)
			{
				header = "[dim]" + EscapeMarkup(columnInfo.Name) + "[/]";
			}
			table.AddColumn(new TableColumn(header).RightAligned());
		}
		int count2 = Math.Min(state.RowScrollOffset + consoleHeightOffset, state.FeatureCount);
		for (int j = state.RowScrollOffset; j < count2; j++)
		{
			string[] strings = new string[count - state.ColScrollOffset + 1];
			strings[0] = FormatRowIndex(state, j);
			for (int k = state.ColScrollOffset; k < count; k++)
			{
				object cellValue = state.GetCellValue(j, k);
				strings[k - state.ColScrollOffset + 1] = FormatCell(state, j, k, cellValue);
			}
			table.AddRow(strings);
		}
		iRenderables.Add(table);
		if (!string.IsNullOrEmpty(state.EditError))
		{
			iRenderables.Add(new Markup("[red]" + EscapeMarkup(state.EditError) + "[/]"));
		}
		else if (!string.IsNullOrEmpty(state.StatusMessage))
		{
			iRenderables.Add(new Markup("[green]" + EscapeMarkup(state.StatusMessage) + "[/]"));
		}
		iRenderables.Add(new Rule().RuleStyle("dim"));
		iRenderables.Add(new Markup(GetFooterText(state)));
		return new Rows(iRenderables);
	}

	private string FormatCell(FeatureTableState state, int row, int col, object localValue)
	{
		string formatValue = FeatureTableDataAccessor.FormatValue(localValue);
		formatValue = ((!string.IsNullOrEmpty(formatValue)) ? EscapeMarkup(formatValue) : "[dim]-[/]");
		bool deletedRowsStateTerm = state.DeletedRows.Contains(row);
		bool isInteractiveMode = state.IsInteractiveMode && row == state.CellCursorRow && col == state.CellCursorCol;
		bool containsKey = state.PendingChanges.ContainsKey((row, col));
		if (deletedRowsStateTerm)
		{
			return "[strikethrough dim]" + formatValue + "[/]";
		}
		if (state.IsEditingCell & isInteractiveMode)
		{
			string escapeMarkup = EscapeMarkup(state.EditBuffer);
			return "[black on white]" + escapeMarkup + "_[/]";
		}
		if (isInteractiveMode)
		{
			return "[black on yellow]" + formatValue + "[/]";
		}
		if (containsKey)
		{
			return "[green]" + formatValue + "[/]";
		}
		return formatValue;
	}

	private string FormatRowIndex(FeatureTableState state, int row)
	{
		bool deletedRowsStateTerm = state.DeletedRows.Contains(row);
		bool isInteractiveMode = state.IsInteractiveMode && state.CellCursorCol == -1 && state.CellCursorRow == row;
		string row2 = row.ToString();
		if (deletedRowsStateTerm)
		{
			return "[strikethrough dim]" + row2 + "[/]";
		}
		if (isInteractiveMode)
		{
			return "[black on yellow]" + row2 + "[/]";
		}
		return row2;
	}

	private static string GetPendingInfo(FeatureTableState state)
	{
		if (!state.HasUnsavedChanges)
		{
			return "";
		}
		List<string> stringList = new List<string>();
		if (state.PendingChanges.Count > 0)
		{
			stringList.Add($"{state.PendingChanges.Count} edited");
		}
		if (state.DeletedRows.Count > 0)
		{
			stringList.Add($"{state.DeletedRows.Count} deleted");
		}
		return " [yellow][[" + string.Join(", ", stringList) + "]][/]";
	}

	private static string GetFooterText(FeatureTableState state)
	{
		if (state.ShowingSavePrompt)
		{
			return "[yellow]Unsaved changes![/] [[[bold]S[/]]]ave  [[[bold]D[/]]]iscard  [[[bold]C[/]]]ancel";
		}
		if (state.ShowingDeleteConfirm)
		{
			return "[yellow]Delete this row?[/] [[[bold]Y[/]]]es  [[[bold]N[/]]]o";
		}
		if (state.IsEditingCell)
		{
			return "[cyan][[EDIT]][/] Type value  [dim][[Enter]][/] Accept  [dim][[Esc]][/] Cancel";
		}
		if (state.IsInteractiveMode)
		{
			if (state.CellCursorCol == -1)
			{
				string deletedRowsStateTerm = (state.DeletedRows.Contains(state.CellCursorRow) ? "undelete" : "delete");
				string path = (state.HasUnsavedChanges ? "  [dim][[Ctrl+W]][/] Save" : "");
				return $"[cyan][[ROW {state.CellCursorRow}]][/] [dim][[c]][/] Copy row  [dim][[d]][/] {deletedRowsStateTerm}  [dim][[Arrows]][/] Move{path}  [dim][[Esc]][/] Exit";
			}
			string path2 = (state.HasUnsavedChanges ? "  [dim][[Ctrl+W]][/] Save" : "");
			return "[cyan][[INTERACTIVE]][/] [dim][[Arrows]][/] Move  [dim][[c]][/] Copy  [dim][[Enter]][/] Edit" + path2 + "  [dim][[Esc]][/] Exit";
		}
		return "[dim][[Arrows]][/] Navigate  [dim][[PgUp/PgDn]][/] Page  [dim][[c]][/] Copy CSV  [dim][[i]][/] Interactive  [dim][[Esc]][/] Back";
	}

	private static string EscapeMarkup(string textValue)
	{
		return textValue?.Replace("[", "[[").Replace("]", "]]") ?? "";
	}
}
