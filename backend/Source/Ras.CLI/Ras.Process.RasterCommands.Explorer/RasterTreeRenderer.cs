using System;
using System.Collections.Generic;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Ras.Process.RasterCommands.Explorer;

public class RasterTreeRenderer
{
	public IRenderable Render(RasterExplorerState state, string title, string driverLabel, int consoleWidth, int consoleHeight)
	{
		int scaledConsoleWidth = (state.ShowInfoPanel ? Math.Min(70, consoleWidth / 2) : 0);
		int consoleWidthTerm = consoleWidth - scaledConsoleWidth - 1;
		IRenderable renderable = RenderTreePanel(state, title, driverLabel, consoleWidthTerm, consoleHeight);
		IRenderable iRenderable;
		if (state.ShowInfoPanel)
		{
			IRenderable renderable2 = RenderInfoPanel(state);
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
		List<IRenderable> iRenderables = new List<IRenderable>
		{
			iRenderable,
			new Rule().RuleStyle("dim")
		};
		if (state.IsEditing)
		{
			iRenderables.Add(new Markup($"[yellow]Editing[/] {EscapeMarkup(state.SelectedNode?.Label)}: [white]{EscapeMarkup(state.EditBuffer)}[/]█"));
			if (!string.IsNullOrEmpty(state.EditHint))
			{
				iRenderables.Add(new Markup("[dim]Options: " + EscapeMarkup(state.EditHint) + "[/]"));
			}
			if (!string.IsNullOrEmpty(state.EditError))
			{
				iRenderables.Add(new Markup("[red]" + EscapeMarkup(state.EditError) + "[/]"));
			}
			else
			{
				iRenderables.Add(new Markup("[dim][[Enter]][/] Save  [dim][[Esc]][/] Cancel"));
			}
		}
		else
		{
			iRenderables.Add(new Markup("[dim][[Up/Down]][/] Navigate  [dim][[Right/Enter]][/] Expand/Edit  [dim][[Left]][/] Collapse  [dim][[Backspace]][/] Back  [dim][[a]][/] Info  [dim][[c]][/] Copy  [dim][[Q]][/] Quit"));
			if (!string.IsNullOrEmpty(state.ErrorMessage))
			{
				iRenderables.Add(new Markup("[red]" + EscapeMarkup(state.ErrorMessage) + "[/]"));
			}
			else if (!string.IsNullOrEmpty(state.StatusMessage))
			{
				iRenderables.Add(new Markup("[green]" + EscapeMarkup(state.StatusMessage) + "[/]"));
			}
		}
		return new Rows(iRenderables);
	}

	private IRenderable RenderTreePanel(RasterExplorerState state, string title, string driverLabel, int width, int consoleHeight)
	{
		List<IRenderable> iRenderables = new List<IRenderable> { new Rule($"[bold]Raster Explorer: {EscapeMarkup(title)}[/] [dim]({EscapeMarkup(driverLabel)})[/]").LeftJustified() };
		int count = consoleHeight - 6;
		int count2 = Math.Min(state.ScrollOffset + count, state.VisibleNodes.Count);
		for (int i = state.ScrollOffset; i < count2; i++)
		{
			RasterTreeNode node = state.VisibleNodes[i];
			bool isSelected = i == state.CursorIndex;
			iRenderables.Add(new Markup(RenderNodeLine(state, node, isSelected)));
		}
		for (int j = count2 - state.ScrollOffset; j < count; j++)
		{
			iRenderables.Add(new Markup(" "));
		}
		return new Rows(iRenderables);
	}

	private string RenderNodeLine(RasterExplorerState state, RasterTreeNode node, bool isSelected)
	{
		string depthNodeTerm = new string(' ', node.Depth * 2);
		string result;
		if (node.Kind == NodeKind.Section)
		{
			string isExpandedNodeTerm;
			if (node.Children.Count != 0)
			{
				isExpandedNodeTerm = (node.IsExpanded ? "[[-]]" : "[[+]]");
			}
			else
			{
				isExpandedNodeTerm = "   ";
			}
			result = $"{depthNodeTerm}{isExpandedNodeTerm} [yellow]{EscapeMarkup(node.Label)}[/]";
		}
		else if (node.Kind == NodeKind.TiffTag)
		{
			result = depthNodeTerm + "  [grey70]" + EscapeMarkup(node.Label) + "[/]";
		}
		else
		{
			bool isEditingWithIsSelected = state.IsEditing & isSelected;
			string escapeMarkup = (isEditingWithIsSelected ? (state.EditBuffer + "█") : EscapeMarkup(node.Value));
			string path = ((node.IsEditable && !isEditingWithIsSelected) ? " [dim](edit)[/]" : "");
			string textValue = (isEditingWithIsSelected ? "white" : "cyan");
			result = ((string.IsNullOrEmpty(node.Value) && !isEditingWithIsSelected) ? (depthNodeTerm + "  [grey70]" + EscapeMarkup(node.Label) + "[/]") : $"{depthNodeTerm}  [grey70]{EscapeMarkup(node.Label)}[/]: [{textValue}]{escapeMarkup}[/]{path}");
		}
		if (isSelected && !state.IsEditing)
		{
			return "[black on blue]" + StripMarkup(node, depthNodeTerm) + "[/]";
		}
		return result;
	}

	private static string StripMarkup(RasterTreeNode node, string indent)
	{
		if (node.Kind == NodeKind.Section)
		{
			string isExpandedNodeTerm;
			if (node.Children.Count != 0)
			{
				isExpandedNodeTerm = (node.IsExpanded ? "[-]" : "[+]");
			}
			else
			{
				isExpandedNodeTerm = "   ";
			}
			return EscapeMarkup(indent + isExpandedNodeTerm + " " + node.Label);
		}
		if (node.Kind == NodeKind.TiffTag)
		{
			return EscapeMarkup(indent + "  " + node.Label);
		}
		string message = (string.IsNullOrEmpty(node.Value) ? (indent + "  " + node.Label) : $"{indent}  {node.Label}: {node.Value}");
		if (node.IsEditable)
		{
			message += " (edit)";
		}
		return EscapeMarkup(message);
	}

	private IRenderable RenderInfoPanel(RasterExplorerState state)
	{
		RasterTreeNode selectedNode = state.SelectedNode;
		if (selectedNode != null && selectedNode.ShowsTilePreview)
		{
			return RenderTilePreview(state.TilePreview, state.TilePreviewError);
		}
		if (selectedNode != null && selectedNode.Kind == NodeKind.TiffTag)
		{
			return RenderTagPanels(selectedNode.Parent, selectedNode);
		}
		if (selectedNode != null && selectedNode.Kind == NodeKind.Section && HasTiffTagChildren(selectedNode))
		{
			return RenderTagPanels(selectedNode, null);
		}
		Table table = new Table().Border(TableBorder.Rounded);
		table.AddColumn(new TableColumn("[bold]Detail[/]"));
		table.Title = new TableTitle("[cyan]Selection[/]");
		if (selectedNode == null)
		{
			table.AddRow("[dim]Nothing selected[/]");
			return table;
		}
		table.AddRow("[yellow]" + EscapeMarkup(selectedNode.Label) + "[/]");
		if (!string.IsNullOrEmpty(selectedNode.Value))
		{
			table.AddRow(EscapeMarkup(selectedNode.Value));
		}
		if (!string.IsNullOrEmpty(selectedNode.Detail))
		{
			table.AddRow(EscapeMarkup(selectedNode.Detail));
		}
		if (selectedNode.IsEditable)
		{
			table.AddRow("[dim]Editable - press Enter[/]");
		}
		if (selectedNode.Kind == NodeKind.SubdatasetItem)
		{
			table.AddRow("[dim]Press Enter to open[/]");
		}
		return table;
	}

	private IRenderable RenderTilePreview(TilePreview p, string error)
	{
		Table table = new Table().Border(TableBorder.Rounded);
		table.AddColumn(new TableColumn("[bold]Image Preview[/]"));
		table.Title = new TableTitle("[cyan]Tile Coverage[/]");
		if (p == null)
		{
			table.AddRow("[dim]" + EscapeMarkup(error ?? "No preview available.") + "[/]");
			return table;
		}
		for (int i = 0; i < p.Rows; i++)
		{
			StringBuilder stringBuilder = new StringBuilder();
			for (int j = 0; j < p.Cols; j++)
			{
				char shadeFor = ShadeFor(p.DataFractions[i * p.Cols + j]);
				stringBuilder.Append(shadeFor);
				stringBuilder.Append(shadeFor);
			}
			table.AddRow("[white]" + EscapeMarkup(stringBuilder.ToString()) + "[/]");
		}
		table.AddEmptyRow();
		string message = (p.Sampled ? $"{p.Rows}x{p.Cols} sample of {p.TilesTall}x{p.TilesWide} tiles" : $"{p.TilesTall}x{p.TilesWide} tiles");
		table.AddRow("[grey70]" + EscapeMarkup(message) + "[/]");
		table.AddRow("[grey70]Data:[/] · 0%  ░ 25%  ▒ 50%  ▓ 75%  █ 100%");
		if (!p.HasNoData)
		{
			table.AddRow("[dim]No NoData value; all cells shown as full.[/]");
		}
		return table;
	}

	private static char ShadeFor(double fraction)
	{
		if (fraction < 0.0)
		{
			return '?';
		}
		if (fraction <= 0.001)
		{
			return '·';
		}
		if (fraction <= 0.375)
		{
			return '░';
		}
		if (fraction <= 0.625)
		{
			return '▒';
		}
		if (fraction <= 0.875)
		{
			return '▓';
		}
		return '█';
	}

	private static bool HasTiffTagChildren(RasterTreeNode node)
	{
		if (node.Children.Count > 0)
		{
			return node.Children[0].Kind == NodeKind.TiffTag;
		}
		return false;
	}

	private IRenderable RenderTagPanels(RasterTreeNode level, RasterTreeNode selected)
	{
		Table table = new Table().Border(TableBorder.Rounded).Expand();
		table.AddColumn(new TableColumn("[bold]Tag[/]"));
		table.AddColumn(new TableColumn("[bold]Value[/]"));
		table.Title = new TableTitle("[cyan]TIFF Tags - " + EscapeMarkup(level.Label) + "[/]");
		foreach (RasterTreeNode child in level.Children)
		{
			if (child.Kind == NodeKind.TiffTag)
			{
				string escapeMarkup = EscapeMarkup(child.Label);
				string escapeMarkup2 = EscapeMarkup(child.Value);
				if (child == selected)
				{
					table.AddRow("[black on blue]" + escapeMarkup + "[/]", "[black on blue]" + escapeMarkup2 + "[/]");
				}
				else
				{
					table.AddRow(escapeMarkup, escapeMarkup2);
				}
			}
		}
		List<IRenderable> iRenderables = new List<IRenderable> { table };
		if (selected != null)
		{
			Table table2 = new Table().Border(TableBorder.Rounded).Expand();
			table2.AddColumn(new TableColumn("[bold]Tag Detail[/]"));
			table2.Title = new TableTitle("[yellow]" + EscapeMarkup(selected.Label) + "[/]");
			if (!string.IsNullOrEmpty(selected.Description))
			{
				table2.AddRow("[grey70]Description:[/] " + EscapeMarkup(selected.Description));
			}
			string detail = ((!string.IsNullOrEmpty(selected.Detail)) ? selected.Detail : selected.Value);
			table2.AddRow("[grey70]Value:[/] " + EscapeMarkup(string.IsNullOrEmpty(detail) ? "(empty)" : detail));
			if (selected.IsEditable)
			{
				table2.AddRow("[dim]Editable - press Enter[/]");
			}
			iRenderables.Add(table2);
		}
		else
		{
			iRenderables.Add(new Markup("[dim]Select a tag to see its full value.[/]"));
		}
		return new Rows(iRenderables);
	}

	private static string EscapeMarkup(string textValue)
	{
		return textValue?.Replace("[", "[[").Replace("]", "]]") ?? "";
	}
}
