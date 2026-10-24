using System;
using System.Collections.Generic;

namespace Utility.DataStructures.Tree;

public class TreeNode<T> : ITreeNode<T, TreeNode<T>>
{
	public T Item { get; set; }

	public TreeNode<T> Parent { get; set; }

	public virtual List<TreeNode<T>> Children { get; } = new List<TreeNode<T>>();

	IList<TreeNode<T>> ITreeNode<T, TreeNode<T>>.Children => Children;

	public TreeNode(TreeNode<T> parent, T localValue)
	{
		Parent = parent;
		Item = localValue;
	}

	public TreeNode(T localValue)
	{
		Item = localValue;
	}

	public static TreeNode<T> Build(T rootElement, Func<T, IEnumerable<T>> getChildrenFromParent)
	{
		Action<TreeNode<T>> AddChildren = (TreeNode<T> tn) =>
		{
			foreach (T getChildrenFromParent2 in getChildrenFromParent(tn.Item))
			{
				tn.Children.Add(new TreeNode<T>(tn, getChildrenFromParent2));
			}
		};
		Action<TreeNode<T>> Build = null;
		Build = (TreeNode<T> tn) =>
		{
			AddChildren(tn);
			foreach (TreeNode<T> child in tn.Children)
			{
				Build(child);
			}
		};
		TreeNode<T> treeNode = new TreeNode<T>(rootElement);
		Build(treeNode);
		return treeNode;
	}
}
