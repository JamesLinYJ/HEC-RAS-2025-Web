using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Utility.DataStructures;

public class AvlTreeSet<T> : IEnumerable<T>, IEnumerable, ICollection<T>, ICollection, IReadOnlyCollection<T>
{
	private IComparer<T> _comparer;

	private AvlNode<T> _root;

	protected int _count;

	private object _syncRoot;

	private readonly EnumerableWrapper<AvlNode<T>> _enumerableWrapper;

	private readonly EnumerableWrapper<AvlNode<T>> _enumerableWrapperReverse;

	public string Name { get; protected set; }

	public AvlNode<T> Root => _root;

	public int Count => _count;

	public object SyncRoot
	{
		get
		{
			if (_syncRoot == null)
			{
				Interlocked.CompareExchange(ref _syncRoot, new object(), null);
			}
			return _syncRoot;
		}
	}

	public bool IsSynchronized => true;

	public bool IsReadOnly => false;

	public AvlTreeSet(IComparer<T> comparer)
	{
		if (comparer == null)
		{
			throw new ArgumentNullException("comparer can't be null");
		}
		_comparer = comparer;
		_enumerableWrapper = new EnumerableWrapper<AvlNode<T>>(() => new AvlNodeEnumerator<T>(this));
		_enumerableWrapperReverse = new EnumerableWrapper<AvlNode<T>>(() => new AvlNodeEnumeratorReverse<T>(this));
	}

	public AvlTreeSet()
		: this((IComparer<T>)Comparer<T>.Default)
	{
	}

	public IEnumerator<T> GetEnumerator()
	{
		return new AvlNodeItemEnumerator<T>(this);
	}

	public IEnumerable<AvlNode<T>> Nodes()
	{
		return _enumerableWrapper;
	}

	public IEnumerable<AvlNode<T>> NodesReverse()
	{
		return _enumerableWrapperReverse;
	}

	public AvlNode<T> GetNode(T localValue)
	{
		AvlNode<T> avlNode = _root;
		while (avlNode != null)
		{
			int compare = _comparer.Compare(localValue, avlNode.Item);
			if (compare < 0)
			{
				avlNode = avlNode.Left;
				continue;
			}
			if (compare > 0)
			{
				avlNode = avlNode.Right;
				continue;
			}
			return avlNode;
		}
		return null;
	}

	public bool Contains(T localValue)
	{
		return GetNode(localValue) != null;
	}

	public virtual AvlNode<T> Add(T localValue)
	{
		AvlNode<T> avlNode = _root;
		while (avlNode != null)
		{
			int compare = _comparer.Compare(localValue, avlNode.Item);
			if (compare < 0)
			{
				AvlNode<T> left = avlNode.Left;
				if (left == null)
				{
					AvlNode<T> result = (avlNode.Left = new AvlNode<T>
					{
						Item = localValue,
						Parent = avlNode
					});
					AddBalance(avlNode, 1);
					return result;
				}
				avlNode = left;
				continue;
			}
			if (compare > 0)
			{
				AvlNode<T> right = avlNode.Right;
				if (right == null)
				{
					AvlNode<T> result2 = (avlNode.Right = new AvlNode<T>
					{
						Item = localValue,
						Parent = avlNode
					});
					AddBalance(avlNode, -1);
					return result2;
				}
				avlNode = right;
				continue;
			}
			return null;
		}
		_root = new AvlNode<T>
		{
			Item = localValue
		};
		_count++;
		return _root;
	}

