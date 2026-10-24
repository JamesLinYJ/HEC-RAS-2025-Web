using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Utility.Extensions;

namespace Utility.DataStructures;

public class AdvancedCollection<T> : ObservableCollection<T>, IAdvancedCollection<T>, IReadOnlyAdvancedCollection<T>, IReadOnlyNotifyCollection<T>, IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, INotifyCollectionChanged, ICollection<T>, ICollection, IList<T>, IList, INotifyPropertyChanged, INotifyList<T>, INotifyList
{
	public static ReadOnlyAdvancedCollection<T> Empty = new ReadOnlyAdvancedCollection<T>(new AdvancedCollection<T>());

	private bool _suppressCollectionChangedNotifications;

	public AdvancedCollection()
	{
	}

	public AdvancedCollection(IEnumerable<T> enumerable)
		: base(enumerable)
	{
	}

	public AdvancedCollection(List<T> items)
		: base(items ?? new List<T>())
	{
	}

	protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
	{
		if (!_suppressCollectionChangedNotifications)
		{
			base.OnCollectionChanged(e);
		}
	}

	public bool AddAll(IEnumerable<T> data, bool fullRefresh = false)
	{
		if (!data.Any())
		{
			return false;
		}
		HashSet<T> count = null;
		if (!fullRefresh && (data.Count() > 10 || Count > 10))
		{
			count = new HashSet<T>(Count);
			count.AddRange(this);
		}
		_suppressCollectionChangedNotifications = true;
		List<T> addIds = null;
		foreach (T datum in data)
		{
			if (fullRefresh)
			{
				Add(datum);
				continue;
			}
			if (count != null)
			{
				if (count.Contains(datum))
				{
					continue;
				}
			}
			else if (Contains(datum))
			{
				continue;
			}
			if (addIds == null)
			{
				addIds = new List<T>();
			}
			addIds.Add(datum);
			count?.Add(datum);
			Add(datum);
		}
		_suppressCollectionChangedNotifications = false;
		if (addIds == null && !fullRefresh)
		{
			return false;
		}
		if (fullRefresh)
		{
			Refresh();
		}
		else
		{
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, addIds, Count - addIds.Count));
		}
		return true;
	}

	public bool RemoveAll(IEnumerable<T> data, bool fullRefresh = false)
	{
		if (!data.Any())
		{
			return false;
		}
		HashSet<T> count = null;
		if (!fullRefresh && (data.Count() > 10 || Count > 10))
		{
			count = new HashSet<T>(Count);
			count.AddRange(this);
		}
		_suppressCollectionChangedNotifications = true;
		List<T> removeIds = null;
		int startingIndex = -1;
		foreach (T datum in data)
		{
			if (count != null)
			{
				if (!count.Contains(datum))
				{
					continue;
				}
			}
			else if (!Contains(datum))
			{
				continue;
			}
			if (removeIds == null)
			{
				removeIds = new List<T>();
				startingIndex = IndexOf(datum);
			}
			removeIds.Add(datum);
			count?.Remove(datum);
			Remove(datum);
		}
		_suppressCollectionChangedNotifications = false;
		if (removeIds == null || removeIds.Count == 0)
		{
			return false;
		}
		if (fullRefresh)
		{
			Refresh();
		}
		else
		{
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removeIds, startingIndex));
		}
		return true;
	}

	public bool RemoveAllAt(IEnumerable<int> data, bool fullRefresh = false)
	{
		if (!data.Any())
		{
			return false;
		}
		_suppressCollectionChangedNotifications = true;
		List<T> removeIds = new List<T>();
		int removeIds2 = int.MaxValue;
		foreach (int orderDescending in data.OrderDescending())
		{
			T orderDescendingTerm = base[orderDescending];
			removeIds.Add(orderDescendingTerm);
			removeIds2 = Math.Min(removeIds2, orderDescending);
			RemoveAt(orderDescending);
		}
		_suppressCollectionChangedNotifications = false;
		if (removeIds.Count == 0)
		{
			return false;
		}
		if (fullRefresh)
		{
			Refresh();
		}
		else
		{
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removeIds, removeIds2));
		}
		return true;
	}

	public void SetAll(IEnumerable<T> data, bool fullRefresh = false)
	{
		_suppressCollectionChangedNotifications = true;
		HashSet<T> replaceIds = this.ToHashSet();
		List<T> items = data.ToList();
		Clear();
		for (int i = 0; i < items.Count; i++)
		{
			T localValue = items[i];
			if (replaceIds.Contains(localValue))
			{
				replaceIds.Remove(localValue);
				localValue.Equals(localValue);
				i--;
			}
			else
			{
				Add(localValue);
			}
		}
		_suppressCollectionChangedNotifications = false;
		if (fullRefresh)
		{
			Refresh();
		}
		else
		{
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, data.ToList(), replaceIds.ToList(), 0));
		}
	}

	public void SetRange(IList<int> indices, IEnumerable<T> dataList, bool fullRefresh = false)
	{
		if (indices.Count != 0)
		{
			ValidateIndices(indices, dataList);
			List<T> dataListList = dataList.ToList();
			List<T> items = new List<T>();
			for (int i = 0; i < dataListList.Count; i++)
			{
				T dataList2 = dataListList[i];
				int index = indices[i];
				items.Add(Items[index]);
				Items[index] = dataList2;
			}
			if (fullRefresh)
			{
				Refresh();
			}
		}
	}

	private void ValidateIndices(IList<int> indices, IEnumerable<T> dataList)
	{
		if (indices.Count != dataList.Count())
		{
			throw new ArgumentException("Count of indices must match count of data.");
		}
		int maxValue = int.MaxValue;
		int minValue = int.MinValue;
		foreach (int index in indices)
		{
			maxValue = Math.Min(index, maxValue);
			minValue = Math.Max(index, minValue);
		}
		if (maxValue < 0)
		{
			throw new IndexOutOfRangeException("SetRange called with negative index: " + maxValue);
		}
		if (minValue >= Count)
		{
			throw new IndexOutOfRangeException("SetRange called with index out of range: " + minValue + Environment.NewLine + "Collection count: " + Count);
		}
	}

	public void Refresh()
	{
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}

	public void Set(IEnumerable<T> data, bool refresh = false)
	{
		List<T> oldItems = new List<T>(Items);
		Items.Clear();
		foreach (T datum in data)
		{
			Items.Add(datum);
		}
		if (refresh)
		{
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}
		else
		{
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, data.ToList(), oldItems, 0));
		}
	}

	public void Sort(Comparison<T> sortFunc)
	{
		if (Items.Count < 2)
		{
			return;
		}
		for (int i = 0; i < Items.Count - 1 && sortFunc(Items[i], Items[i + 1]) <= 0; i++)
		{
			if (i == Items.Count - 2)
			{
				return;
			}
		}
		int count = Items.Count;
		T[] rent = ArrayPool<T>.Shared.Rent(count);
		try
		{
			for (int j = 0; j < count; j++)
			{
				rent[j] = Items[j];
			}
			Array.Sort(rent, 0, count, Comparer<T>.Create(sortFunc));
			_suppressCollectionChangedNotifications = true;
			Items.Clear();
			for (int k = 0; k < count; k++)
			{
				Items.Add(rent[k]);
			}
			_suppressCollectionChangedNotifications = false;
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}
		finally
		{
			ArrayPool<T>.Shared.Return(rent);
		}
	}
}
