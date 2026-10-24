using System;
using System.Collections.Generic;
using System.IO;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Ras.Process.H5Commands.Explorer;

public class TreeRenderer
{
	public IRenderable Render(ExplorerState state, string filename, int consoleWidth, int consoleHeight)
	{
		int scaledConsoleWidth = (state.ShowInfoPanel ? Math.Min(90, consoleWidth / 2) : 0);
		int consoleWidthTerm = consoleWidth - scaledConsoleWidth - 1;
		IRenderable renderable = RenderTreePanel(state, filename, consoleWidthTerm, consoleHeight);
		IRenderable renderable2 = RenderInfoPanel(state, scaledConsoleWidth, consoleHeight);
		IRenderable iRenderable;
		if (state.ShowInfoPanel)
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
		if (state.ShowingDeletePrompt)
		{
			iRenderables.Add(new Markup("[yellow]" + EscapeMarkup(state.DeletePromptMessage) + "[/]"));
			iRenderables.Add(new Markup("[[[bold]Y[/]]]es, delete  [[[bold]N[/]]]o, cancel"));
		}
		else
		{
			iRenderables.Add(new Markup("[dim][[Up/Down]][/] Navigate  [dim][[Right/Enter]][/] Expand/Open  [dim][[Left]][/] Collapse  [dim][[a]][/] Attributes  [dim][[c]][/] Copy  [dim][[d]][/] Delete  [dim][[Q]][/] Quit"));
		}
		return new Rows(iRenderables);
	}

