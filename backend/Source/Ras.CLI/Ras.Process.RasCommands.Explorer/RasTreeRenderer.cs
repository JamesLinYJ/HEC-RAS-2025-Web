using System;
using System.Collections.Generic;
using System.IO;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Ras.Process.RasCommands.Explorer;

public class RasTreeRenderer
{
	public IRenderable Render(RasExplorerState state, string projectName, int consoleWidth, int consoleHeight)
	{
		int scaledConsoleWidth = (state.ShowInfoPanel ? Math.Min(90, consoleWidth / 2) : 0);
		int consoleWidthTerm = consoleWidth - scaledConsoleWidth - 1;
		IRenderable renderable = RenderTreePanel(state, projectName, consoleWidthTerm, consoleHeight);
		IRenderable renderable2 = (state.ShowInfoPanel ? RenderInfoPanel(state, scaledConsoleWidth, consoleHeight) : null);
		IRenderable iRenderable;
		if (state.ShowInfoPanel && renderable2 != null)
		{
			Grid grid = new Grid();
			grid.AddColumn(new GridColumn().Width(consoleWidthTerm));
			grid.AddColumn(new GridColumn().Width(scaledConsoleWidth));
			grid.AddRow(renderable, renderable2);
			iRenderable = grid;
		}
		else
		{
			iRenderable = renderable;
		}
		List<IRenderable> iRenderables = new List<IRenderable> { iRenderable };
		iRenderables.Add(new Rule().RuleStyle("dim"));
		iRenderables.Add(new Markup("[dim][[Up/Down]][/] Navigate  [dim][[Right/Enter]][/] Expand  [dim][[Left]][/] Collapse  [dim][[a]][/] Info Panel  [dim][[x]][/] Actions" + state.SelectedNode?.Type switch
		{
			RasNodeType.Plan => "  [dim][[e]][/] Edit Plan", 
			RasNodeType.Geometry => "  [dim][[e]][/] Edit Geometry", 
			_ => "", 
		} + "  [dim][[c]][/] Copy  [dim][[Q]][/] Quit"));
		return new Rows(iRenderables);
	}

	private IRenderable RenderTreePanel(RasExplorerState state, string projectName, int width, int consoleHeight)
	{
		List<IRenderable> iRenderables = new List<IRenderable>();
		iRenderables.Add(new Rule("[bold]RAS Explorer: " + EscapeMarkup(projectName) + "[/]").LeftJustified());
		int count = consoleHeight - 6;
		int count2 = Math.Min(state.ScrollOffset + count, state.VisibleNodes.Count);
		for (int i = state.ScrollOffset; i < count2; i++)
		{
			RasTreeNode node = state.VisibleNodes[i];
			bool isSelected = i == state.CursorIndex;
			iRenderables.Add(new Markup(RenderNodeLine(node, isSelected)));
		}
		for (int j = count2 - state.ScrollOffset; j < count; j++)
		{
			iRenderables.Add(new Markup(" "));
		}
		if (!string.IsNullOrEmpty(state.ErrorMessage))
		{
			iRenderables.Add(new Markup("[red]" + EscapeMarkup(state.ErrorMessage) + "[/]"));
		}
		return new Rows(iRenderables);
	}

