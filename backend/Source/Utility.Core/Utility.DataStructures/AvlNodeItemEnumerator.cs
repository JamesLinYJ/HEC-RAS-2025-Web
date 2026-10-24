using System;
using System.Collections;
using System.Collections.Generic;

namespace Utility.DataStructures;

internal class AvlNodeItemEnumerator<T> : IEnumerator<T>, IEnumerator, IDisposable
{
	private AvlNode<T> _current;

	private AvlTreeSet<T> _avlTree;

	public T Current
	{
		get
		{
			if (_current == null)
			{
				throw new InvalidOperationException("Current is invalid");
			}
			return _current.Item;
		}
	}

	object IEnumerator.Current => Current;

	public AvlNodeItemEnumerator(AvlTreeSet<T> avlTree)
	{
		if (avlTree == null)
		{
			throw new ArgumentNullException("avlTree can't be null");
		}
		_avlTree = avlTree;
	}

	public void Dispose()
	{
	}

	public bool MoveNext()
	{
		if (_current == null)
		{
			_current = _avlTree.GetFirstNode();
		}
		else
		{
			_current = _current.GetNextNode();
		}
		if (_current == null)
		{
			return false;
		}
		return true;
	}

	public void Reset()
	{
		_current = null;
	}
}
