using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;

namespace Utility.NumericalAnalysis;

public sealed class TimeValueRingBuffer<T> : IReadOnlyList<(double Time, T Value)>, IEnumerable<(double Time, T Value)>, IEnumerable, IReadOnlyCollection<(double Time, T Value)> where T : INumber<T>
{
	private readonly double[] _times;

	private readonly T[] _values;

	private readonly int _capacity;

	private int _head;

	private int _count;

	private T _min;

	private T _max;

	private bool _hasStats;

	public double TimeCap { get; }

	public int Capacity => _capacity;

	public int Count => _count;

	public bool IsEmpty => _count == 0;

	public bool HasStats => _hasStats;

	public T Min => _min;

	public T Max => _max;

	public T ValueSpan
	{
		get
		{
			if (!_hasStats)
			{
				return T.Zero;
			}
			return _max - _min;
		}
	}

	public (double Time, T Value) this[int i]
	{
		get
		{
			if ((uint)i >= (uint)_count)
			{
				throw new ArgumentOutOfRangeException("i");
			}
			int capacitySum = _head + i;
			if (capacitySum >= _capacity)
			{
				capacitySum -= _capacity;
			}
			return (Time: _times[capacitySum], Value: _values[capacitySum]);
		}
	}

	public double LastTime
	{
		get
		{
			if (_count != 0)
			{
				return _times[NewestIndex];
			}
			return double.NaN;
		}
	}

	public T LastValue => _values[NewestIndex];

	private int NewestIndex
	{
		get
		{
			int capacitySum = _head + _count - 1;
			if (capacitySum >= _capacity)
			{
				capacitySum -= _capacity;
			}
			return capacitySum;
		}
	}

	public TimeValueRingBuffer(int capacity, double timeCap = double.PositiveInfinity)
	{
		if (capacity < 1)
		{
			throw new ArgumentOutOfRangeException("capacity", "Capacity must be >= 1.");
		}
		_capacity = capacity;
		TimeCap = timeCap;
		_times = new double[capacity];
		_values = new T[capacity];
	}

	public void Add(double time, T localValue)
	{
		bool condition = false;
		while (_count > 0 && _times[NewestIndex] >= time)
		{
			_count--;
			condition = true;
		}
		int capacitySum = _head + _count;
		if (capacitySum >= _capacity)
		{
			capacitySum -= _capacity;
		}
		_times[capacitySum] = time;
		_values[capacitySum] = localValue;
		_count++;
		while (_count > _capacity)
		{
			_head = Next(_head);
			_count--;
			condition = true;
		}
		while (_count > 1 && time - _times[_head] > TimeCap)
		{
			_head = Next(_head);
			_count--;
			condition = true;
		}
		if (condition)
		{
			RecomputeStats();
		}
		else
		{
			AddToStats(localValue);
		}
	}

	public void Clear()
	{
		_head = 0;
		_count = 0;
		_hasStats = false;
	}

	private int Next(int i)
	{
		if (i + 1 != _capacity)
		{
			return i + 1;
		}
		return 0;
	}

	private void AddToStats(T localValue)
	{
		if (!T.IsFinite(localValue))
		{
			return;
		}
		if (!_hasStats)
		{
			_min = localValue;
			_max = localValue;
			_hasStats = true;
			return;
		}
		if (localValue < _min)
		{
			_min = localValue;
		}
		if (localValue > _max)
		{
			_max = localValue;
		}
	}

	private void RecomputeStats()
	{
		_hasStats = false;
		for (int i = 0; i < _count; i++)
		{
			AddToStats(this[i].Value);
		}
	}

	public IEnumerator<(double Time, T Value)> GetEnumerator()
	{
		for (int i = 0; i < _count; i++)
		{
			yield return this[i];
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
