using System;
using System.Collections.Generic;
using System.Threading;

namespace Utility.DataStructures;

public static class HashHelpers
{
	public static HashSet<T> ComputeHashSet<T>(IList<T> elements)
	{
		if (elements == null)
		{
			throw new ArgumentNullException("elements");
		}
		if (elements.Count == 0)
		{
			return new HashSet<T>();
		}
		if (elements.Count < 1024)
		{
			HashSet<T> elementsSet = new HashSet<T>();
			for (int i = 0; i < elements.Count; i++)
			{
				elementsSet.Add(elements[i]);
			}
			return elementsSet;
		}
		ThreadLocal<HashSet<T>> tl = new ThreadLocal<HashSet<T>>(() => new HashSet<T>(), trackAllValues: true);
		Parallel.SmartFor(elements.Count, (int start, int end) =>
		{
			HashSet<T> tlTerm = tl.Value;
			for (int j = start; j <= end; j++)
			{
				tlTerm.Add(elements[j]);
			}
		}, 256);
		HashSet<T> items = new HashSet<T>();
		foreach (HashSet<T> localValue in tl.Values)
		{
			foreach (T localValue2 in localValue)
			{
				items.Add(localValue2);
			}
		}
		return items;
	}

	public static HashSet<T> ComputeHashSet<T>(Memory<T> elements)
	{
		if (elements.Length == 0)
		{
			return new HashSet<T>();
		}
		if (elements.Length < 1024)
		{
			HashSet<T> elementsSet = new HashSet<T>();
			Span<T> elementsSpan = elements.Span;
			for (int i = 0; i < elementsSpan.Length; i++)
			{
				elementsSet.Add(elementsSpan[i]);
			}
			return elementsSet;
		}
		ThreadLocal<HashSet<T>> tl = new ThreadLocal<HashSet<T>>(() => new HashSet<T>(), trackAllValues: true);
		Parallel.SmartFor(elements.Length, (int start, int end) =>
		{
			Span<T> elements2 = elements.Span;
			HashSet<T> tlTerm = tl.Value;
			for (int j = start; j <= end; j++)
			{
				tlTerm.Add(elements2[j]);
			}
		}, 256);
		HashSet<T> items = new HashSet<T>();
		foreach (HashSet<T> localValue in tl.Values)
		{
			foreach (T localValue2 in localValue)
			{
				items.Add(localValue2);
			}
		}
		return items;
	}
}
