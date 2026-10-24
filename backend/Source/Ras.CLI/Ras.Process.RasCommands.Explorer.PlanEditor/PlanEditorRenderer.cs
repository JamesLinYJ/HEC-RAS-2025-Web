using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Ras.Process.RasCommands.Explorer.PlanEditor;

public class PlanEditorRenderer
{
	public IRenderable Render(PlanEditorState state, int consoleWidth, int consoleHeight)
	{
		List<IRenderable> iRenderables = new List<IRenderable>();
		string path = (state.HasUnsavedChanges ? " [yellow][[Unsaved *]][/]" : "");
		iRenderables.Add(new Rule("[bold]Plan Editor: " + EscapeMarkup(state.Plan?.Name ?? "Unknown") + "[/]" + path).LeftJustified());
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
		string path2 = state.GetCurrentGeometry()?.Name ?? "[dim]< None >[/]";
		bool isOnGeometryRow = state.IsOnGeometryRow;
		bool hasPendingGeometryChange = state.HasPendingGeometryChange;
		table.AddRow(FormatAssociationName("Geometry", isOnGeometryRow), FormatAssociationValue(path2, isOnGeometryRow, hasPendingGeometryChange), "[dim]Associated geometry for this plan[/]");
		string path3 = state.GetCurrentBoundaryCondition()?.Name ?? "[dim]< None >[/]";
		bool isOnBCRow = state.IsOnBCRow;
		bool hasPendingBCChange = state.HasPendingBCChange;
		table.AddRow(FormatAssociationName("Boundary Condition", isOnBCRow), FormatAssociationValue(path3, isOnBCRow, hasPendingBCChange), "[dim]Associated boundary condition for this plan[/]");
		iRenderables.Add(table);
		Table table2 = new Table();
		table2.Border(TableBorder.Rounded);
		table2.Expand();
		table2.Title = new TableTitle("[bold]Plan Properties[/] [dim](saved to Plan file)[/]");
		table2.AddColumn(new TableColumn("[bold]Property[/]").Width(ordinal3));
		table2.AddColumn(new TableColumn("[bold]Value[/]").Width(ordinal4));
		table2.AddColumn(new TableColumn("[bold dim]Description[/]").Width(consoleWidthTerm));
		int ordinal5 = consoleHeightTerm - 2 - 2;
		int count = Math.Min(state.ScrollOffset + ordinal5, state.Properties.Count);
		for (int i = state.ScrollOffset; i < count; i++)
		{
			PlanPropertyInfo prop = state.Properties[i];
			bool isSelected = i + 2 == state.CursorRow;
			string propertyName = FormatPropertyName(prop, isSelected);
			string formatValue = FormatValue(state, prop, isSelected);
			string description = FormatDescription(prop, consoleWidthTerm);
			table2.AddRow(propertyName, formatValue, description);
		}
		iRenderables.Add(table2);
		string textValue = null;
		if (state.IsOnGeometryRow)
		{
			textValue = "The geometry defines the computational mesh and hydraulic structures for this plan.";
		}
		else if (state.IsOnBCRow)
		{
			textValue = "The boundary condition defines inflows, outflows, and other hydraulic forcing for this plan.";
		}
		else
		{
			PlanPropertyInfo selectedProperty = state.SelectedProperty;
			if (selectedProperty != null)
			{
				textValue = selectedProperty.Description;
				if (selectedProperty.MinValue.HasValue || selectedProperty.MaxValue.HasValue)
				{
					string textValue2 = "";
					if (selectedProperty.MinValue.HasValue && selectedProperty.MaxValue.HasValue)
					{
						textValue2 = $" (Range: {selectedProperty.MinValue} - {selectedProperty.MaxValue})";
					}
					else if (selectedProperty.MinValue.HasValue)
					{
						textValue2 = $" (Min: {selectedProperty.MinValue})";
					}
					else if (selectedProperty.MaxValue.HasValue)
					{
						textValue2 = $" (Max: {selectedProperty.MaxValue})";
					}
					textValue += textValue2;
				}
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

	private string FormatPropertyName(PlanPropertyInfo prop, bool isSelected)
	{
		string escapeMarkup = EscapeMarkup(prop.DisplayName);
		if (isSelected)
		{
			return "[black on yellow]> " + escapeMarkup + "[/]";
		}
		return "  " + escapeMarkup;
	}

	private string FormatValue(PlanEditorState state, PlanPropertyInfo prop, bool isSelected)
	{
		bool containsKey = state.PendingChanges.ContainsKey(prop.PropertyName);
		object currentValue = state.GetCurrentValue(prop);
		if (isSelected && state.IsEditingCell)
		{
			string escapeMarkup = EscapeMarkup(state.EditBuffer);
			return "[black on white]" + escapeMarkup + "_[/]";
		}
		string isEnumPropTerm;
		if (!prop.IsBool)
		{
			isEnumPropTerm = ((!prop.IsEnum) ? EscapeMarkup(PlanValueEditor.FormatValue(currentValue, prop.ValueType)) : ("[cyan][[" + EscapeMarkup(PlanValueEditor.FormatValue(currentValue, prop.ValueType)) + "]][/]"));
		}
		else
		{
			bool currentValue2 = default;
			int unity;
			if (currentValue is bool)
			{
				currentValue2 = (bool)currentValue;
				unity = 1;
			}
			else
			{
				unity = 0;
			}
			isEnumPropTerm = ((((uint)unity & (currentValue2 ? 1u : 0u)) != 0) ? "[green]Yes[/]" : "[red]No[/]");
		}
		if (isSelected && !state.IsEditingCell)
		{
			return "[black on yellow]" + isEnumPropTerm + "[/]";
		}
		if (containsKey)
		{
			return "[green]" + isEnumPropTerm + "[/]";
		}
		return isEnumPropTerm;
	}

	private string FormatDescription(PlanPropertyInfo prop, int maxWidth)
	{
		string description = prop.Description ?? "";
		if (description.Length > maxWidth - 3)
		{
			description = description.Substring(0, maxWidth - 6) + "...";
		}
		return "[dim]" + EscapeMarkup(description) + "[/]";
	}

	private IRenderable RenderSavePrompt(PlanEditorState state)
	{
		return new Panel(new Markup("[yellow]You have unsaved changes![/]\n\n[[[bold]S[/]]] Save - Overwrite current plan file\n[[[bold]A[/]]] Save As - Save to a new plan file\n[[[bold]D[/]]] Discard - Exit without saving\n[[[bold]C[/]]] Cancel - Continue editing"))
		{
			Header = new PanelHeader("[bold]Unsaved Changes[/]"),
			Border = BoxBorder.Rounded,
			Padding = new Padding(2, 1)
		};
	}

	private IRenderable RenderSaveAsPrompt(PlanEditorState state)
	{
		Markup content = new Markup("Enter new plan name:\n\n[black on white] " + EscapeMarkup(state.SaveAsName) + "_ [/]\n\n[dim][[Enter]] Confirm  [[Esc]] Cancel[/]");
		if (!string.IsNullOrEmpty(state.ErrorMessage))
		{
			content = new Markup($"Enter new plan name:\n\n[black on white] {EscapeMarkup(state.SaveAsName)}_ [/]\n\n[red]{EscapeMarkup(state.ErrorMessage)}[/]\n\n" + "[dim][[Enter]] Confirm  [[Esc]] Cancel[/]");
		}
		return new Panel(content)
		{
			Header = new PanelHeader("[bold]Save As[/]"),
			Border = BoxBorder.Rounded,
			Padding = new Padding(2, 1)
		};
	}

	private string GetFooterText(PlanEditorState state)
	{
		if (state.IsEditingCell)
		{
			return "[cyan][[EDIT]][/] Type value  [dim][[Enter]][/] Accept  [dim][[Esc]][/] Cancel";
		}
		PlanPropertyInfo selectedProperty = state.SelectedProperty;
		string textValue = "";
		if (selectedProperty != null)
		{
			if (selectedProperty.IsBool)
			{
				textValue = "  [dim][[Space]][/] Toggle";
			}
			else if (selectedProperty.IsEnum)
			{
				textValue = "  [dim][[Left/Right]][/] Cycle";
			}
		}
		string path = (state.HasUnsavedChanges ? "  [dim][[S]][/] Save  [dim][[A]][/] Save As" : "");
		return "[dim][[Up/Down]][/] Navigate  [dim][[Enter]][/] Edit" + textValue + path + "  [dim][[Esc]][/] Exit";
	}

	private static string EscapeMarkup(string textValue)
	{
		return textValue?.Replace("[", "[[").Replace("]", "]]") ?? "";
	}
}
