using System;

namespace Utility.Memory;

public class PoolHandle<T> : IDisposable
{
	private readonly Pool<T> _manager;

	private readonly T _data;

	public T Data => _data;

	public PoolHandle(Pool<T> manager, T data)
	{
		if (manager == null)
		{
			throw new ArgumentException("manager");
		}
		_manager = manager;
		_data = data;
	}

	public void Dispose()
	{
		_manager.Return(this);
	}

	public void Dispose_NotThreafsafe()
	{
		_manager.Return_NotThreadsafe(this);
	}

	internal void DisposeWithoutReturning()
	{
	}

	~PoolHandle()
	{
	}
}
