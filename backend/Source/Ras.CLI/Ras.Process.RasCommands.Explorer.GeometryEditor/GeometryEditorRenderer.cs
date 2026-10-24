using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Ras.Process.RasCommands.Explorer.GeometryEditor;

public class GeometryEditorRenderer
{
	public IRenderable Render(GeometryEditorState state, int consoleWidth, int consoleHeight)
	{
		List<IRenderable> iRenderables = new List<IRenderable>();
		string path = (state.HasUnsavedChanges ? " [yellow][[Unsaved *]][/]" : "");
		iRenderables.Add(new Rule("[bold]Geometry Editor: " + EscapeMarkup(state.Geometry?.Name ?? "Unknown") + "[/]" + path).LeftJustified());
		if (state.ShowingSavePrompt)
		{
			iRenderables.Add(RenderSavePrompt(state));
			return new Rows(iRenderables);
		}
		if (state.ShowingSaveAsPrompt)
		{
			iRenderables.Add(RenderSaveAsPrompt(state));
			return new Rows(iRenderables);
		}
		int ordinal = 4;
		int ordinal2 = 5;
		int consoleHeightTerm = Math.Max(5, consoleHeight - ordinal - ordinal2);
		int ordinal3 = 42;
		int ordinal4 = 20;
		int consoleWidthTerm = Math.Max(20, consoleWidth - ordinal3 - ordinal4 - 10);
		Table table = new Table();
		table.Border(TableBorder.Rounded);
		table.Expand();
		table.Title = new TableTitle("[bold cyan]Associations[/] [dim](saved to Project file)[/]");
		table.AddColumn(new TableColumn("[bold]Property[/]").Width(ordinal3));
		table.AddColumn(new TableColumn("[bold]Value[/]").Width(ordinal4));
		table.AddColumn(new TableColumn("[bold dim]Description[/]").Width(consoleWidthTerm));
		string path2 = state.GetCurrentTerrain()?.Name ?? "[dim]< None >[/]";
		bool isOnTerrainRow = state.IsOnTerrainRow;
		bool hasPendingTerrainChange = state.HasPendingTerrainChange;
		table.AddRow(FormatAssociationName("Terrain", isOnTerrainRow), FormatAssociationValue(path2, isOnTerrainRow, hasPendingTerrainChange), "[dim]Digital elevation model for this geometry[/]");
		string path3 = state.GetCurrentNValue()?.Name ?? "[dim]< None >[/]";
		bool isOnNValueRow = state.IsOnNValueRow;
		bool hasPendingNValueChange = state.HasPendingNValueChange;
		table.AddRow(FormatAssociationName("Manning's N", isOnNValueRow), FormatAssociationValue(path3, isOnNValueRow, hasPendingNValueChange), "[dim]Roughness layer for hydraulic calculations[/]");
		iRenderables.Add(table);
		Table table2 = new Table();
		table2.Border(TableBorder.Rounded);
		table2.Expand();
		table2.Title = new TableTitle("[bold]Geometry Properties[/] [dim](saved to Geometry file)[/]");
		table2.AddColumn(new TableColumn("[bold]Property[/]").Width(ordinal3));
		table2.AddColumn(new TableColumn("[bold]Value[/]").Width(ordinal4));
		table2.AddColumn(new TableColumn("[bold dim]Description[/]").Width(consoleWidthTerm));
		int ordinal5 = consoleHeightTerm - 2 - 2;
		int count = Math.Min(state.ScrollOffset + ordinal5, state.Properties.Count);
		for (int i = state.ScrollOffset; i < count; i++)
		{
			GeometryPropertyInfo prop = state.Properties[i];
			bool isSelected = i + 2 == state.CursorRow;
			string propertyName = FormatPropertyName(prop, isSelected);
			string formatValue = FormatValue(state, prop, isSelected);
			string description = FormatDescription(prop, consoleWidthTerm);
			table2.AddRow(propertyName, formatValue, description);
		}
		iRenderables.Add(table2);
		string textValue = null;
		if (state.IsOnTerrainRow)
		{
			textValue = "The terrain provides elevation data used to compute cell elevations and property tables.";
		}
		else if (state.IsOnNValueRow)
		{
			textValue = "The Manning's N layer defines surface roughness values for friction calculations.";
		}
		else
		{
			GeometryPropertyInfo selectedProperty = state.SelectedProperty;
			if (selectedProperty != null)
			{
				textValue = selectedProperty.Description;
			}
		}
		if (textValue != null)
		{
			iRenderables.Add(new Rule().RuleStyle("dim"));
			iRenderables.Add(new Markup("[dim]" + EscapeMarkup(textValue) + "[/]"));
		}
		if (!string.IsNullOrEmpty(state.EditError))
		{
			iRenderables.Add(new Markup("[red]" + EscapeMarkup(state.EditError) + "[/]"));
		}
		else if (!string.IsNullOrEmpty(state.ErrorMessage))
		{
			iRenderables.Add(new Markup("[red]" + EscapeMarkup(state.ErrorMessage) + "[/]"));
		}
		else if (!string.IsNullOrEmpty(state.StatusMessage))
		{
			iRenderables.Add(new Markup("[green]" + EscapeMarkup(state.StatusMessage) + "[/]"));
		}
		iRenderables.Add(new Rule().RuleStyle("dim"));
		iRenderables.Add(new Markup(GetFooterText(state)));
		return new Rows(iRenderables);
	}

