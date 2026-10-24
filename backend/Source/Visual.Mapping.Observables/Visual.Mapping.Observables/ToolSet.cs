using System;
using System.Collections.Generic;
using Utility;
using Utility.DataStructures;

namespace Visual.Mapping.Observables;

public sealed class ToolSet : IDisposable
{
	public delegate void ActiveDescriptorChangedEvent(ToolSet sender, ToolDescriptor oldVal, ToolDescriptor newVal);

	private readonly HashSet<ToolNode> _activateSubscribed = new HashSet<ToolNode>();

	private static readonly IComparer<ToolNode> _zIndexInsertAfterComparer = Comparer<ToolNode>.Create((ToolNode a, ToolNode b) => (a.ZIndex > b.ZIndex) ? 1 : (-1));

	public AdvancedCollection<ToolNode> RootNodes { get; } = new AdvancedCollection<ToolNode>();

	public IEnumerable<ToolLeaf> Leafs
	{
		get
		{
			foreach (ToolNode rootNode in RootNodes)
			{
				if (rootNode is ToolLeaf toolLeaf)
				{
					yield return toolLeaf;
					continue;
				}
				foreach (ToolNode descendant in rootNode.Descendants)
				{
					if (descendant is ToolLeaf toolLeaf2)
					{
						yield return toolLeaf2;
					}
				}
			}
		}
	}

	public IEnumerable<ToolGroup> Groups
	{
		get
		{
			foreach (ToolNode rootNode in RootNodes)
			{
				if (rootNode is ToolGroup toolGroup)
				{
					yield return toolGroup;
					continue;
				}
				foreach (ToolNode descendant in rootNode.Descendants)
				{
					if (descendant is ToolGroup toolGroup2)
					{
						yield return toolGroup2;
					}
				}
			}
		}
	}

	public IEnumerable<ToolNode> Nodes
	{
		get
		{
			foreach (ToolNode node in RootNodes)
			{
				yield return node;
				foreach (ToolNode descendant in node.Descendants)
				{
					yield return descendant;
				}
			}
		}
	}

	public ToolDescriptor ActiveDescriptor
	{
		get;
		private set
		{
			if (!object.Equals(field, value))
			{
				ToolDescriptor oldVal = field;
				field = value;
				ActiveDescriptorChanged?.Invoke(this, oldVal, value);
			}
		}
	}

	public ToolDescriptor FallbackDescriptor { get; set; }

	public event ActiveDescriptorChangedEvent ActiveDescriptorChanged;

	public void AddRoot(ToolNode root)
	{
		ArgumentNullException.ThrowIfNull(root, "root");
		int index = ~MathHelpers.BinarySearch(RootNodes, root, _zIndexInsertAfterComparer);
		RootNodes.Insert(index, root);
		SubscribeActivateRequestedRecursive(root);
		ReevaluateAllAvailability();
		ClearActiveStateRecursive(root);
	}

	public bool TryRemoveRoot(ToolNode root)
	{
		if (root == null)
		{
			return false;
		}
		if (!RootNodes.Remove(root))
		{
			return false;
		}
		UnsubscribeActivateRequestedRecursive(root);
		ReevaluateAllAvailability();
		ApplyFallbackIfActiveCulled();
		return true;
	}

	public bool TryActivate(ToolDescriptor descriptor)
	{
		if (descriptor == null)
		{
			return false;
		}
		if (ActiveDescriptor == descriptor)
		{
			return true;
		}
		ToolLeaf toolLeaf = null;
		foreach (ToolLeaf leaf in Leafs)
		{
			if (leaf.Tool == descriptor)
			{
				toolLeaf = leaf;
				break;
			}
		}
		if (toolLeaf == null)
		{
			return false;
		}
		foreach (ToolLeaf leaf2 in Leafs)
		{
			leaf2.IsActive = leaf2 == toolLeaf;
		}
		foreach (ToolGroup group in Groups)
		{
			if (group.IsActive = group.ContainsDescriptor(descriptor))
			{
				group.DisplayDescriptor = descriptor;
			}
		}
		ActiveDescriptor = descriptor;
		return true;
	}

	private void SubscribeActivateRequestedRecursive(ToolNode node)
	{
		if (node == null)
		{
			return;
		}
		if (_activateSubscribed.Add(node))
		{
			node.ActivateRequested += OnNodeActivateRequested;
		}
		foreach (ToolNode child in node.Children)
		{
			SubscribeActivateRequestedRecursive(child);
		}
	}

	private void UnsubscribeActivateRequestedRecursive(ToolNode node)
	{
		if (node == null)
		{
			return;
		}
		if (_activateSubscribed.Remove(node))
		{
			node.ActivateRequested -= OnNodeActivateRequested;
		}
		foreach (ToolNode child in node.Children)
		{
			UnsubscribeActivateRequestedRecursive(child);
		}
	}

	private void OnNodeActivateRequested(ToolNode node)
	{
		if (!TryActivate(node.FaceDescriptor))
		{
			throw new InvalidOperationException("ToolSet has no leaf for descriptor '" + (node.FaceDescriptor?.Id ?? "<null>") + "'. A node activation request must always resolve to a leaf in this set.");
		}
	}

	private void ApplyFallbackIfActiveCulled()
	{
		if (ActiveDescriptor != null && FallbackDescriptor != null && !LeafExistsFor(ActiveDescriptor))
		{
			TryActivate(FallbackDescriptor);
		}
	}

	private static void ClearActiveStateRecursive(ToolNode subtree)
	{
		if (subtree == null)
		{
			return;
		}
		subtree.IsActive = false;
		foreach (ToolNode descendant in subtree.Descendants)
		{
			descendant.IsActive = false;
		}
	}

	private bool LeafExistsFor(ToolDescriptor descriptor)
	{
		foreach (ToolLeaf leaf in Leafs)
		{
			if (leaf.Tool == descriptor)
			{
				return true;
			}
		}
		return false;
	}

	public ToolNode GetRootNodeFor(ToolDescriptor descriptor)
	{
		if (descriptor == null)
		{
			return null;
		}
		foreach (ToolNode rootNode in RootNodes)
		{
			if (rootNode.ContainsDescriptor(descriptor))
			{
				return rootNode;
			}
		}
		return null;
	}

	public void ReevaluateAllAvailability()
	{
		foreach (ToolLeaf leaf in Leafs)
		{
			leaf.IsAvailable = IsAvailable(leaf, leaf.Tool);
		}
		foreach (ToolGroup group in Groups)
		{
			bool isAvailable = false;
			foreach (ToolNode descendant in group.Descendants)
			{
				if (descendant is ToolLeaf { IsAvailable: not false })
				{
					isAvailable = true;
					break;
				}
			}
			group.IsAvailable = isAvailable;
		}
	}

	public bool IsAvailable(ToolNode node, ToolDescriptor descriptor)
	{
		if (node == null || descriptor == null)
		{
			return false;
		}
		Func<bool> availableWhen = node.AvailableWhen;
		if (availableWhen != null && !availableWhen())
		{
			return false;
		}
		Func<bool> isAvailable = descriptor.IsAvailable;
		if (isAvailable != null && !isAvailable())
		{
			return false;
		}
		return true;
	}

	public void Dispose()
	{
		foreach (ToolNode rootNode in RootNodes)
		{
			UnsubscribeActivateRequestedRecursive(rootNode);
		}
		foreach (ToolNode rootNode2 in RootNodes)
		{
			rootNode2.Dispose();
		}
	}
}
