using System;
using System.Collections.Generic;

namespace Ras.Process.H5Commands.Explorer;

public class H5TreeNode
{
	public string Name { get; }

	public string FullPath { get; }

	public NodeType Type { get; }

	public H5TreeNode Parent { get; }

	public List<H5TreeNode> Children { get; } = new List<H5TreeNode>();

	public bool IsExpanded { get; set; }

	public bool ChildrenLoaded { get; set; }

	public int AttributeCount { get; set; }

	public long[] Dimensions { get; set; }

	public Type DataType { get; set; }

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

	public H5TreeNode(string name, string fullPath, NodeType type, H5TreeNode parent = null)
	{
		Name = name;
		FullPath = fullPath;
		Type = type;
		Parent = parent;
	}

	public static List<H5TreeNode> GetVisibleNodes(H5TreeNode root)
	{
		List<H5TreeNode> h5TreeNodes = new List<H5TreeNode>();
		CollectVisible(root, h5TreeNodes);
		return h5TreeNodes;
	}

	private static void CollectVisible(H5TreeNode node, List<H5TreeNode> h5TreeNodes)
	{
		h5TreeNodes.Add(node);
		if (!node.IsExpanded)
		{
			return;
		}
		foreach (H5TreeNode child in node.Children)
		{
			CollectVisible(child, h5TreeNodes);
		}
	}

	public string GetDatasetInfo()
	{
		if (Type != NodeType.Dataset || Dimensions == null)
		{
			return "";
		}
		string dataTypeTerm = DataType?.Name ?? "?";
		string dimensionsTerm = string.Join("x", Dimensions);
		return $"[{dataTypeTerm}, {dimensionsTerm}]";
	}
}
