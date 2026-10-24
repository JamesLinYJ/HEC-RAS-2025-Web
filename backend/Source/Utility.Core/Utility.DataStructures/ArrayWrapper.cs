using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Utility.DataStructures;

public class ArrayWrapper<T> : IArray<T>, IEnumerable<T>, IEnumerable
{
	public T this[int index]
	{
		get
		{
			return BackingBuffer[index];
		}
		set
		{
			BackingBuffer[index] = value;
		}
	}

	public int Count => BackingBuffer.Count;

	public IList<T> BackingBuffer { get; private set; }

	public ArrayWrapper(IList<T> data)
	{
		BackingBuffer = data;
	}

	public T First()
	{
		return BackingBuffer.First();
	}

	public T Last()
	{
		return BackingBuffer.Last();
	}

	public List<T> ToList()
	{
		return BackingBuffer.ToList();
	}

	public IEnumerator<T> GetEnumerator()
	{
		return BackingBuffer.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return BackingBuffer.GetEnumerator();
	}
}
