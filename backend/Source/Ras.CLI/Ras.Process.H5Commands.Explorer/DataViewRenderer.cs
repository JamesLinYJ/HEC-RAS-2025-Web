using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;
using Utility.Memory;

namespace Ras.Process.H5Commands.Explorer;

public class DataViewRenderer
{
	public IRenderable Render(ExplorerState state, int consoleWidth, int consoleHeight)
	{
		if (state.CurrentTable != null)
		{
			return RenderTable(state, consoleWidth, consoleHeight);
		}
		if (state.CurrentData == null)
		{
			return new Markup("[red]No data loaded[/]");
		}
		int rank = state.CurrentData.Rank;
		return rank switch
		{
			1 => Render1D(state, consoleWidth, consoleHeight), 
			2 => Render2D(state, consoleWidth, consoleHeight), 
			_ => new Markup($"[red]Unsupported data rank: {rank}[/]"), 
		};
	}

	private IRenderable Render1D(ExplorerState state, int consoleWidth, int consoleHeight)
	{
		List<IRenderable> iRenderables = new List<IRenderable>();
		Array currentData = state.CurrentData;
		int length = currentData.GetLength(0);
		string getElementTypeGetTypeTerm = currentData.GetType().GetElementType()?.Name ?? "?";
		string pendingInfo = GetPendingInfo(state);
		iRenderables.Add(new Rule("[bold]Dataset: " + EscapeMarkup(state.CurrentDatasetPath) + "[/]" + pendingInfo).LeftJustified());
		string message = (state.IsInteractiveMode ? $" | Cursor: [cyan]({state.CellCursorRow}, {state.CellCursorCol})[/]" : "");
		iRenderables.Add(new Markup($"Type: [cyan]{getElementTypeGetTypeTerm}[/] | Length: [cyan]{length}[/] | Viewing from: [cyan]{state.DataRowOffset}[/]{message}"));
		iRenderables.Add(new Rule().RuleStyle("dim"));
		Spectre.Console.Table table = new Spectre.Console.Table();
		table.Border(TableBorder.Rounded);
		table.AddColumn(new Spectre.Console.TableColumn("[bold]Index[/]").RightAligned());
		string header = FormatColumnHeader(state, 0, "Value");
		table.AddColumn(new Spectre.Console.TableColumn(header).RightAligned());
		int showColumnStatsStateTerm = ((state.ShowColumnStats && state.CachedColumnStats != null) ? 12 : 0);
		int consoleHeightTerm = consoleHeight - 8 - showColumnStatsStateTerm;
		int count = Math.Min(state.DataRowOffset + consoleHeightTerm, length);
		for (int i = state.DataRowOffset; i < count; i++)
		{
			object getValue = (state.PendingChanges.TryGetValue((i, 0), out var localValue) ? localValue : currentData.GetValue(i));
			table.AddRow(FormatRowIndex(state, i), FormatCell(state, i, 0, getValue));
		}
		iRenderables.Add(table);
		if (!string.IsNullOrEmpty(state.EditError))
		{
			iRenderables.Add(new Markup("[red]" + EscapeMarkup(state.EditError) + "[/]"));
		}
		if (state.ShowColumnStats && state.CachedColumnStats != null)
		{
			iRenderables.Add(new Rule().RuleStyle("dim"));
			iRenderables.Add(RenderStatsPanel(state));
		}
		iRenderables.Add(new Rule().RuleStyle("dim"));
		iRenderables.Add(new Markup(GetFooterText(state)));
		return new Rows(iRenderables);
	}