	private IRenderable RenderInfoPanel(RasExplorerState state, int width, int consoleHeight)
	{
		Table table = new Table();
		table.Border(TableBorder.Rounded);
		table.AddColumn(new TableColumn("[bold]Property[/]"));
		table.AddColumn(new TableColumn("[bold]Value[/]"));
		string focusOnInfoPanelStateTerm = (state.FocusOnInfoPanel ? "[black on yellow]" : "[yellow]");
		table.Title = new TableTitle(focusOnInfoPanelStateTerm + "Properties[/]");
		RasNodeInfoData selectedNodeInfo = state.SelectedNodeInfo;
		if (selectedNodeInfo == null)
		{
			table.AddRow("[dim]Select an item[/]", "");
			return table;
		}
		List<(string, string)> valueTuples = new List<(string, string)>();
		if (!string.IsNullOrEmpty(selectedNodeInfo.Name))
		{
			valueTuples.Add(("Name", selectedNodeInfo.Name));
		}
		if (!string.IsNullOrEmpty(selectedNodeInfo.Filename))
		{
			valueTuples.Add(("File", Path.GetFileName(selectedNodeInfo.Filename)));
		}
		Dictionary<string, string> associations = selectedNodeInfo.Associations;
		if (associations != null && associations.Count > 0)
		{
			foreach (KeyValuePair<string, string> association in selectedNodeInfo.Associations)
			{
				string isNullOrEmptyAssociationTerm = (string.IsNullOrEmpty(association.Value) ? "(none)" : association.Value);
				valueTuples.Add((association.Key, isNullOrEmptyAssociationTerm));
			}
		}
		Dictionary<string, object> properties = selectedNodeInfo.Properties;
		if (properties != null && properties.Count > 0)
		{
			foreach (KeyValuePair<string, object> property in selectedNodeInfo.Properties)
			{
				string propertyValue = FormatPropertyValue(property.Value);
				valueTuples.Add((property.Key, propertyValue));
			}
		}
		if (!string.IsNullOrEmpty(selectedNodeInfo.ExtentInfo))
		{
			valueTuples.Add(("Extent", selectedNodeInfo.ExtentInfo));
		}
		if (valueTuples.Count == 0 && selectedNodeInfo.Table == null && selectedNodeInfo.Preview == null)
		{
			table.AddRow("[dim]No properties[/]", "");
			return table;
		}
		int consoleHeightOffset = consoleHeight - 10;
		int infoPanelScrollOffset = state.InfoPanelScrollOffset;
		int count = Math.Min(infoPanelScrollOffset + consoleHeightOffset, valueTuples.Count);
		if (valueTuples.Count > consoleHeightOffset)
		{
			if (infoPanelScrollOffset > 0)
			{
				table.Caption = new TableTitle($"[dim]↑ {infoPanelScrollOffset} more above[/]");
			}
			int countDifference = valueTuples.Count - count;
			if (countDifference > 0)
			{
				string message = ((infoPanelScrollOffset > 0) ? $"[dim]↑ {infoPanelScrollOffset} above | ↓ {countDifference} below[/]" : $"[dim]↓ {countDifference} more below[/]");
				table.Caption = new TableTitle(message);
			}
		}
		for (int i = infoPanelScrollOffset; i < count; i++)
		{
			(string, string) tuple = valueTuples[i];
			bool focusOnInfoPanelStateTerm2 = state.FocusOnInfoPanel && i == state.SelectedPropertyIndex;
			string escapeMarkup = EscapeMarkup(tuple.Item1);
			string escapeMarkup2 = EscapeMarkup(tuple.Item2);
			if (focusOnInfoPanelStateTerm2)
			{
				table.AddRow("[black on blue]" + escapeMarkup + "[/]", "[black on blue]" + escapeMarkup2 + "[/]");
			}
			else
			{
				table.AddRow(escapeMarkup, escapeMarkup2);
			}
		}
		bool hasTable = selectedNodeInfo.Table != null && selectedNodeInfo.Table.Columns.Count > 0;
		bool hasPreview = selectedNodeInfo.Preview != null && state.InfoPanelMode == InfoPanelMode.PropertiesAndPreview;
		if (hasTable | hasPreview)
		{
			List<IRenderable> iRenderables = new List<IRenderable> { table };
			if (hasTable)
			{
				iRenderables.Add(new Rule().RuleStyle("dim"));
				iRenderables.Add(RenderDataTable(selectedNodeInfo.Table, state, valueTuples.Count));
			}
			if (hasPreview)
			{
				iRenderables.Add(new Rule("[dim]Preview[/]").RuleStyle("dim"));
				Panel padding = HasBoxBorderExtensions.NoBorder(new Panel(selectedNodeInfo.Preview)).Padding(0, 0, 0, 0);
				iRenderables.Add(padding);
			}
			return new Rows(iRenderables);
		}
		return table;
	}

