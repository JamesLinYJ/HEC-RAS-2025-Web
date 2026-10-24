using System;

namespace Utility.Memory;

public class BufferHandle<T> : IDisposable where T : unmanaged
{
	private readonly BufferPool<T> _manager;

	private readonly T[] _data;

	public T[] Buffer => _data;

	public BufferHandle(BufferPool<T> manager)
	{
		if (manager == null)
		{
			throw new ArgumentException("manager");
		}
		_manager = manager;
		_data = new T[_manager.BufferSize];
	}

	public void SetData(T fillValue)
	{
		_data.AsSpan().Fill(fillValue);
	}

	public void Dispose()
	{
		_manager.Return(this);
	}

	~BufferHandle()
	{
		_ = _manager.IsDisposed;
	}
}
