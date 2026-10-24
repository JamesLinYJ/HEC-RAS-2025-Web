using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Memory;

public sealed class Pool<T>
{
	private static readonly ILogger _logger = LogHelper.GetLogger("Pool");

	private int _numOnLoan;

	private readonly Stack<PoolHandle<T>> _availableElements;

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

	public Pool(Func<T> generator)
	{
		_generator = generator;
		_availableElements = new Stack<PoolHandle<T>>();
	}

	public PoolHandle<T> Acquire()
	{
		lock (_availableElements)
		{
			return Acquire_NotThreadsafe();
		}
	}

	public PoolHandle<T> Acquire_NotThreadsafe()
	{
		PoolHandle<T> poolHandle = null;
		poolHandle = ((_availableElements.Count <= 0) ? new PoolHandle<T>(this, _generator()) : _availableElements.Pop());
		_numOnLoan++;
		return poolHandle;
	}

	public void Return(PoolHandle<T> handle)
	{
		lock (_availableElements)
		{
			Return_NotThreadsafe(handle);
		}
	}

	public void Return_NotThreadsafe(PoolHandle<T> handle)
	{
		_numOnLoan--;
		if (_availableElements.Count > MaxResidentAllowed)
		{
			OnEvicted?.Invoke(handle.Data);
			handle.DisposeWithoutReturning();
		}
		else
		{
			_availableElements.Push(handle);
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
			foreach (PoolHandle<T> availableElement in _availableElements)
			{
				cleanup(availableElement.Data);
			}
			_numOnLoan -= _availableElements.Count;
			_availableElements.Clear();
		}
	}

	public void ExecuteOnAvailableElements(Action<T> action)
	{
		lock (_availableElements)
		{
			foreach (PoolHandle<T> availableElement in _availableElements)
			{
				action(availableElement.Data);
			}
		}
	}
}
