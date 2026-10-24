using System;
using System.Collections.Generic;

namespace Utility.DataStructures;

public class PriorityDictionary<TElement, TPriority> where TPriority : IComparable<TPriority>, IEquatable<TPriority>
{
	private struct PrioritySerial(TPriority pri, int ser)
	{
		public TPriority priority = pri;

		public int serial = ser;
	}

	private int serial;

	private Dictionary<TElement, PrioritySerial> _dict;

	private PriorityQueue<(TElement, int), TPriority> _queue;

	private TElement nullElement;

	private TPriority nullPriority;

	public int Count => _dict.Count;

	public PriorityDictionary(TElement nullElement, TPriority nullPriority)
	{
		_dict = new Dictionary<TElement, PrioritySerial>();
		_queue = new PriorityQueue<(TElement, int), TPriority>();
		this.nullElement = nullElement;
		this.nullPriority = nullPriority;
	}

	public void Add(TElement elt, TPriority newPriority)
	{
		if (!_dict.TryGetValue(elt, out var localValue) || !localValue.priority.Equals(newPriority))
		{
			serial++;
			_dict[elt] = new PrioritySerial(newPriority, serial);
			_queue.Enqueue((elt, serial), newPriority);
		}
	}

	public TElement Dequeue(out TPriority priority)
	{
		if (TryDequeue(out var elt, out priority))
		{
			return elt;
		}
		throw new Exception("Priority queue is empty");
	}

	public bool TryDequeue(out TElement elt, out TPriority priority)
	{
		while (_queue.Count > 0)
		{
			if (!_queue.TryDequeue(out var element, out priority))
			{
				throw new Exception("Priority queue failure");
			}
			if (_dict.ContainsKey(element.Item1) && _dict[element.Item1].serial == element.Item2)
			{
				_dict.Remove(element.Item1);
				(elt, _) = element;
				return true;
			}
		}
		elt = nullElement;
		priority = nullPriority;
		return false;
	}

	public void Remove(TElement elt)
	{
		_dict.Remove(elt);
	}

	public bool Contains(TElement elt)
	{
		return _dict.ContainsKey(elt);
	}
}
