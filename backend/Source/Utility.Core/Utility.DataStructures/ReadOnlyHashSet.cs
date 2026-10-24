using System;
using System.Collections.Generic;

namespace Utility.DataStructures;

public class ReadOnlyHashSet<T> : HashSet<T>
{
	public ReadOnlyHashSet()
	{
	}

	public ReadOnlyHashSet(IEnumerable<T> collection)
		: base(collection)
	{
	}

	public ReadOnlyHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
		: base(collection, comparer)
	{
	}

	public ReadOnlyHashSet(IEqualityComparer<T> comparer)
		: base(comparer)
	{
	}

	public new bool Add(T localValue)
	{
		throw new InvalidOperationException("Can't add items to ReadOnlyHashSet.");
	}

	public new bool Remove(T localValue)
	{
		throw new InvalidOperationException("Can't remove items from ReadOnlyHashSet.");
	}

	public new void UnionWith(IEnumerable<T> other)
	{
		throw new InvalidOperationException("Can't modify this ReadOnlyHashSet");
	}

	public new void IntersectWith(IEnumerable<T> other)
	{
		throw new InvalidOperationException("Can't modify this ReadOnlyHashSet");
	}

	public new void ExceptWith(IEnumerable<T> other)
	{
		throw new InvalidOperationException("Can't modify this ReadOnlyHashSet");
	}

	public new void SymmetricExceptWith(IEnumerable<T> other)
	{
		throw new InvalidOperationException("Can't modify this ReadOnlyHashSet");
	}
}
