using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Utility.DataStructures;
using Utility.Helpers;

namespace Utility.Extensions;

public static class IListExtensions
{
	private static class DedupeScratch<T>
	{
		[ThreadStatic]
		private static HashSet<T> _value;

		public static HashSet<T> Value => _value ?? (_value = new HashSet<T>());
	}

	private class IROListWrapper<T> : IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable
	{
		private readonly IReadOnlyList<T> _ro;

		public T this[int index]
		{
			get
			{
				return _ro[index];
			}
			set
			{
				throw new NotImplementedException();
			}
		}

		public int Count => _ro.Count;

		public bool IsReadOnly => true;

		public IROListWrapper(IReadOnlyList<T> ro)
		{
			_ro = ro;
		}

		public void Add(T localValue)
		{
			throw new NotImplementedException();
		}

		public void Clear()
		{
			throw new NotImplementedException();
		}

		public bool Contains(T localValue)
		{
			return _ro.Contains(localValue);
		}

		public void CopyTo(T[] values, int arrayIndex)
		{
			for (int i = 0; i < _ro.Count && arrayIndex + i < _ro.Count; i++)
			{
				values[arrayIndex + i] = _ro[i];
			}
		}

		public IEnumerator<T> GetEnumerator()
		{
			return _ro.GetEnumerator();
		}

		public int IndexOf(T localValue)
		{
			return _ro.IndexOf(localValue);
		}

		public void Insert(int index, T localValue)
		{
			throw new NotImplementedException();
		}

		public bool Remove(T localValue)
		{
			throw new NotImplementedException();
		}

		public void RemoveAt(int index)
		{
			throw new NotImplementedException();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return _ro.GetEnumerator();
		}
	}

	public static void LinearInterpolateNaNsInPlace(this IList<double> doubleList)
	{
		ListHelpers.LinearInterpolateNaNsInPlace(doubleList, doubleList.Count, (IList<double> doubleListList, int idx) => doubleListList[idx], (IList<double> doubleList2, int idx, double computed) =>
		{
			doubleList2[idx] = computed;
		});
	}

	public static bool IsNullOrEmpty<T>(this ICollection<T> l)
	{
		if (l != null)
		{
			return l.Count == 0;
		}
		return true;
	}

	public static List<T> DedupeInPlace<T>(this List<T> items)
	{
		HashSet<T> localValue = DedupeScratch<T>.Value;
		localValue.Clear();
		int ordinal = 0;
		for (int i = 0; i < items.Count; i++)
		{
			T localValue2 = items[i];
			if (localValue.Add(localValue2))
			{
				items[ordinal++] = localValue2;
			}
		}
		items.RemoveRange(ordinal, items.Count - ordinal);
		return items;
	}

	public static void ThrowIfNullOrEmpty<T>(this IList<T> l, string name)
	{
		if (l == null)
		{
			throw new ArgumentNullException(name);
		}
		if (l.Count == 0)
		{
			throw new ArgumentException(name + " is empty.");
		}
	}

	public static IList<T> AsMutable<T>(this IReadOnlyList<T> ro)
	{
		return new IROListWrapper<T>(ro);
	}

	public static void AddRangeReverse<T>(this List<T> l, IList<T> newItems)
	{
		if (l.Capacity < l.Count + newItems.Count)
		{
			l.Capacity = l.Count + newItems.Count;
		}
		for (int i = newItems.Count - 1; i >= 0; i += -1)
		{
			l.Add(newItems[i]);
		}
	}

	public static void AddRangeReverse<T>(this List<T> l, ReadOnlySpan<T> newItems)
	{
		if (l.Capacity < l.Count + newItems.Length)
		{
			l.Capacity = l.Count + newItems.Length;
		}
		for (int i = newItems.Length - 1; i >= 0; i += -1)
		{
			l.Add(newItems[i]);
		}
	}

	public static List<T> CloneReversed<T>(this List<T> l)
	{
		List<T> items = new List<T>(l.Count);
		for (int i = l.Count - 1; i >= 0; i += -1)
		{
			items.Add(l[i]);
		}
		return items;
	}

	public static int FirstIndexOf<T>(this T[] values, Func<T, bool> predicate)
	{
		return ((IList<T>)values).FirstIndexOf(predicate);
	}

	public static int FirstIndexOf<T>(this IList<T> items, Func<T, bool> predicate)
	{
		for (int i = 0; i < items.Count; i++)
		{
			if (predicate(items[i]))
			{
				return i;
			}
		}
		return -1;
	}

	public static int FirstIndexOf<T>(this IReadOnlyList<T> items, Func<T, bool> predicate)
	{
		for (int i = 0; i < items.Count; i++)
		{
			if (predicate(items[i]))
			{
				return i;
			}
		}
		return -1;
	}

	public static int FirstIndexOf<T>(this List<T> items, Func<T, bool> predicate)
	{
		return ((IReadOnlyList<T>)items).FirstIndexOf(predicate);
	}

	public static int FirstIndexOf<T>(this Span<T> buffer, Func<T, bool> predicate)
	{
		for (int i = 0; i < buffer.Length; i++)
		{
			if (predicate(buffer[i]))
			{
				return i;
			}
		}
		return -1;
	}

	public static T[] ElementsAt<T>(this IList<T> items, IList<int> indexes)
	{
		T[] values = new T[indexes.Count];
		for (int i = 0; i < indexes.Count; i++)
		{
			values[i] = items[indexes[i]];
		}
		return values;
	}

