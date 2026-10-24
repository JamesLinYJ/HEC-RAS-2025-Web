using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Utility;
using Utility.DataStructures;

public class NotifyUpdateableDoubleCollection : IReadOnlyNotifyCollection<double>, IReadOnlyList<double>, IEnumerable<double>, IEnumerable, IReadOnlyCollection<double>, INotifyCollectionChanged, IDisposable
{
	private List<double> _values;

	private Func<IReadOnlyList<double>> _getValuesFunc;

	private Func<double> _appendValueFunc;

	private Dictionary<(object, string), WeakSubscriptionHandle> _updateSubscriptions = new Dictionary<(object, string), WeakSubscriptionHandle>();

	private Dictionary<(object, string), WeakSubscriptionHandle> _appendSubscriptions = new Dictionary<(object, string), WeakSubscriptionHandle>();

	public double this[int index] => _values[index];

	public int Count => _values.Count;

	public event NotifyCollectionChangedEventHandler CollectionChanged;

	public NotifyUpdateableDoubleCollection(Func<IReadOnlyList<double>> getValuesFunc, Func<double> appendValueFunc = null)
	{
		_updateSubscriptions = new Dictionary<(object, string), WeakSubscriptionHandle>();
		_values = new List<double>();
		_getValuesFunc = getValuesFunc;
		_appendValueFunc = appendValueFunc;
		Update();
	}

	public void Update()
	{
		_values.Clear();
		IReadOnlyList<double> readOnlyList = _getValuesFunc();
		if (readOnlyList != null)
		{
			_values.AddRange(readOnlyList);
		}
		if (CollectionChanged != null)
		{
			CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}
	}

	private void Append()
	{
		_values.Add(_appendValueFunc());
		if (CollectionChanged != null)
		{
			CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}
	}

	public IEnumerator<double> GetEnumerator()
	{
		for (int i = 0; i < _values.Count; i++)
		{
			yield return _values[i];
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public void SubscribeUpdateToEvent(object publisher, string eventName)
	{
		_updateSubscriptions[(publisher, eventName)] = WeakSubscription.Subscribe(publisher, eventName, Update);
	}

	public void UnsubscribeUpdateFromEvent(object publisher, string eventName)
	{
		(object, string) key = (publisher, eventName);
		if (_updateSubscriptions.ContainsKey(key))
		{
			_updateSubscriptions[key].Unsubscribe();
			_updateSubscriptions.Remove(key);
		}
	}

	public void SubscribeAppendToEvent(object publisher, string eventName)
	{
		if (_appendValueFunc == null)
		{
			throw new Exception("This collection has not defined an add function");
		}
		_appendSubscriptions[(publisher, eventName)] = WeakSubscription.Subscribe(publisher, eventName, Append);
	}

	public void UnsubscribeAppendFromEvent(object publisher, string eventName)
	{
		(object, string) key = (publisher, eventName);
		if (_appendSubscriptions.ContainsKey(key))
		{
			_appendSubscriptions[key].Unsubscribe();
			_appendSubscriptions.Remove(key);
		}
	}

	public void Dispose()
	{
		foreach (WeakSubscriptionHandle weakSubscriptionHandle in _updateSubscriptions.Values)
		{
			weakSubscriptionHandle.Unsubscribe();
		}
		foreach (WeakSubscriptionHandle weakSubscriptionHandle2 in _appendSubscriptions.Values)
		{
			weakSubscriptionHandle2.Unsubscribe();
		}
	}
}
