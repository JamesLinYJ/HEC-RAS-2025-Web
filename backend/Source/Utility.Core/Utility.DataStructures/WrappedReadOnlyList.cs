using System.Collections;
using System.Collections.Generic;

namespace Utility.DataStructures;

public class WrappedReadOnlyList<T> : IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>
{
	private IList<T> _collection;

	public T this[int index] => _collection[index];

	public int Count => _collection.Count;

	public WrappedReadOnlyList(IList<T> collection)
	{
		_collection = collection;
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _collection.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return _collection.GetEnumerator();
	}
}
