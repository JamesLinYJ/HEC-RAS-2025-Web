using System.Collections.Generic;

namespace Ras.Process.RasterCommands.Explorer;

public class RasterTreeNode
{
	public NodeKind Kind { get; }

	public string Label { get; }

	public string Value { get; set; }

	public bool IsEditable { get; set; }

	public int Depth { get; set; }

	public RasterTreeNode Parent { get; set; }

	public List<RasterTreeNode> Children { get; } = new List<RasterTreeNode>();

	public bool IsExpanded { get; set; }

	public string MetadataKey { get; set; }

	public GerefField Georef { get; set; }

	public SubdatasetEntry Subdataset { get; set; }

	public int TiffTagId { get; set; }

	public int LevelIndex { get; set; } = -1;

	public string EditHint { get; set; }

	public bool ShowsTilePreview { get; set; }

	public string Detail { get; set; }

	public string Description { get; set; }

	public bool IsExpandable
	{
		get
		{
			if (Kind == NodeKind.Section)
			{
				return Children.Count > 0;
			}
			return false;
		}
	}

	public RasterTreeNode(NodeKind kind, string label, string stringValue = "")
	{
		Kind = kind;
		Label = label;
		Value = stringValue;
	}

	public RasterTreeNode AddChild(RasterTreeNode child)
	{
		child.Parent = this;
		child.Depth = Depth + 1;
		Children.Add(child);
		return child;
	}

	public static List<RasterTreeNode> GetVisibleNodes(RasterTreeNode root)
	{
		List<RasterTreeNode> rasterTreeNodes = new List<RasterTreeNode>();
		foreach (RasterTreeNode child in root.Children)
		{
			CollectVisible(child, rasterTreeNodes);
		}
		return rasterTreeNodes;
	}

	private static void CollectVisible(RasterTreeNode node, List<RasterTreeNode> rasterTreeNodes)
	{
		rasterTreeNodes.Add(node);
		if (!node.IsExpanded)
		{
			return;
		}
		foreach (RasterTreeNode child in node.Children)
		{
			CollectVisible(child, rasterTreeNodes);
		}
	}
}
