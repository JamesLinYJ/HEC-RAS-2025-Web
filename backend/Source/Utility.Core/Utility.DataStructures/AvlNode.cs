namespace Utility.DataStructures;

public sealed class AvlNode<T>
{
	public AvlNode<T> Parent;

	public AvlNode<T> Left;

	public AvlNode<T> Right;

	public T Item;

	public int Balance;

	public int GetHeight()
	{
		int height = 1;
		AvlNode<T> avlNode = this;
		while (avlNode.Parent != null)
		{
			height++;
			avlNode = avlNode.Parent;
		}
		return height;
	}

	public AvlNode<T> GetNextNode()
	{
		AvlNode<T> avlNode;
		if (Right != null)
		{
			avlNode = Right;
			while (avlNode.Left != null)
			{
				avlNode = avlNode.Left;
			}
			return avlNode;
		}
		avlNode = this;
		while (avlNode.Parent != null)
		{
			if (avlNode.Parent.Left == avlNode)
			{
				return avlNode.Parent;
			}
			avlNode = avlNode.Parent;
		}
		return null;
	}

	public AvlNode<T> GetPreviousNode()
	{
		AvlNode<T> avlNode;
		if (Left != null)
		{
			avlNode = Left;
			while (avlNode.Right != null)
			{
				avlNode = avlNode.Right;
			}
			return avlNode;
		}
		avlNode = this;
		while (avlNode.Parent != null)
		{
			if (avlNode.Parent.Right == avlNode)
			{
				return avlNode.Parent;
			}
			avlNode = avlNode.Parent;
		}
		return null;
	}

	public override int GetHashCode()
	{
		return (Item.GetHashCode() + Left?.Item.GetHashCode()) ?? Right?.Item.GetHashCode() ?? Balance;
	}

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (!(obj is AvlNode<T> avlNode))
		{
			return false;
		}
		if (Balance != avlNode.Balance || !object.Equals(Item, avlNode.Item))
		{
			return false;
		}
		if (Left != null)
		{
			if (avlNode.Left == null)
			{
				return false;
			}
			if (!object.Equals(Left.Item, avlNode.Left.Item))
			{
				return false;
			}
		}
		else if (avlNode.Left != null)
		{
			return false;
		}
		if (Right != null)
		{
			if (avlNode.Right == null)
			{
				return false;
			}
			if (!object.Equals(Right.Item, avlNode.Right.Item))
			{
				return false;
			}
		}
		else if (avlNode.Right != null)
		{
			return false;
		}
		return true;
	}

	public override string ToString()
	{
		return $"AvlNode [{Item}], balance: {Balance}, Parent: {Parent?.Item.ToString()}, Left: {Left?.Item.ToString()}, Right: {Right?.Item.ToString()},";
	}
}
