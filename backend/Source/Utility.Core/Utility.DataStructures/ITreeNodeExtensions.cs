using System;
using System.Collections.Generic;

namespace Utility.DataStructures;

public static class ITreeNodeExtensions
{
	public static IEnumerable<T> RecurseItems<T, K>(this ITreeNode<T, K> root) where K : ITreeNode<T, K>
	{
		yield return root.Item;
		foreach (K child in root.Children)
		{
			foreach (T recurseItem in child.RecurseItems())
			{
				yield return recurseItem;
			}
		}
	}

	public static IEnumerable<TN> Recurse<TN, T>(this TN root) where TN : class, ITreeNode<T, TN>
	{
		yield return root;
		foreach (TN child in root.Children)
		{
			foreach (TN tn in child?.Recurse<TN, T>())
			{
				yield return tn;
			}
		}
	}

	public static IEnumerable<TN> RecurseIf<TN, T>(this TN root, Func<TN, bool> includeNode) where TN : class, ITreeNode<T, TN>
	{
		if (root == null || !includeNode(root))
		{
			yield break;
		}
		yield return root;
		foreach (TN child in root.Children)
		{
			foreach (TN recurseIf in child.RecurseIf<TN, T>(includeNode))
			{
				yield return recurseIf;
			}
		}
	}
}
