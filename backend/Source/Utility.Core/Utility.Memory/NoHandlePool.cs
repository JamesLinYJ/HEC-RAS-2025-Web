using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Memory;

public sealed class NoHandlePool<T>
{
	private static readonly ILogger _logger = LogHelper.GetLogger("NoHandlePool");

	private int _numOnLoan;

	private readonly Stack<T> _availableElements;

	private readonly Func<T> _generator;

	public int MaxResidentAllowed = int.MaxValue;

	public Action<T> OnEvicted;

	public int ElementsOnLoan => _numOnLoan;

	public int ElementsAvailable
	{
		get
		{
			lock (_availableElements)
			{
				return _availableElements.Count;
			}
		}
	}

	public NoHandlePool(Func<T> generator, int initialCapacity = 0)
	{
		_generator = generator;
		_availableElements = new Stack<T>(initialCapacity);
	}

	public T Acquire()
	{
		lock (_availableElements)
		{
			return Acquire_NotThreadsafe();
		}
	}

	public T Acquire_NotThreadsafe()
	{
		T result = ((_availableElements.Count <= 0) ? _generator() : _availableElements.Pop());
		_numOnLoan++;
		return result;
	}

	public void Return(T data)
	{
		lock (_availableElements)
		{
			Return_NotThreadsafe(data);
		}
	}

	public void Return_NotThreadsafe(T data)
	{
		_numOnLoan--;
		if (_availableElements.Count > MaxResidentAllowed)
		{
			OnEvicted?.Invoke(data);
		}
		else
		{
			_availableElements.Push(data);
		}
	}

	public void ClearAll()
	{
		lock (_availableElements)
		{
			_numOnLoan -= _availableElements.Count;
			_availableElements.Clear();
		}
	}

	public void ClearAll(Action<T> cleanup)
	{
		lock (_availableElements)
		{
			foreach (T availableElement in _availableElements)
			{
				cleanup(availableElement);
			}
			_numOnLoan -= _availableElements.Count;
			_availableElements.Clear();
		}
	}

	public void ExecuteOnAvailableElements(Action<T> action)
	{
		lock (_availableElements)
		{
			foreach (T availableElement in _availableElements)
			{
				action(availableElement);
			}
		}
	}
}
