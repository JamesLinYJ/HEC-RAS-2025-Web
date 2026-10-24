using System.Collections;
using System.Collections.Generic;

namespace RasMapperLib;

public class TreeNodeCollection : IList<TreeNode>
{
	public List<TreeNode> Nodes;

	public TreeNode this[int index]
	{
		get
		{
			return ((IList<TreeNode>)Nodes)[index];
		}
		set
		{
			((IList<TreeNode>)Nodes)[index] = value;
		}
	}

	public int Count => ((ICollection<TreeNode>)Nodes).Count;

	public bool IsReadOnly => ((ICollection<TreeNode>)Nodes).IsReadOnly;

	public TreeNodeCollection()
	{
		Nodes = new List<TreeNode>();
	}

	public void Insert(int index, TreeNode treeNode)
	{
		((IList<TreeNode>)Nodes).Insert(index, treeNode);
	}

	void IList<TreeNode>.Insert(int index, TreeNode treeNode)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Insert
		this.Insert(index, treeNode);
	}

	public void RemoveAt(int index)
	{
		((IList<TreeNode>)Nodes).RemoveAt(index);
	}

	void IList<TreeNode>.RemoveAt(int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in RemoveAt
		this.RemoveAt(index);
	}

	public void Add(TreeNode treeNode)
	{
		((ICollection<TreeNode>)Nodes).Add(treeNode);
	}

	void ICollection<TreeNode>.Add(TreeNode treeNode)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Add
		this.Add(treeNode);
	}

	public void Clear()
	{
		((ICollection<TreeNode>)Nodes).Clear();
	}

	void ICollection<TreeNode>.Clear()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Clear
		this.Clear();
	}

	public void CopyTo(TreeNode[] nodes, int arrayIndex)
	{
		((ICollection<TreeNode>)Nodes).CopyTo(nodes, arrayIndex);
	}

	void ICollection<TreeNode>.CopyTo(TreeNode[] treeNodes, int arrayIndex)
	{
		//ILSpy generated this explicit interface implementation from .override directive in CopyTo
		this.CopyTo(treeNodes, arrayIndex);
	}

	public int IndexOf(TreeNode treeNode)
	{
		return ((IList<TreeNode>)Nodes).IndexOf(treeNode);
	}

	int IList<TreeNode>.IndexOf(TreeNode treeNode)
	{
		//ILSpy generated this explicit interface implementation from .override directive in IndexOf
		return this.IndexOf(treeNode);
	}

	public bool Contains(TreeNode treeNode)
	{
		return ((ICollection<TreeNode>)Nodes).Contains(treeNode);
	}

	bool ICollection<TreeNode>.Contains(TreeNode treeNode)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Contains
		return this.Contains(treeNode);
	}

	public bool Remove(TreeNode treeNode)
	{
		return ((ICollection<TreeNode>)Nodes).Remove(treeNode);
	}

	bool ICollection<TreeNode>.Remove(TreeNode treeNode)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Remove
		return this.Remove(treeNode);
	}

	public IEnumerator<TreeNode> GetEnumerator()
	{
		return ((IEnumerable<TreeNode>)Nodes).GetEnumerator();
	}

	IEnumerator<TreeNode> IEnumerable<TreeNode>.GetEnumerator()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetEnumerator
		return this.GetEnumerator();
	}

	private IEnumerator IEnumerable_GetEnumerator()
	{
		return ((IEnumerable)Nodes).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		//ILSpy generated this explicit interface implementation from .override directive in IEnumerable_GetEnumerator
		return this.IEnumerable_GetEnumerator();
	}

	public void AddRange(IList<TreeNode> tns)
	{
		foreach (TreeNode tn in tns)
		{
			Add(tn);
		}
	}

	public TreeNode[] Find(string stringValue, bool recurse)
	{
		return null;
	}

	public bool ContainsKey(TreeNode key)
	{
		return false;
	}

	public bool ContainsKey(string key)
	{
		return false;
	}

	public void RemoveByKey(TreeNode key)
	{
	}

	public void RemoveByKey(string key)
	{
	}
}