	private IRenderable Render2D(ExplorerState state, int consoleWidth, int consoleHeight)
	{
		List<IRenderable> iRenderables = new List<IRenderable>();
		Array currentData = state.CurrentData;
		int length = currentData.GetLength(0);
		int length2 = currentData.GetLength(1);
		string getElementTypeGetTypeTerm = currentData.GetType().GetElementType()?.Name ?? "?";
		string pendingInfo = GetPendingInfo(state);
		iRenderables.Add(new Rule("[bold]Dataset: " + EscapeMarkup(state.CurrentDatasetPath) + "[/]" + pendingInfo).LeftJustified());
		string message = (state.IsInteractiveMode ? $" | Cursor: [cyan]({state.CellCursorRow}, {state.CellCursorCol})[/]" : "");
		iRenderables.Add(new Markup($"Type: [cyan]{getElementTypeGetTypeTerm}[/] | Shape: [cyan]{length} x {length2}[/] | Position: [cyan]({state.DataRowOffset}, {state.DataColOffset})[/]{message}"));
		iRenderables.Add(new Rule().RuleStyle("dim"));
		int ordinal = 12;
		int ordinal2 = 8;
		int consoleWidthTerm = consoleWidth - ordinal2 - 4;
		int ordinal3 = Math.Max(1, consoleWidthTerm / (ordinal + 3));
		int showColumnStatsStateTerm = ((state.ShowColumnStats && state.CachedColumnStats != null) ? 12 : 0);
		int consoleHeightTerm = consoleHeight - 9 - showColumnStatsStateTerm;
		Spectre.Console.Table table = new Spectre.Console.Table();
		table.Border(TableBorder.Rounded);
		table.AddColumn(new Spectre.Console.TableColumn("[bold]Row[/]").RightAligned());
		int count = Math.Min(state.DataColOffset + ordinal3, length2);
		for (int i = state.DataColOffset; i < count; i++)
		{
			string header = FormatColumnHeader(state, i, i.ToString());
			table.AddColumn(new Spectre.Console.TableColumn(header).RightAligned());
		}
		int count2 = Math.Min(state.DataRowOffset + consoleHeightTerm, length);
		for (int j = state.DataRowOffset; j < count2; j++)
		{
			string[] strings = new string[count - state.DataColOffset + 1];
			strings[0] = FormatRowIndex(state, j);
			for (int k = state.DataColOffset; k < count; k++)
			{
				object getValue = (state.PendingChanges.TryGetValue((j, k), out var localValue) ? localValue : currentData.GetValue(j, k));
				strings[k - state.DataColOffset + 1] = FormatCell(state, j, k, getValue);
			}
			table.AddRow(strings);
		}
		iRenderables.Add(table);
		if (!string.IsNullOrEmpty(state.EditError))
		{
			iRenderables.Add(new Markup("[red]" + EscapeMarkup(state.EditError) + "[/]"));
		}
		if (state.ShowColumnStats && state.CachedColumnStats != null)
		{
			iRenderables.Add(new Rule().RuleStyle("dim"));
			iRenderables.Add(RenderStatsPanel(state));
		}
		iRenderables.Add(new Rule().RuleStyle("dim"));
		iRenderables.Add(new Markup(GetFooterText(state)));
		return new Rows(iRenderables);
	}

	private static string FormatValue(object localValue)
	{
		if (localValue == null)
		{
			return "[dim]null[/]";
		}
		if (!(localValue is float f))
		{
			if (localValue is double d)
			{
				return FormatDouble(d);
			}
			return localValue.ToString();
		}
		return FormatFloat(f);
	}