	public static void Fill<T>(this T[] values, T localValue) where T : unmanaged
	{
		values.AsSpan().Fill(localValue);
	}

	public static void RemoveRange<T>(this List<T> items, int startIdx)
	{
		items.RemoveRange(startIdx, items.Count - startIdx);
	}

	public static void AddRange<T>(this ObservableCollection<T> observableCollection, IEnumerable<T> elements)
	{
		foreach (T element in elements)
		{
			observableCollection.Add(element);
		}
	}

	public static IEnumerable<K> SelectIf<T, K>(this IEnumerable<T> iEnumerable, Func<T, K> selector, Func<K, bool> conditional)
	{
		foreach (T localValue in iEnumerable)
		{
			K selector2 = selector(localValue);
			if (conditional(selector2))
			{
				yield return selector2;
			}
		}
	}

	public static IEnumerable<K> SelectIfNotNull<T, K>(this IEnumerable<T> iEnumerable, Func<T, K> selector)
	{
		return iEnumerable.SelectIf(selector, (K k) => k != null);
	}

	public static IEnumerable<int> WhereByIndex<T>(this IList<T> iList, Func<T, bool> predicate)
	{
		for (int i = 0; i < iList.Count; i++)
		{
			T localValue = iList[i];
			if (predicate(localValue))
			{
				yield return i;
			}
		}
	}

	public static void TrimIndexOf<T>(this IList<T> lst, Predicate<T> shouldKeep, out int firstKeptIndex, out int keepCount)
	{
		firstKeptIndex = -1;
		for (int i = 0; i < lst.Count; i++)
		{
			if (shouldKeep(lst[i]))
			{
				firstKeptIndex = i;
				break;
			}
		}
		if (firstKeptIndex == -1)
		{
			keepCount = 0;
			return;
		}
		for (int countOffset = lst.Count - 1; countOffset >= 0; countOffset--)
		{
			if (shouldKeep(lst[countOffset]))
			{
				keepCount = countOffset - firstKeptIndex;
				return;
			}
		}
		throw new Exception("Error - could not find the last kept index. Did the list mutate?");
	}

	public static void AddRange<T>(this HashSet<T> hs, IEnumerable<T> newElements)
	{
		foreach (T newElement in newElements)
		{
			hs.Add(newElement);
		}
	}

	public static IList<T> ReverseIndexing<T>(this IList<T> iList)
	{
		return new JITList<T>(iList.Count, shouldCache: false, (int i) => iList[iList.Count - i - 1]);
	}

	public static void RemoveWhere<K, V>(this Dictionary<K, V> dic, Func<KeyValuePair<K, V>, bool> shouldRemove)
	{
		List<K> keys = new List<K>();
		foreach (KeyValuePair<K, V> dic2 in dic)
		{
			if (shouldRemove(dic2))
			{
				keys.Add(dic2.Key);
			}
		}
		for (int i = 0; i < keys.Count; i++)
		{
			dic.Remove(keys[i]);
		}
	}

	public static List<T> FilterByIndex<T>(this IList<T> items, Func<int, bool> keepElement, out int[] keepMap)
	{
		List<T> itemsList = new List<T>();
		keepMap = new int[items.Count];
		for (int i = 0; i < items.Count; i++)
		{
			if (keepElement(i))
			{
				itemsList.Add(items[i]);
				keepMap[i] = itemsList.Count - 1;
			}
			else
			{
				keepMap[i] = -1;
			}
		}
		return itemsList;
	}

	public static List<T> Filter<T>(this IList<T> items, Func<T, bool> keepElement, out int[] keepMap)
	{
		List<T> itemsList = new List<T>();
		keepMap = new int[items.Count];
		for (int i = 0; i < items.Count; i++)
		{
			T localValue = items[i];
			if (keepElement(localValue))
			{
				itemsList.Add(localValue);
				keepMap[i] = itemsList.Count - 1;
			}
			else
			{
				keepMap[i] = -1;
			}
		}
		return itemsList;
	}

	public static int IndexOf<T>(this IReadOnlyList<T> items, T element)
	{
		if (element == null)
		{
			return -1;
		}
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].Equals(element))
			{
				return i;
			}
		}
		return -1;
	}

	public static void RotateForward<T>(this IList<T> items, int nElements)
	{
		if (nElements >= items.Count)
		{
			nElements -= items.Count;
		}
		if (nElements != 0)
		{
			T[] values = new T[nElements];
			for (int i = 0; i < nElements; i++)
			{
				values[i] = items[items.Count - nElements + i];
			}
			for (int countOffset = items.Count - 1; countOffset >= nElements; countOffset--)
			{
				items[countOffset] = items[countOffset - nElements];
			}
			for (int j = 0; j < nElements; j++)
			{
				items[j] = values[j];
			}
		}
	}

	public static void RotateBackward<T>(this IList<T> items, int nElements)
	{
		if (nElements >= items.Count)
		{
			nElements -= items.Count;
		}
		if (nElements != 0)
		{
			T[] values = new T[nElements];
			for (int i = 0; i < nElements; i++)
			{
				values[i] = items[i];
			}
			for (int j = 0; j < items.Count - nElements; j++)
			{
				items[j] = items[j + nElements];
			}
			for (int k = 0; k < nElements; k++)
			{
				items[k + (items.Count - nElements)] = values[k];
			}
		}
	}
}
