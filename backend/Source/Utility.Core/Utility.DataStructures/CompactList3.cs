using System;
using System.Collections.Generic;

namespace Utility.DataStructures;

public struct CompactList3<T>
{
	public struct Enumerator
	{
		private CompactList3<T> _list;

		private int _index;

		public T Current => _list[_index];

		internal Enumerator(CompactList3<T> compactList3)
		{
			_list = compactList3;
			_index = -1;
		}

		public bool MoveNext()
		{
			return ++_index < _list._count;
		}
	}

	private const int InlineCapacity = 3;

	private InlineSlots3<T> _inline;

	private List<T> _overflow;

	private int _count;

	public int Count => _count;

	public T this[int index]
	{
		get
		{
			if (index < 0 || index >= _count)
			{
				throw new IndexOutOfRangeException();
			}
			if (index >= 3)
			{
				return _overflow[index - 3];
			}
			return _inline[index];
		}
		set
		{
			if (index < 0 || index >= _count)
			{
				throw new IndexOutOfRangeException();
			}
			if (index < 3)
			{
				_inline[index] = value;
			}
			else
			{
				_overflow[index - 3] = value;
			}
		}
	}

	public void Add(T inline)
	{
		if (_count < 3)
		{
			_inline[_count] = inline;
		}
		else
		{
			if (_overflow == null)
			{
				_overflow = new List<T>();
			}
			_overflow.Add(inline);
		}
		_count++;
	}

	public void Clear()
	{
		_inline = default;
		_overflow?.Clear();
		_count = 0;
	}

	public Enumerator GetEnumerator()
	{
		return new Enumerator(this);
	}
}