	public virtual AvlNode<T> AddOrUpdate(T localValue)
	{
		AvlNode<T> avlNode = _root;
		while (avlNode != null)
		{
			int compare = _comparer.Compare(localValue, avlNode.Item);
			if (compare < 0)
			{
				AvlNode<T> left = avlNode.Left;
				if (left == null)
				{
					AvlNode<T> result = (avlNode.Left = new AvlNode<T>
					{
						Item = localValue,
						Parent = avlNode
					});
					AddBalance(avlNode, 1);
					return result;
				}
				avlNode = left;
				continue;
			}
			if (compare > 0)
			{
				AvlNode<T> right = avlNode.Right;
				if (right == null)
				{
					AvlNode<T> result2 = (avlNode.Right = new AvlNode<T>
					{
						Item = localValue,
						Parent = avlNode
					});
					AddBalance(avlNode, -1);
					return result2;
				}
				avlNode = right;
				continue;
			}
			avlNode.Item = localValue;
			return avlNode;
		}
		_root = new AvlNode<T>
		{
			Item = localValue
		};
		_count++;
		return _root;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected void AddBalance(AvlNode<T> node, int balance)
	{
		_count++;
		while (node != null)
		{
			balance = (node.Balance += balance);
			switch (balance)
			{
			case 2:
				if (node.Left.Balance == 1)
				{
					RotateRight(node);
				}
				else
				{
					RotateLeftRight(node);
				}
				return;
			case -2:
				if (node.Right.Balance == -1)
				{
					RotateLeft(node);
				}
				else
				{
					RotateRightLeft(node);
				}
				return;
			case 0:
				return;
			}
			AvlNode<T> parent = node.Parent;
			if (parent != null)
			{
				balance = ((parent.Left == node) ? 1 : (-1));
			}
			node = parent;
		}
	}

	protected AvlNode<T> RotateLeft(AvlNode<T> node)
	{
		AvlNode<T> right = node.Right;
		AvlNode<T> left = right.Left;
		AvlNode<T> avlNode = (right.Parent = node.Parent);
		right.Left = node;
		node.Right = left;
		node.Parent = right;
		if (left != null)
		{
			left.Parent = node;
		}
		if (node == _root)
		{
			_root = right;
		}
		else if (avlNode.Right == node)
		{
			avlNode.Right = right;
		}
		else
		{
			avlNode.Left = right;
		}
		right.Balance++;
		node.Balance = -right.Balance;
		return right;
	}

	protected AvlNode<T> RotateRight(AvlNode<T> node)
	{
		AvlNode<T> left = node.Left;
		AvlNode<T> right = left.Right;
		AvlNode<T> avlNode = (left.Parent = node.Parent);
		left.Right = node;
		node.Left = right;
		node.Parent = left;
		if (right != null)
		{
			right.Parent = node;
		}
		if (node == _root)
		{
			_root = left;
		}
		else if (avlNode.Left == node)
		{
			avlNode.Left = left;
		}
		else
		{
			avlNode.Right = left;
		}
		left.Balance--;
		node.Balance = -left.Balance;
		return left;
	}

	protected AvlNode<T> RotateLeftRight(AvlNode<T> node)
	{
		AvlNode<T> left = node.Left;
		AvlNode<T> right = left.Right;
		AvlNode<T> parent = node.Parent;
		AvlNode<T> right2 = right.Right;
		AvlNode<T> left2 = right.Left;
		right.Parent = parent;
		node.Left = right2;
		left.Right = left2;
		right.Left = left;
		right.Right = node;
		left.Parent = right;
		node.Parent = right;
		if (right2 != null)
		{
			right2.Parent = node;
		}
		if (left2 != null)
		{
			left2.Parent = left;
		}
		if (node == _root)
		{
			_root = right;
		}
		else if (parent.Left == node)
		{
			parent.Left = right;
		}
		else
		{
			parent.Right = right;
		}
		if (right.Balance == -1)
		{
			node.Balance = 0;
			left.Balance = 1;
		}
		else if (right.Balance == 0)
		{
			node.Balance = 0;
			left.Balance = 0;
		}
		else
		{
			node.Balance = -1;
			left.Balance = 0;
		}
		right.Balance = 0;
		return right;
	}

	protected AvlNode<T> RotateRightLeft(AvlNode<T> node)
	{
		AvlNode<T> right = node.Right;
		AvlNode<T> left = right.Left;
		AvlNode<T> parent = node.Parent;
		AvlNode<T> left2 = left.Left;
		AvlNode<T> right2 = left.Right;
		left.Parent = parent;
		node.Right = left2;
		right.Left = right2;
		left.Right = right;
		left.Left = node;
		right.Parent = left;
		node.Parent = left;
		if (left2 != null)
		{
			left2.Parent = node;
		}
		if (right2 != null)
		{
			right2.Parent = right;
		}
		if (node == _root)
		{
			_root = left;
		}
		else if (parent.Right == node)
		{
			parent.Right = left;
		}
		else
		{
			parent.Left = left;
		}
		if (left.Balance == 1)
		{
			node.Balance = 0;
			right.Balance = -1;
		}
		else if (left.Balance == 0)
		{
			node.Balance = 0;
			right.Balance = 0;
		}
		else
		{
			node.Balance = 1;
			right.Balance = 0;
		}
		left.Balance = 0;
		return left;
	}

	public virtual bool Remove(T localValue)
	{
		AvlNode<T> avlNode = _root;
		while (avlNode != null)
		{
			if (_comparer.Compare(localValue, avlNode.Item) < 0)
			{
				avlNode = avlNode.Left;
				continue;
			}
			if (_comparer.Compare(localValue, avlNode.Item) > 0)
			{
				avlNode = avlNode.Right;
				continue;
			}
			RemoveNode(avlNode);
			return true;
		}
		return false;
	}

	public void RemoveNode(AvlNode<T> node)
	{
		_count--;
		AvlNode<T> left = node.Left;
		AvlNode<T> right = node.Right;
		if (left == null)
		{
			if (right == null)
			{
				if (node == _root)
				{
					_root = null;
				}
				else if (node.Parent.Left == node)
				{
					node.Parent.Left = null;
					RemoveBalance(node.Parent, -1);
				}
				else if (node.Parent.Right == node)
				{
					node.Parent.Right = null;
					RemoveBalance(node.Parent, 1);
				}
				else
				{
					DumpVisual2();
				}
			}
			else
			{
				Replace(node, right);
				RemoveBalance(node, 0);
			}
			return;
		}
		if (right == null)
		{
			Replace(node, left);
			RemoveBalance(node, 0);
			return;
		}
		AvlNode<T> avlNode = right;
		if (avlNode.Left == null)
		{
			AvlNode<T> avlNode2 = (avlNode.Parent = node.Parent);
			avlNode.Left = left;
			avlNode.Balance = node.Balance;
			left.Parent = avlNode;
			if (node == _root)
			{
				_root = avlNode;
			}
			else if (avlNode2.Left == node)
			{
				avlNode2.Left = avlNode;
			}
			else
			{
				avlNode2.Right = avlNode;
			}
			RemoveBalance(avlNode, 1);
			return;
		}
		while (avlNode.Left != null)
		{
			avlNode = avlNode.Left;
		}
		AvlNode<T> parent = node.Parent;
		AvlNode<T> parent2 = avlNode.Parent;
		AvlNode<T> right2 = avlNode.Right;
		if (parent2.Left == avlNode)
		{
			parent2.Left = right2;
		}
		else
		{
			parent2.Right = right2;
		}
		if (right2 != null)
		{
			right2.Parent = parent2;
		}
		avlNode.Parent = parent;
		avlNode.Left = left;
		avlNode.Balance = node.Balance;
		avlNode.Right = right;
		right.Parent = avlNode;
		left.Parent = avlNode;
		if (node == _root)
		{
			_root = avlNode;
		}
		else if (parent.Left == node)
		{
			parent.Left = avlNode;
		}
		else
		{
			parent.Right = avlNode;
		}
		RemoveBalance(parent2, -1);
	}

	protected void RemoveBalance(AvlNode<T> node, int balance)
	{
		while (node != null)
		{
			balance = (node.Balance += balance);
			switch (balance)
			{
			case 2:
				if (node.Left.Balance >= 0)
				{
					node = RotateRight(node);
					if (node.Balance == -1)
					{
						return;
					}
				}
				else
				{
					node = RotateLeftRight(node);
				}
				break;
			case -2:
				if (node.Right.Balance <= 0)
				{
					node = RotateLeft(node);
					if (node.Balance == 1)
					{
						return;
					}
				}
				else
				{
					node = RotateRightLeft(node);
				}
				break;
			default:
				return;
			case 0:
				break;
			}
			AvlNode<T> parent = node.Parent;
			if (parent != null)
			{
				balance = ((parent.Left != node) ? 1 : (-1));
			}
			node = parent;
		}
	}

	private static void Replace(AvlNode<T> target, AvlNode<T> source)
	{
		AvlNode<T> left = source.Left;
		AvlNode<T> right = source.Right;
		target.Balance = source.Balance;
		target.Item = source.Item;
		target.Left = left;
		target.Right = right;
		if (left != null)
		{
			left.Parent = target;
		}
		if (right != null)
		{
			right.Parent = target;
		}
	}

	public virtual bool RemoveSafe(T localValue)
	{
		AvlNode<T> avlNode = _root;
		while (avlNode != null)
		{
			if (_comparer.Compare(localValue, avlNode.Item) < 0)
			{
				avlNode = avlNode.Left;
				continue;
			}
			if (_comparer.Compare(localValue, avlNode.Item) > 0)
			{
				avlNode = avlNode.Right;
				continue;
			}
			RemoveNodeSafe(avlNode);
			return true;
		}
		return false;
	}

	public void RemoveNodeSafe(AvlNode<T> node)
	{
		_count--;
		AvlNode<T> left = node.Left;
		AvlNode<T> right = node.Right;
		if (left == null)
		{
			if (right == null)
			{
				if (node == _root)
				{
					_root = null;
				}
				else if (node.Parent.Left == node)
				{
					node.Parent.Left = null;
					RemoveBalance(node.Parent, -1);
				}
				else if (node.Parent.Right == node)
				{
					node.Parent.Right = null;
					RemoveBalance(node.Parent, 1);
				}
				else
				{
					DumpVisual2();
				}
			}
			else
			{
				ReplaceSafe(node, right);
				RemoveBalance(right, 0);
			}
			return;
		}
		if (right == null)
		{
			ReplaceSafe(node, left);
			RemoveBalance(left, 0);
			return;
		}
		AvlNode<T> avlNode = right;
		if (avlNode.Left == null)
		{
			AvlNode<T> avlNode2 = (avlNode.Parent = node.Parent);
			avlNode.Left = left;
			avlNode.Balance = node.Balance;
			left.Parent = avlNode;
			if (node == _root)
			{
				_root = avlNode;
			}
			else if (avlNode2.Left == node)
			{
				avlNode2.Left = avlNode;
			}
			else
			{
				avlNode2.Right = avlNode;
			}
			RemoveBalance(avlNode, 1);
			return;
		}
		while (avlNode.Left != null)
		{
			avlNode = avlNode.Left;
		}
		AvlNode<T> parent = node.Parent;
		AvlNode<T> parent2 = avlNode.Parent;
		AvlNode<T> right2 = avlNode.Right;
		if (parent2.Left == avlNode)
		{
			parent2.Left = right2;
		}
		else
		{
			parent2.Right = right2;
		}
		if (right2 != null)
		{
			right2.Parent = parent2;
		}
		avlNode.Parent = parent;
		avlNode.Left = left;
		avlNode.Balance = node.Balance;
		avlNode.Right = right;
		right.Parent = avlNode;
		left.Parent = avlNode;
		if (node == _root)
		{
			_root = avlNode;
		}
		else if (parent.Left == node)
		{
			parent.Left = avlNode;
		}
		else
		{
			parent.Right = avlNode;
		}
		RemoveBalance(parent2, -1);
	}

	private void ReplaceSafe(AvlNode<T> itemToDelete, AvlNode<T> child)
	{
		child.Parent = itemToDelete.Parent;
		if (itemToDelete.Parent != null)
		{
			if (itemToDelete.Parent.Left == itemToDelete)
			{
				itemToDelete.Parent.Left = child;
			}
			else if (itemToDelete.Parent.Right == itemToDelete)
			{
				itemToDelete.Parent.Right = child;
			}
		}
		else
		{
			_root = child;
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public T GetFirstItem()
	{
		AvlNode<T> firstNode = GetFirstNode();
		if (firstNode != null)
		{
			return firstNode.Item;
		}
		return default;
	}

	public AvlNode<T> GetFirstNode()
	{
		if (Root != null)
		{
			AvlNode<T> avlNode = Root;
			while (avlNode.Left != null)
			{
				avlNode = avlNode.Left;
			}
			return avlNode;
		}
		return null;
	}

	public T GetLastItem()
	{
		AvlNode<T> lastNode = GetLastNode();
		if (lastNode != null)
		{
			return lastNode.Item;
		}
		return default;
	}

	public AvlNode<T> GetLastNode()
	{
		if (Root != null)
		{
			AvlNode<T> avlNode = Root;
			while (avlNode.Right != null)
			{
				avlNode = avlNode.Right;
			}
			return avlNode;
		}
		return null;
	}

	public virtual void Dump()
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(35, 3, stringBuilder2);
		handler.AppendLiteral("AvlTree dump of '");
		handler.AppendFormatted(Name);
		handler.AppendLiteral("', First: ");
		handler.AppendFormatted(GetFirstNode());
		handler.AppendLiteral(", Last: ");
		handler.AppendFormatted(GetLastNode());
		stringBuilder2.AppendLine(ref handler);
		bool condition = true;
		using IEnumerator<T> itemEnumerator = GetEnumerator();
		while (itemEnumerator.MoveNext())
		{
			T current = itemEnumerator.Current;
			if (condition)
			{
				condition = false;
			}
			else
			{
				stringBuilder.Append(", ");
			}
			stringBuilder.Append("[");
			stringBuilder.Append(current);
			stringBuilder.Append("]");
		}
	}

	public virtual void DumpVisual2(string prefix = "", string title = "")
	{
		VerifyIntegrity();
		StringBuilder stringBuilder = new StringBuilder();
		foreach (AvlNode<T> node in Nodes())
		{
			stringBuilder.Clear();
			stringBuilder.Append(prefix);
			for (int i = 0; i < node.GetHeight(); i++)
			{
				stringBuilder.Append("  ");
			}
			stringBuilder.Append(node);
		}
	}

	[DebuggerHidden]
	public void VerifyIntegrity()
	{
		if (Root != null)
		{
			VerifyNodeIntegrityRecursive(Root);
		}
	}

	[DebuggerHidden]
	private void VerifyNodeIntegrityRecursive(AvlNode<T> node)
	{
		if (node.Left != null)
		{
			VerifyNodeIntegrityRecursive(node.Left);
		}
		if (node.Right != null)
		{
			VerifyNodeIntegrityRecursive(node.Right);
		}
	}

	public int DebugCount()
	{
		return RecursiveCount(Root);
	}

	private int RecursiveCount(AvlNode<T> node)
	{
		if (node == null)
		{
			return 0;
		}
		return 1 + RecursiveCount(node.Left) + RecursiveCount(node.Right);
	}

	void ICollection<T>.Add(T localValue)
	{
		Add(localValue);
	}

	public void Clear()
	{
		_root = null;
		_count = 0;
	}

	public void CopyTo(T[] values, int index, int count)
	{
		if (values == null)
		{
			throw new ArgumentNullException("'array' can't be null");
		}
		if (index < 0)
		{
			throw new ArgumentException("'index' can't be null");
		}
		if (count < 0)
		{
			throw new ArgumentOutOfRangeException("'count' should be greater or equal to 0");
		}
		if (index > values.Length || count > values.Length - index)
		{
			throw new ArgumentException("The array size is not big enough to get all items");
		}
		if (count == 0)
		{
			return;
		}
		int ordinal = 0;
		int ordinal2 = 0;
		foreach (AvlNode<T> node in Nodes())
		{
			if (ordinal >= index)
			{
				values[ordinal2] = node.Item;
				ordinal2++;
				count--;
				if (count == 0)
				{
					break;
				}
			}
			ordinal++;
		}
	}

	public void CopyTo(T[] values, int arrayIndex)
	{
		new SortedSet<double>();
		CopyTo(values, arrayIndex, Count);
	}

	public void CopyTo(Array values, int index)
	{
		CopyTo(values as T[], index, Count);
	}

	[Conditional("DEBUG")]
	[DebuggerHidden]
	public void DebugEnsureTreeIsValid()
	{
	}

	[Conditional("DEBUG")]
	[DebuggerHidden]
	private void DebugEnsureProperParentRecursive(AvlNode<T> node)
	{
		_ = node.Left;
		_ = node.Right;
	}

	[Conditional("DEBUG")]
	[DebuggerHidden]
	public void DebugEnsureProperBalance()
	{
	}

	[Conditional("DEBUG")]
	[DebuggerHidden]
	private void RecursiveEnsureNodeBalanceIsValid(AvlNode<T> node)
	{
		int maxHeightRecursive = GetMaxHeightRecursive(node.Left);
		int maxHeightRecursive2 = GetMaxHeightRecursive(node.Right);
		if (maxHeightRecursive != 0 || maxHeightRecursive2 == 0)
		{
		}
		_ = node.Left;
		_ = node.Right;
	}

	[Conditional("DEBUG")]
	[DebuggerHidden]
	private void DumpIfNotTrue(bool assertion)
	{
		if (!assertion)
		{
			DumpVisual2();
			DumpVisual();
		}
	}

	[Conditional("DEBUG")]
	[DebuggerHidden]
	public void DebugEnsureTreeIsSorted()
	{
		IEnumerator<T> itemEnumerator = GetEnumerator();
		itemEnumerator.Reset();
		if (itemEnumerator.MoveNext())
		{
			T current = itemEnumerator.Current;
			while (itemEnumerator.MoveNext())
			{
				_comparer.Compare(current, itemEnumerator.Current);
				_ = -1;
				current = itemEnumerator.Current;
			}
		}
	}

	public int GetMaxHeight()
	{
		return GetMaxHeightRecursive(Root);
	}

	private int GetMaxHeightRecursive(AvlNode<T> node)
	{
		if (node == null)
		{
			return 0;
		}
		return 1 + Math.Max(GetMaxHeightRecursive(node.Left), GetMaxHeightRecursive(node.Right));
	}

	public void CopyTo(AvlTreeSet<T> avlTree)
	{
		avlTree.Name = Name;
		avlTree._comparer = _comparer;
		avlTree._count = _count;
		avlTree._root = CopyTreeRecursive(_root, null);
	}

	private AvlNode<T> CopyTreeRecursive(AvlNode<T> source, AvlNode<T> copyParent)
	{
		if (source == null)
		{
			return null;
		}
		AvlNode<T> avlNode = new AvlNode<T>();
		avlNode.Parent = copyParent;
		avlNode.Item = source.Item;
		avlNode.Balance = source.Balance;
		avlNode.Left = CopyTreeRecursive(source.Left, avlNode);
		avlNode.Right = CopyTreeRecursive(source.Right, avlNode);
		return avlNode;
	}

	public override int GetHashCode()
	{
		if (Root == null)
		{
			return 0;
		}
		return Root.Item.GetHashCode() + (Count ^ ~Root.Balance);
	}

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (!(obj is AvlTreeSet<T> avlTreeSet))
		{
			return false;
		}
		if (Count != avlTreeSet.Count || Name != avlTreeSet.Name)
		{
			return false;
		}
		using (IEnumerator<T> itemEnumerator = GetEnumerator())
		{
			using IEnumerator<T> avlTreeSet2 = avlTreeSet.GetEnumerator();
			itemEnumerator.Reset();
			avlTreeSet2.Reset();
			while (true)
			{
				bool condition = itemEnumerator.MoveNext();
				bool avlTreeSet2Term = avlTreeSet2.MoveNext();
				if (condition != avlTreeSet2Term)
				{
					return false;
				}
				if (!condition)
				{
					break;
				}
				if (!object.Equals(itemEnumerator.Current, avlTreeSet2.Current))
				{
					return false;
				}
			}
		}
		return true;
	}

	public void DumpVisual(string title = null)
	{
		if (title == null)
		{
			title = Name;
		}
		AvlNode<T> avlNode = GetLastNode();
		if (Count <= 256)
		{
			while (avlNode != null)
			{
				avlNode.GetHeight();
				avlNode = avlNode.GetPreviousNode();
			}
		}
	}

	public override string ToString()
	{
		return $"AvlTreeSet of {typeof(T).Name}. Count = {Count}";
	}
}