	private IRenderable RenderDataTable(InfoTable infoTable, RasExplorerState state, int baseIndex)
	{
		Table table = new Table();
		table.Border(TableBorder.Simple);
		table.Expand();
		foreach (string column in infoTable.Columns)
		{
			table.AddColumn(new TableColumn("[bold]" + EscapeMarkup(column) + "[/]"));
		}
		int ordinal = 0;
		foreach (List<string> row in infoTable.Rows)
		{
			bool focusOnInfoPanelStateTerm = state.FocusOnInfoPanel && baseIndex + ordinal == state.SelectedPropertyIndex;
			List<string> stringList = new List<string>();
			foreach (string row2 in row)
			{
				string escapeMarkup = EscapeMarkup(row2 ?? "");
				if (focusOnInfoPanelStateTerm)
				{
					stringList.Add("[black on blue]" + escapeMarkup + "[/]");
				}
				else
				{
					stringList.Add(escapeMarkup);
				}
			}
			table.AddRow(stringList.ToArray());
			ordinal++;
		}
		return table;
	}

	private string RenderNodeLine(RasTreeNode node, bool isSelected)
	{
		string depthNodeTerm = new string(' ', node.Depth * 2);
		string expander = GetExpander(node);
		string nodeColor = GetNodeColor(node);
		string message = $"[{nodeColor}]{EscapeMarkup(node.Name)}[/]";
		string textValue = "";
		if (!string.IsNullOrEmpty(node.SuffixMarkup))
		{
			textValue = " " + node.SuffixMarkup;
		}
		else if (!string.IsNullOrEmpty(node.DisplaySuffix))
		{
			textValue = " [dim]" + EscapeMarkup(node.DisplaySuffix) + "[/]";
		}
		string message2 = $"{depthNodeTerm}{expander} {message}{textValue}";
		if (isSelected)
		{
			return "[black on blue]" + message2 + "[/]";
		}
		return message2;
	}

	private static string GetExpander(RasTreeNode node)
	{
		if (node.Type == RasNodeType.Root || node.Type == RasNodeType.Collection || node.Type == RasNodeType.Geometry || node.Type == RasNodeType.Terrain || node.Type == RasNodeType.NValue || node.Type == RasNodeType.Classification || node.Children.Count > 0)
		{
			if (!node.IsExpanded)
			{
				return "[[+]]";
			}
			return "[[-]]";
		}
		return "  >";
	}

	private static string GetNodeColor(RasTreeNode node)
	{
		return node.Type switch
		{
			RasNodeType.Root => "white bold", 
			RasNodeType.Collection => "yellow", 
			RasNodeType.Plan => "cyan", 
			RasNodeType.Geometry => "cyan", 
			RasNodeType.BoundaryCondition => "cyan", 
			RasNodeType.Result => "cyan", 
			RasNodeType.Terrain => "cyan", 
			RasNodeType.NValue => "cyan", 
			RasNodeType.Classification => "cyan", 
			RasNodeType.SubLayer => "green", 
			_ => "white", 
		};
	}

	private static string FormatPropertyValue(object localValue)
	{
		if (localValue == null)
		{
			return "(null)";
		}
		if (!(localValue is DateTime dateTime))
		{
			if (!(localValue is TimeSpan timeSpan))
			{
				if (localValue is bool)
				{
					return ((bool)localValue) ? "Yes" : "No";
				}
				return localValue.ToString();
			}
			return timeSpan.ToString("hh\\:mm\\:ss");
		}
		return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
	}

	private static string EscapeMarkup(string textValue)
	{
		return textValue?.Replace("[", "[[").Replace("]", "]]") ?? "";
	}
}
