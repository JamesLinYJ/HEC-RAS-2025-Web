using System;
using System.Collections.Generic;

namespace Utility.Memory;

public sealed class BufferPool<T> : IDisposable where T : unmanaged
{
	private const int SpecialSize = 65536;

	private static readonly Dictionary<Tuple<int, bool, T>, BufferPool<T>> _specialFillCase;

	private int _numOutstandingBuffers;

	private Stack<BufferHandle<T>> _availableBuffers;

	private readonly bool _ignoreDispose;

	public bool IsDisposed { get; private set; }

	public int BufferSize { get; }

	public bool InitializeBuffersToFillValue { get; }

	public T BufferFillValue { get; set; }

	public static BufferPool<T> RegisterStaticSpecialInstance(int size, bool shouldFill, T fillValue)
	{
		Tuple<int, bool, T> tuple = GetTuple(size, shouldFill, fillValue);
		lock (_specialFillCase)
		{
			if (_specialFillCase.TryGetValue(tuple, out var specialFillCase))
			{
				return specialFillCase;
			}
			specialFillCase = ((!shouldFill) ? new BufferPool<T>(ignoreDispose: true, size) : new BufferPool<T>(ignoreDispose: true, size, fillValue));
			_specialFillCase[tuple] = specialFillCase;
			return specialFillCase;
		}
	}

	private static Tuple<int, bool, T> GetTuple(int size, bool shouldFill, T fillValue)
	{
		if (shouldFill)
		{
			return new Tuple<int, bool, T>(size, shouldFill, fillValue);
		}
		return new Tuple<int, bool, T>(size, item2: false, default);
	}

	static BufferPool()
	{
		_specialFillCase = new Dictionary<Tuple<int, bool, T>, BufferPool<T>>();
		RegisterStaticSpecialInstance(65536, shouldFill: true, default);
		RegisterStaticSpecialInstance(65536, shouldFill: false, default);
		if (typeof(T) == typeof(float))
		{
			BufferPool<float>.RegisterStaticSpecialInstance(65536, true, -9999f);
		}
	}

	public static BufferPool<T> GetBufferPool(int size)
	{
		Tuple<int, bool, T> tuple = GetTuple(size, shouldFill: false, default);
		if (_specialFillCase.TryGetValue(tuple, out var localValue))
		{
			return localValue;
		}
		return new BufferPool<T>(size);
	}

	public static BufferPool<T> GetBufferPool(int size, T ndv)
	{
		Tuple<int, bool, T> tuple = GetTuple(size, shouldFill: true, ndv);
		if (_specialFillCase.TryGetValue(tuple, out var localValue))
		{
			return localValue;
		}
		return new BufferPool<T>(size, ndv);
	}

	public BufferPool(int bufferSize)
	{
		if (bufferSize <= 0)
		{
			throw new ArgumentOutOfRangeException("bufferSize");
		}
		BufferSize = bufferSize;
		_availableBuffers = new Stack<BufferHandle<T>>();
	}

	public BufferPool(int bufferSize, T fillValue)
		: this(bufferSize)
	{
		InitializeBuffersToFillValue = true;
		BufferFillValue = fillValue;
	}

	private BufferPool(bool ignoreDispose, int bufferSize)
	{
		if (bufferSize <= 0)
		{
			throw new ArgumentOutOfRangeException("bufferSize");
		}
		_ignoreDispose = ignoreDispose;
		BufferSize = bufferSize;
		_availableBuffers = new Stack<BufferHandle<T>>();
	}

	public BufferPool(bool ignoreDispose, int bufferSize, T fillValue)
		: this(bufferSize)
	{
		_ignoreDispose = ignoreDispose;
		InitializeBuffersToFillValue = true;
		BufferFillValue = fillValue;
	}

	public BufferHandle<T> Acquire()
	{
		if (IsDisposed)
		{
			throw new ObjectDisposedException("BufferPool");
		}
		BufferHandle<T> bufferHandle = null;
		bool initializeBuffersToFillValue = InitializeBuffersToFillValue;
		lock (_availableBuffers)
		{
			if (_availableBuffers.Count > 0)
			{
				bufferHandle = _availableBuffers.Pop();
			}
			else
			{
				bufferHandle = new BufferHandle<T>(this);
				if (InitializeBuffersToFillValue && BufferFillValue.Equals(default(T)))
				{
					initializeBuffersToFillValue = false;
				}
			}
			_numOutstandingBuffers++;
		}
		if (initializeBuffersToFillValue)
		{
			bufferHandle.SetData(BufferFillValue);
		}
		return bufferHandle;
	}

	public void Return(BufferHandle<T> handle)
	{
		lock (_availableBuffers)
		{
			_availableBuffers.Push(handle);
			_numOutstandingBuffers--;
		}
	}

	public void Dispose()
	{
		if (!_ignoreDispose)
		{
			IsDisposed = true;
			_availableBuffers = null;
		}
	}
}
