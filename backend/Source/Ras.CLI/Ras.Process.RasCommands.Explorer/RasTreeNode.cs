using System.Collections.Generic;

namespace Ras.Process.RasCommands.Explorer;

public class RasTreeNode
{
	public string Name { get; }

	public string FullPath { get; }

	public RasNodeType Type { get; }

	public RasTreeNode Parent { get; }

	public List<RasTreeNode> Children { get; } = new List<RasTreeNode>();

	public bool IsExpanded { get; set; }

	public bool ChildrenLoaded { get; set; }

	public object DataSource { get; set; }

	public string DisplaySuffix { get; set; }

	public string SuffixMarkup { get; set; }

	public int Depth
	{
		get
		{
			if (Parent != null)
			{
				return Parent.Depth + 1;
			}
			return 0;
		}
	}

	public RasTreeNode(string name, string fullPath, RasNodeType type, RasTreeNode parent = null)
	{
		Name = name;
		FullPath = fullPath;
		Type = type;
		Parent = parent;
	}

	public static List<RasTreeNode> GetVisibleNodes(RasTreeNode root)
	{
		List<RasTreeNode> rasTreeNodes = new List<RasTreeNode>();
		CollectVisible(root, rasTreeNodes);
		return rasTreeNodes;
	}

	private static void CollectVisible(RasTreeNode node, List<RasTreeNode> rasTreeNodes)
	{
		rasTreeNodes.Add(node);
		if (!node.IsExpanded)
		{
			return;
		}
		foreach (RasTreeNode child in node.Children)
		{
			CollectVisible(child, rasTreeNodes);
		}
	}
}