	private IRenderable RenderTreePanel(ExplorerState state, string filename, int width, int consoleHeight)
	{
		List<IRenderable> iRenderables = new List<IRenderable>();
		iRenderables.Add(new Rule("[bold]H5 Explorer: " + Path.GetFileName(filename) + "[/]").LeftJustified());
		int count = consoleHeight - 6;
		int count2 = Math.Min(state.ScrollOffset + count, state.VisibleNodes.Count);
		for (int i = state.ScrollOffset; i < count2; i++)
		{
			H5TreeNode node = state.VisibleNodes[i];
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

	private IRenderable RenderInfoPanel(ExplorerState state, int width, int consoleHeight)
	{
		List<IRenderable> iRenderables = new List<IRenderable>();
		iRenderables.Add(RenderAttributesTable(state));
		H5TreeNode selectedNode = state.SelectedNode;
		if (selectedNode != null && selectedNode.Type == NodeType.Dataset)
		{
			iRenderables.Add(RenderDatasetPropertiesTable(state));
		}
		return new Rows(iRenderables);
	}

	private IRenderable RenderAttributesTable(ExplorerState state)
	{
		Table table = new Table();
		table.Border(TableBorder.Rounded);
		table.AddColumn(new TableColumn("[bold]Attribute[/]"));
		table.AddColumn(new TableColumn("[bold]Value[/]"));
		string focusOnInfoPanelStateTerm = (state.FocusOnInfoPanel ? "[black on yellow]" : "[yellow]");
		table.Title = new TableTitle(focusOnInfoPanelStateTerm + "Attributes[/]");
		NodeInfoData selectedNodeInfo = state.SelectedNodeInfo;
		if (selectedNodeInfo?.Attributes != null && selectedNodeInfo.Attributes.Count > 0)
		{
			int ordinal = 0;
			foreach (KeyValuePair<string, object> attribute in selectedNodeInfo.Attributes)
			{
				bool focusOnInfoPanelStateTerm2 = state.FocusOnInfoPanel && ordinal == state.SelectedAttributeIndex;
				string escapeMarkup = EscapeMarkup(attribute.Key);
				string escapeMarkup2 = EscapeMarkup(FormatAttributeValue(attribute.Value));
				if (focusOnInfoPanelStateTerm2)
				{
					table.AddRow("[black on blue]" + escapeMarkup + "[/]", "[black on blue]" + escapeMarkup2 + "[/]");
				}
				else
				{
					table.AddRow(escapeMarkup, escapeMarkup2);
				}
				ordinal++;
			}
		}
		else
		{
			table.AddRow("[dim]No attributes[/]", "");
		}
		return table;
	}

	private IRenderable RenderDatasetPropertiesTable(ExplorerState state)
	{
		Table table = new Table();
		table.Border(TableBorder.Rounded);
		table.AddColumn(new TableColumn("[bold]Property[/]"));
		table.AddColumn(new TableColumn("[bold]Value[/]"));
		table.Title = new TableTitle("[cyan]Dataset Properties[/]");
		DatasetProperties datasetProperties = state.SelectedNodeInfo?.DatasetInfo;
		if (datasetProperties != null)
		{
			table.AddRow("Shape", FormatDimensions(datasetProperties.Dimensions));
			table.AddRow("Type", datasetProperties.ElementType?.Name ?? "?");
			long[] chunkDimensions = datasetProperties.ChunkDimensions;
			if (chunkDimensions != null && chunkDimensions.Length != 0)
			{
				table.AddRow("Chunks", FormatDimensions(datasetProperties.ChunkDimensions));
			}
			else
			{
				table.AddRow("Chunks", "[dim]None (contiguous)[/]");
			}
			table.AddRow("Compressed", FormatBytes(datasetProperties.CompressedSize));
			table.AddRow("Uncompressed", FormatBytes(datasetProperties.UncompressedSize));
			if (datasetProperties.CompressedSize > 0 && datasetProperties.UncompressedSize > 0)
			{
				double compressedSizeOverUncompressedSize = (double)datasetProperties.CompressedSize / (double)datasetProperties.UncompressedSize;
				table.AddRow("Ratio", $"{compressedSizeOverUncompressedSize:F2}x");
			}
			if (datasetProperties.IsVariableLength)
			{
				table.AddRow("Variable Length", "Yes");
			}
		}
		else
		{
			table.AddRow("[dim]Select a dataset[/]", "");
		}
		return table;
	}

	private string RenderNodeLine(H5TreeNode node, bool isSelected)
	{
		string depthNodeTerm = new string(' ', node.Depth * 2);
		string textValue;
		if (node.Type != NodeType.Group)
		{
			textValue = "  >  ";
		}
		else
		{
			textValue = (node.IsExpanded ? "[[-]]" : "[[+]]");
		}
		string nodeNodeTypeTerm = ((node.Type == NodeType.Group) ? "yellow" : "cyan");
		string message = $"[{nodeNodeTypeTerm}]{EscapeMarkup(node.Name)}[/]";
		string messageTerm = depthNodeTerm + textValue + " " + message;
		if (isSelected)
		{
			return "[black on blue]" + messageTerm + "[/]";
		}
		return messageTerm;
	}

	private static string FormatDimensions(long[] dims)
	{
		if (dims == null || dims.Length == 0)
		{
			return "?";
		}
		return string.Join(" x ", dims);
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes <= 0)
		{
			return "[dim]N/A[/]";
		}
		if (bytes >= 1024)
		{
			if (bytes >= 1048576)
			{
				return $"{(double)bytes / 1048576.0:F1} MB";
			}
			return $"{(double)bytes / 1024.0:F1} KB";
		}
		return $"{bytes} B";
	}

	private static string FormatAttributeValue(object localValue, int maxLength = 30)
	{
		if (localValue == null)
		{
			return "[dim]null[/]";
		}
		if (localValue is Array values)
		{
			if (values.Length == 0)
			{
				return "[]";
			}
			if (values.Length <= 3)
			{
				List<string> stringList = new List<string>();
				foreach (object localValue2 in values)
				{
					stringList.Add(Truncate(localValue2?.ToString() ?? "null", maxLength));
				}
				return "[" + string.Join(", ", stringList) + "]";
			}
			return $"{values.GetType().GetElementType()?.Name}[{values.Length}]";
		}
		return Truncate(localValue.ToString() ?? "", maxLength);
	}

	private static string Truncate(string textValue, int maxLength)
	{
		if (string.IsNullOrEmpty(textValue) || textValue.Length <= maxLength)
		{
			return textValue;
		}
		return textValue.Substring(0, maxLength - 3) + "...";
	}

	private static string EscapeMarkup(string textValue)
	{
		return textValue?.Replace("[", "[[").Replace("]", "]]") ?? "";
	}
}