	private string FormatAssociationName(string name, bool isSelected)
	{
		if (isSelected)
		{
			return "[black on yellow]> " + name + "[/]";
		}
		return "  " + name;
	}

	private string FormatAssociationValue(string textValue, bool isSelected, bool isPending)
	{
		string escapeMarkup = (textValue.Contains("[dim]") ? textValue : EscapeMarkup(textValue));
		if (isSelected)
		{
			return "[black on yellow]" + escapeMarkup + "[/]";
		}
		if (isPending)
		{
			return "[green]" + escapeMarkup + "[/]";
		}
		return escapeMarkup;
	}

	private string FormatPropertyName(GeometryPropertyInfo prop, bool isSelected)
	{
		string escapeMarkup = EscapeMarkup(prop.DisplayName);
		string path = (prop.IsReadOnly ? " [dim](read-only)[/]" : "");
		if (isSelected)
		{
			return "[black on yellow]> " + escapeMarkup + "[/]" + path;
		}
		return "  " + escapeMarkup + path;
	}

	private string FormatValue(GeometryEditorState state, GeometryPropertyInfo prop, bool isSelected)
	{
		bool containsKey = state.PendingChanges.ContainsKey(prop.PropertyName);
		object currentValue = state.GetCurrentValue(prop);
		if (isSelected && state.IsEditingCell)
		{
			string escapeMarkup = EscapeMarkup(state.EditBuffer);
			return "[black on white]" + escapeMarkup + "_[/]";
		}
		string escapeMarkup2 = EscapeMarkup(currentValue?.ToString() ?? "(empty)");
		if (isSelected && !state.IsEditingCell)
		{
			return "[black on yellow]" + escapeMarkup2 + "[/]";
		}
		if (containsKey)
		{
			return "[green]" + escapeMarkup2 + "[/]";
		}
		if (prop.IsReadOnly)
		{
			return "[dim]" + escapeMarkup2 + "[/]";
		}
		return escapeMarkup2;
	}

	private string FormatDescription(GeometryPropertyInfo prop, int maxWidth)
	{
		string description = prop.Description ?? "";
		if (description.Length > maxWidth - 3)
		{
			description = description.Substring(0, maxWidth - 6) + "...";
		}
		return "[dim]" + EscapeMarkup(description) + "[/]";
	}

	private IRenderable RenderSavePrompt(GeometryEditorState state)
	{
		return new Panel(new Markup("[yellow]You have unsaved changes![/]\n\n[[[bold]S[/]]] Save - Overwrite current geometry file\n[[[bold]A[/]]] Save As - Save to a new geometry file\n[[[bold]D[/]]] Discard - Exit without saving\n[[[bold]C[/]]] Cancel - Continue editing"))
		{
			Header = new PanelHeader("[bold]Unsaved Changes[/]"),
			Border = BoxBorder.Rounded,
			Padding = new Padding(2, 1)
		};
	}

	private IRenderable RenderSaveAsPrompt(GeometryEditorState state)
	{
		Markup content = new Markup("Enter new geometry name:\n\n[black on white] " + EscapeMarkup(state.SaveAsName) + "_ [/]\n\n[dim][[Enter]] Confirm  [[Esc]] Cancel[/]");
		if (!string.IsNullOrEmpty(state.ErrorMessage))
		{
			content = new Markup($"Enter new geometry name:\n\n[black on white] {EscapeMarkup(state.SaveAsName)}_ [/]\n\n[red]{EscapeMarkup(state.ErrorMessage)}[/]\n\n" + "[dim][[Enter]] Confirm  [[Esc]] Cancel[/]");
		}
		return new Panel(content)
		{
			Header = new PanelHeader("[bold]Save As[/]"),
			Border = BoxBorder.Rounded,
			Padding = new Padding(2, 1)
		};
	}

	private string GetFooterText(GeometryEditorState state)
	{
		if (state.IsEditingCell)
		{
			return "[cyan][[EDIT]][/] Type value  [dim][[Enter]][/] Accept  [dim][[Esc]][/] Cancel";
		}
		GeometryPropertyInfo selectedProperty = state.SelectedProperty;
		string textValue = "";
		if (selectedProperty != null && !selectedProperty.IsReadOnly)
		{
			textValue = "  [dim][[Enter]][/] Edit";
		}
		string path = (state.HasUnsavedChanges ? "  [dim][[S]][/] Save  [dim][[A]][/] Save As" : "");
		return "[dim][[Up/Down]][/] Navigate" + textValue + path + "  [dim][[Esc]][/] Exit";
	}

	private static string EscapeMarkup(string textValue)
	{
		return textValue?.Replace("[", "[[").Replace("]", "]]") ?? "";
	}
}