	private string FormatCell(ExplorerState state, int row, int col, object localValue)
	{
		string formatValue = FormatValue(localValue);
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

	private string FormatColumnHeader(ExplorerState state, int col, string headerText)
	{
		if (state.CellCursorRow == -1 && state.CellCursorCol == col && state.IsInteractiveMode)
		{
			return "[black on cyan]" + headerText + "[/]";
		}
		return "[bold]" + headerText + "[/]";
	}

	private string FormatRowIndex(ExplorerState state, int row)
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

	private static string FormatFloat(float f)
	{
		if (float.IsNaN(f))
		{
			return "NaN";
		}
		if (float.IsInfinity(f))
		{
			if (!(f > 0f))
			{
				return "-Inf";
			}
			return "Inf";
		}
		if (Math.Abs(f) >= 1E+09f)
		{
			return f.ToString("E3");
		}
		return f.ToString("N6");
	}

	private static string FormatDouble(double d)
	{
		if (double.IsNaN(d))
		{
			return "NaN";
		}
		if (double.IsInfinity(d))
		{
			if (!(d > 0.0))
			{
				return "-Inf";
			}
			return "Inf";
		}
		if (Math.Abs(d) >= 1000000000.0)
		{
			return d.ToString("E3");
		}
		return d.ToString("N6");
	}

	private static string EscapeMarkup(string textValue)
	{
		return textValue?.Replace("[", "[[").Replace("]", "]]") ?? "";
	}

	private static string GetPendingInfo(ExplorerState state)
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

	private IRenderable RenderTable(ExplorerState state, int consoleWidth, int consoleHeight)
	{
		List<IRenderable> iRenderables = new List<IRenderable>();
		Utility.Memory.Table currentTable = state.CurrentTable;
		int count = currentTable.Rows.Count;
		int count2 = currentTable.Columns.Count;
		string pendingInfo = GetPendingInfo(state);
		iRenderables.Add(new Rule("[bold]Dataset: " + EscapeMarkup(state.CurrentDatasetPath) + "[/]" + pendingInfo).LeftJustified());
		string message = (state.IsInteractiveMode ? $" | Cursor: [cyan]({state.CellCursorRow}, {state.CellCursorCol})[/]" : "");
		iRenderables.Add(new Markup($"Type: [cyan]Compound[/] | Rows: [cyan]{count}[/] | Columns: [cyan]{count2}[/] | Position: [cyan]({state.DataRowOffset}, {state.DataColOffset})[/]{message}"));
		iRenderables.Add(new Rule().RuleStyle("dim"));
		int ordinal = 14;
		int ordinal2 = 8;
		int consoleWidthTerm = consoleWidth - ordinal2 - 4;
		int ordinal3 = Math.Max(1, consoleWidthTerm / (ordinal + 3));
		int showColumnStatsStateTerm = ((state.ShowColumnStats && state.CachedColumnStats != null) ? 12 : 0);
		int consoleHeightTerm = consoleHeight - 9 - showColumnStatsStateTerm;
		Spectre.Console.Table table = new Spectre.Console.Table();
		table.Border(TableBorder.Rounded);
		table.AddColumn(new Spectre.Console.TableColumn("[bold]Row[/]").RightAligned());
		int count3 = Math.Min(state.DataColOffset + ordinal3, count2);
		for (int i = state.DataColOffset; i < count3; i++)
		{
			Utility.Memory.TableColumn tableColumn = currentTable.Columns[i];
			string header = FormatColumnHeader(state, i, tableColumn.Name);
			table.AddColumn(new Spectre.Console.TableColumn(header).RightAligned());
		}
		int count4 = Math.Min(state.DataRowOffset + consoleHeightTerm, count);
		for (int j = state.DataRowOffset; j < count4; j++)
		{
			string[] strings = new string[count3 - state.DataColOffset + 1];
			strings[0] = FormatRowIndex(state, j);
			Utility.Memory.TableRow tableRow = currentTable.Rows[j];
			for (int k = state.DataColOffset; k < count3; k++)
			{
				object tableRow2 = (state.PendingChanges.TryGetValue((j, k), out var localValue) ? localValue : tableRow[k]);
				strings[k - state.DataColOffset + 1] = FormatCell(state, j, k, tableRow2);
			}
			table.AddRow(strings);
		}
		iRenderables.Add(table);
		if (!string.IsNullOrEmpty(state.EditError))
		{
			iRenderables.Add(new Markup("[red]" + EscapeMarkup(state.EditError) + "[/]"));
		}
		if (state.ShowColumnStats && state.CachedColumnStats != null)
		{
			iRenderables.Add(new Rule().RuleStyle("dim"));
			iRenderables.Add(RenderStatsPanel(state));
		}
		iRenderables.Add(new Rule().RuleStyle("dim"));
		iRenderables.Add(new Markup(GetFooterText(state)));
		return new Rows(iRenderables);
	}

	private IRenderable RenderStatsPanel(ExplorerState state)
	{
		if (state.CachedColumnStats == null)
		{
			return new Markup("[dim]No column selected[/]");
		}
		ColumnStatistics cachedColumnStats = state.CachedColumnStats;
		if (!cachedColumnStats.IsNumeric)
		{
			return new Markup("[yellow]" + EscapeMarkup(cachedColumnStats.ErrorMessage) + "[/]");
		}
		Spectre.Console.Table table = new Spectre.Console.Table();
		table.Border(TableBorder.Rounded);
		table.AddColumn("Statistic");
		table.AddColumn("Value");
		table.AddRow("[bold]Column[/]", EscapeMarkup(cachedColumnStats.ColumnName));
		table.AddRow("[bold]Type[/]", cachedColumnStats.ColumnType.Name);
		table.AddRow("Total Count", cachedColumnStats.TotalCount.ToString());
		table.AddRow("Valid Count", cachedColumnStats.ValidCount.ToString());
		if (cachedColumnStats.NullCount > 0)
		{
			table.AddRow("Null Count", $"[dim]{cachedColumnStats.NullCount}[/]");
		}
		if (cachedColumnStats.Min.HasValue)
		{
			table.AddRow("[cyan]Min[/]", cachedColumnStats.Min.Value.ToString("G6"));
			table.AddRow("[cyan]Max[/]", cachedColumnStats.Max.Value.ToString("G6"));
			table.AddRow("[cyan]Mean[/]", cachedColumnStats.Mean.Value.ToString("G6"));
			table.AddRow("[cyan]Std Dev[/]", cachedColumnStats.StdDev.Value.ToString("G6"));
		}
		return new Panel(table).Header("[bold yellow]Column Statistics[/]").BorderStyle("yellow");
	}

	private static string GetFooterText(ExplorerState state)
	{
		if (state.ShowingSavePrompt)
		{
			return "[yellow]Unsaved changes![/] [[[bold]S[/]]]ave  [[[bold]D[/]]]iscard  [[[bold]C[/]]]ancel";
		}
		if (state.IsEditingCell)
		{
			return "[cyan][[EDIT]][/] Type value  [dim][[Enter]][/] Accept  [dim][[Esc]][/] Cancel";
		}
		if (state.IsInteractiveMode)
		{
			if (state.CellCursorRow == -1 && state.CellCursorCol >= 0)
			{
				string showColumnStatsStateTerm = (state.ShowColumnStats ? "Hide" : "Show");
				return $"[cyan][[COLUMN {state.CellCursorCol}]][/] [dim][[s]][/] {showColumnStatsStateTerm} stats  [dim][[Arrows]][/] Move  [dim][[Esc]][/] Exit";
			}
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
}
