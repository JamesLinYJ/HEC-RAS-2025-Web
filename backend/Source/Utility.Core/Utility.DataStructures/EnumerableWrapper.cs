using System;
using System.Collections;
using System.Collections.Generic;

namespace Utility.DataStructures;

public class EnumerableWrapper<T> : IEnumerable<T>, IEnumerable
{
	private Func<IEnumerator<T>> _getEnumeratorFunc;

	public EnumerableWrapper(Func<IEnumerator<T>> getEnumeratorFunc)
	{
		_getEnumeratorFunc = getEnumeratorFunc;
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _getEnumeratorFunc();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
