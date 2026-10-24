using System;
using System.Collections;
using System.Collections.Generic;

namespace Utility.DataStructures;

public class CompactListEnumerator<T> : IEnumerator<T>, IEnumerator, IDisposable
{
	private CompactList<T> _list;

	private int _position = -1;

	public T Current
	{
		get
		{
			try
			{
				return _list[_position];
			}
			catch (Exception)
			{
				throw new IndexOutOfRangeException();
			}
		}
	}

	object IEnumerator.Current => ((IEnumerator<T>)this).Current;

	public CompactListEnumerator(CompactList<T> compactList)
	{
		_list = compactList;
	}

	public bool MoveNext()
	{
		_position++;
		return _position < _list.Count;
	}

	public void Reset()
	{
		_position = -1;
	}

	public void Dispose()
	{
	}
}
