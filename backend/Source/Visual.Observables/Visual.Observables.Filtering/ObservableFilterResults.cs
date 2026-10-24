using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility;
using Utility.DataStructures;
using Utility.Filtering;

namespace Visual.Observables.Filtering;

public class ObservableFilterResults : ObservableObject
{
	private struct FilterAction
	{
		public static FilterAction Refilter = new FilterAction
		{
			Command = FilterActionCommand.Refilter
		};

		public FilterActionCommand Command { get; init; }

		public IList NewItems { get; init; }

		public IList OldItem { get; init; }
	}

	private enum FilterActionCommand
	{
		Refilter,
		Update
	}

	private readonly HashSet<object> _oldItemsScratchSpace = new HashSet<object>();

	private readonly HashSet<object> _newItemsScratchSpace = new HashSet<object>();

	private List<IFilter> _filters = new List<IFilter>();

	private readonly Queue<FilterAction> _queuedActions = new Queue<FilterAction>();

	private readonly Dictionary<object, int> _indexLookupScratchSpace = new Dictionary<object, int>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IEnumerable ItemsSource
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<IEnumerable>.Default.Equals(field, value))
			{
				IEnumerable oldValue = field;
				OnPropertyChanging("ItemsSource");
				field = value;
				OnItemsSourceChanged(oldValue, value);
				OnPropertyChanged("ItemsSource");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<object> FilteredItemsSource
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<AdvancedCollection<object>>.Default.Equals(field, value))
			{
				OnPropertyChanging("FilteredItemsSource");
				field = value;
				OnPropertyChanged("FilteredItemsSource");
			}
		}
	} = new AdvancedCollection<object>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsAnythingFilteredOut
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsAnythingFilteredOut");
				field = value;
				OnPropertyChanged("IsAnythingFilteredOut");
			}
		}
	}

	public IReadOnlyList<IFilter> Filters => _filters;

	private ISynchronizationContext UIThreadSyncContext { get; } = SynchronizationContext.None;

	public ObservableFilterResults(ISynchronizationContext uiThreadSyncContext = null)
	{
		if (uiThreadSyncContext != null)
		{
			UIThreadSyncContext = uiThreadSyncContext;
		}
	}

	public void AddFilter(IFilter filter)
	{
		if (filter != null && !_filters.Contains(filter))
		{
			_filters.Add(filter);
			filter.FilterStatusChanged += FilterStatusChanged;
			QueueRefilter();
		}
	}

	public void RemoveFilter(IFilter filter)
	{
		if (filter != null && _filters.Contains(filter))
		{
			_filters.Remove(filter);
			filter.FilterStatusChanged -= FilterStatusChanged;
			QueueRefilter();
		}
	}

	private void FilterStatusChanged(IFilter sender)
	{
		QueueRefilter();
	}

	private void QueueAction(FilterAction action)
	{
		bool queuedActionCountIsNonZero = _queuedActions.Count != 0;
		_queuedActions.Enqueue(action);
		if (!queuedActionCountIsNonZero)
		{
			UIThreadSyncContext.Sync(ProcessQueuedActions);
		}
	}

	private void ProcessQueuedActions()
	{
		if (_queuedActions.Count == 0)
		{
			return;
		}
		HashSet<object> newItemsScratchSpace = _newItemsScratchSpace;
		HashSet<object> oldItemsScratchSpace = _oldItemsScratchSpace;
		_oldItemsScratchSpace.Clear();
		_newItemsScratchSpace.Clear();
		bool condition = false;
		while (_queuedActions.Count != 0)
		{
			FilterAction filterAction = _queuedActions.Dequeue();
			if (filterAction.Command == FilterActionCommand.Refilter)
			{
				condition = true;
				break;
			}
			if (filterAction.NewItems != null)
			{
				foreach (object newItem in filterAction.NewItems)
				{
					if (oldItemsScratchSpace.Contains(newItem))
					{
						oldItemsScratchSpace.Remove(newItem);
					}
					else
					{
						newItemsScratchSpace.Add(newItem);
					}
				}
			}
			if (filterAction.OldItem == null)
			{
				continue;
			}
			foreach (object oldItem in filterAction.OldItem)
			{
				if (newItemsScratchSpace.Contains(oldItem))
				{
					newItemsScratchSpace.Remove(oldItem);
				}
				else
				{
					oldItemsScratchSpace.Add(oldItem);
				}
			}
		}
		if (condition)
		{
			Refilter();
		}
		else if (newItemsScratchSpace.Count != 0 || oldItemsScratchSpace.Count != 0)
		{
			Update((newItemsScratchSpace.Count == 0) ? null : newItemsScratchSpace.ToList(), (oldItemsScratchSpace.Count == 0) ? null : oldItemsScratchSpace.ToList());
		}
		_queuedActions.Clear();
	}

	private void ItemsSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			QueueRefilter();
		}
		else
		{
			QueueUpdate(e.NewItems, e.OldItems);
		}
	}

	private void QueueUpdate(IList newItems, IList oldItems)
	{
		if (UIThreadSyncContext == SynchronizationContext.None)
		{
			Update(newItems, oldItems);
			return;
		}
		FilterAction action = new FilterAction
		{
			Command = FilterActionCommand.Update,
			NewItems = newItems,
			OldItem = oldItems
		};
		QueueAction(action);
	}

	private void Update(IList newItems, IList oldItems)
	{
		bool condition = false;
		if (newItems != null)
		{
			IEnumerable enumerable = FilterEnumerable(newItems);
			if (enumerable != null)
			{
				condition |= FilteredItemsSource.AddAll(enumerable.Cast<object>());
			}
		}
		if (oldItems != null)
		{
			FilteredItemsSource.RemoveAll(oldItems.Cast<object>(), fullRefresh: true);
		}
		if (condition)
		{
			Resort();
		}
	}

	private IEnumerable FilterEnumerable(IEnumerable source)
	{
		IEnumerable enumerable = source;
		foreach (IFilter filter in _filters)
		{
			enumerable = filter.Filter(enumerable);
		}
		return enumerable;
	}

	private void QueueRefilter()
	{
		if (UIThreadSyncContext == SynchronizationContext.None)
		{
			Refilter();
		}
		else
		{
			QueueAction(FilterAction.Refilter);
		}
	}

	private void Refilter()
	{
		if (ItemsSource == null)
		{
			FilteredItemsSource.Clear();
			return;
		}
		HashSet<object> data = FilterEnumerable(ItemsSource).Cast<object>().ToHashSet();
		FilteredItemsSource.SetAll(data, fullRefresh: true);
		IsAnythingFilteredOut = FilteredItemsSource.Count != ItemsSource.Cast<object>().Count();
		Resort();
	}

	private void Resort()
	{
		if (!(ItemsSource is IList itemsSource) || FilteredItemsSource.Count == 0)
		{
			return;
		}
		Dictionary<object, int> indexLookup = _indexLookupScratchSpace;
		indexLookup.Clear();
		for (int i = 0; i < itemsSource.Count; i++)
		{
			indexLookup[itemsSource[i]] = i;
		}
		if (IsAlreadySorted(FilteredItemsSource, indexLookup))
		{
			return;
		}
		for (int j = 0; j < FilteredItemsSource.Count - 1; j++)
		{
			if (indexLookup[FilteredItemsSource[j]] > indexLookup[FilteredItemsSource[j + 1]])
			{
				object filteredItemsSource = FilteredItemsSource[j];
				FilteredItemsSource[j] = FilteredItemsSource[j + 1];
				FilteredItemsSource[j + 1] = filteredItemsSource;
			}
		}
		if (!IsAlreadySorted(FilteredItemsSource, indexLookup))
		{
			FilteredItemsSource.Sort((object t1, object t2) => indexLookup[t1].CompareTo(indexLookup[t2]));
		}
	}

	private static bool IsAlreadySorted(IList items, Dictionary<object, int> indexLookup)
	{
		for (int i = 0; i < items.Count - 1; i++)
		{
			if (indexLookup[items[i]] > indexLookup[items[i + 1]])
			{
				return false;
			}
		}
		return true;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
	{
		if (oldValue is INotifyCollectionChanged notifyCollectionChanged)
		{
			notifyCollectionChanged.CollectionChanged -= ItemsSourceCollectionChanged;
		}
		if (newValue is INotifyCollectionChanged notifyCollectionChanged2)
		{
			notifyCollectionChanged2.CollectionChanged += ItemsSourceCollectionChanged;
		}
		_queuedActions.Clear();
		Refilter();
	}
}
public class ObservableFilterResults<T> : ObservableObject
{
	private List<IFilter<T>> _filters = new List<IFilter<T>>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IReadOnlyList<T> ItemsSource
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<IReadOnlyList<T>>.Default.Equals(field, value))
			{
				IReadOnlyList<T> oldValue = field;
				OnPropertyChanging("ItemsSource");
				field = value;
				OnItemsSourceChanged(oldValue, value);
				OnPropertyChanged("ItemsSource");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<T> FilteredItemsSource
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<AdvancedCollection<T>>.Default.Equals(field, value))
			{
				OnPropertyChanging("FilteredItemsSource");
				field = value;
				OnPropertyChanged("FilteredItemsSource");
			}
		}
	} = new AdvancedCollection<T>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public T BestMatchItem
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<T>.Default.Equals(field, value))
			{
				OnPropertyChanging("BestMatchItem");
				field = value;
				OnPropertyChanged("BestMatchItem");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsAnythingFilteredOut
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsAnythingFilteredOut");
				field = value;
				OnPropertyChanged("IsAnythingFilteredOut");
			}
		}
	}

	public IReadOnlyList<IFilter<T>> Filters => _filters;

	public ObservableFilterResults(IReadOnlyList<T> itemsSource = null, params IFilter<T>[] defaultFilters)
	{
		for (int i = 0; i < defaultFilters.Length; i++)
		{
			AddFilter(defaultFilters[i]);
		}
		ItemsSource = itemsSource;
	}

	private void ItemsSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		Refilter(e.Action == NotifyCollectionChangedAction.Reset);
	}

	public void AddFilter(IFilter<T> filter)
	{
		_filters.Add(filter);
		filter.FilterStatusChanged += FilterStatusChanged;
		Refilter();
	}

	public void RemoveFilter(IFilter<T> filter)
	{
		_filters.Remove(filter);
		filter.FilterStatusChanged -= FilterStatusChanged;
		Refilter();
	}

	private void FilterStatusChanged(IFilter sender)
	{
		Refilter();
	}

	public void Refilter(bool fullReset = false)
	{
		if (ItemsSource == null)
		{
			FilteredItemsSource.Clear();
			IsAnythingFilteredOut = false;
			return;
		}
		IEnumerable<T> enumerable = ItemsSource;
		foreach (IFilter<T> filter in _filters)
		{
			enumerable = filter.Filter(enumerable);
		}
		List<T> items = new List<T>();
		HashSet<T> itemsSet = new HashSet<T>();
		foreach (T enumerable2 in enumerable)
		{
			if (enumerable2 != null)
			{
				items.Add(enumerable2);
				itemsSet.Add(enumerable2);
			}
		}
		if (items.Count == 0)
		{
			FilteredItemsSource.Clear();
			BestMatchItem = default;
			IsAnythingFilteredOut = ItemsSource.Count > 0;
			return;
		}
		if (fullReset)
		{
			FilteredItemsSource.SetAll(items, fullRefresh: true);
		}
		else
		{
			for (int countOffset = FilteredItemsSource.Count - 1; countOffset >= 0; countOffset--)
			{
				T filteredItemsSource = FilteredItemsSource[countOffset];
				if (!itemsSet.Contains(filteredItemsSource))
				{
					FilteredItemsSource.RemoveAt(countOffset);
				}
			}
			HashSet<T> items2 = new HashSet<T>(FilteredItemsSource.Count);
			foreach (T filteredItemsSource2 in FilteredItemsSource)
			{
				items2.Add(filteredItemsSource2);
			}
			foreach (T localValue in items)
			{
				if (!items2.Contains(localValue))
				{
					FilteredItemsSource.Add(localValue);
					items2.Add(localValue);
				}
			}
		}
		Dictionary<T, int> indexLookup = new Dictionary<T, int>(ItemsSource.Count);
		for (int i = 0; i < ItemsSource.Count; i++)
		{
			T itemsSource = ItemsSource[i];
			if (itemsSource != null)
			{
				indexLookup[itemsSource] = i;
			}
		}
		FilteredItemsSource.Sort((T t1, T t2) => indexLookup[t1].CompareTo(indexLookup[t2]));
		BestMatchItem = ((FilteredItemsSource.Count > 0) ? FilteredItemsSource[0] : default(T));
		IsAnythingFilteredOut = FilteredItemsSource.Count != ItemsSource.Count;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnItemsSourceChanged(IReadOnlyList<T> oldValue, IReadOnlyList<T> newValue)
	{
		if (oldValue is INotifyCollectionChanged notifyCollectionChanged)
		{
			notifyCollectionChanged.CollectionChanged -= ItemsSourceCollectionChanged;
		}
		if (newValue is INotifyCollectionChanged notifyCollectionChanged2)
		{
			notifyCollectionChanged2.CollectionChanged += ItemsSourceCollectionChanged;
		}
		Refilter(fullReset: true);
	}
}
