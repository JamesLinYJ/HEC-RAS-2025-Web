using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Utility.Memory;

public class ArrayCacheManager<TKey, TValue> : IArrayCacheManager<TKey, TValue>, IDisposable where TValue : unmanaged
{
	private readonly CacheManager<TKey, IMemoryOwner<TValue>> _cacheManager;

	private bool _isDisposed;

	public string LogIdentifier
	{
		get
		{
			return _cacheManager.LogIdentifier;
		}
		set
		{
			_cacheManager.LogIdentifier = value;
		}
	}

	public int MaxCacheSizeBytes
	{
		get
		{
			return _cacheManager.MaxCacheSizeBytes;
		}
		set
		{
			_cacheManager.MaxCacheSizeBytes = value;
		}
	}

	public CacheManager<TKey, IMemoryOwner<TValue>> CacheManager => _cacheManager;

	public ArrayCacheManager()
	{
		int sizeoft = Marshal.SizeOf<TValue>();
		_cacheManager = new CacheManager<TKey, IMemoryOwner<TValue>>((IMemoryOwner<TValue> tvHandle) => (tvHandle != null) ? (tvHandle.Memory.Length * sizeoft) : 0);
		_cacheManager.OnItemEvicted += DisposeHandle;
	}

	private static void DisposeHandle(TKey key, IMemoryOwner<TValue> handle)
	{
		handle?.Dispose();
	}

	public void BuildOrUseExisting(TKey key, int size, Action<Memory<TValue>> populate, Action<Memory<TValue>> operate)
	{
		try
		{
			IMemoryOwner<TValue> orMakeAndPin_EnsureSingleGeneration = _cacheManager.GetOrMakeAndPin_EnsureSingleGeneration(key, (size, populate), ((int size, Action<Memory<TValue>> populate) state) =>
			{
				IMemoryOwner<TValue> memoryOwner = SharedMemoryPool<TValue>.Rent(state.size, out var mem);
				try
				{
					state.populate(mem);
					return memoryOwner;
				}
				catch
				{
					memoryOwner.Dispose();
					throw;
				}
			});
			operate(orMakeAndPin_EnsureSingleGeneration.Memory.Slice(0, size));
		}
		catch
		{
		}
		finally
		{
			_cacheManager.Unpin(key);
		}
	}

	public void BuildOrUseExisting<TModel>(TKey key, int size, TModel model, Action<TModel, Memory<TValue>> populate, Action<TModel, Memory<TValue>> operate)
	{
		try
		{
			IMemoryOwner<TValue> orMakeAndPin_EnsureSingleGeneration = _cacheManager.GetOrMakeAndPin_EnsureSingleGeneration(key, (size, model, populate), ((int size, TModel model, Action<TModel, Memory<TValue>> populate) state) =>
			{
				IMemoryOwner<TValue> memoryOwner = SharedMemoryPool<TValue>.Rent(state.size, out var mem);
				try
				{
					state.populate(state.model, mem);
					return memoryOwner;
				}
				catch
				{
					memoryOwner.Dispose();
					throw;
				}
			});
			operate(model, orMakeAndPin_EnsureSingleGeneration.Memory.Slice(0, size));
		}
		catch
		{
		}
		finally
		{
			_cacheManager.Unpin(key);
		}
	}

	public bool TryUseExistingOrBuildPinned<TModel>(TKey key, int size, TModel model, Action<TModel, Memory<TValue>> populate, out ReadOnlyMemory<TValue> data)
	{
		try
		{
			IMemoryOwner<TValue> orMakeAndPin_EnsureSingleGeneration = _cacheManager.GetOrMakeAndPin_EnsureSingleGeneration(key, () =>
			{
				IMemoryOwner<TValue> memoryOwner = SharedMemoryPool<TValue>.Rent(size, out var mem);
				try
				{
					populate(model, mem);
					return memoryOwner;
				}
				catch
				{
					memoryOwner.Dispose();
					throw;
				}
			});
			data = orMakeAndPin_EnsureSingleGeneration.Memory.Slice(0, size);
			return true;
		}
		catch
		{
			_cacheManager.Unpin(key);
			data = default;
			return false;
		}
	}

	public void Unpin(TKey key)
	{
		_cacheManager.Unpin(key);
	}

	public void BuildOrUseExisting(TKey key, int size, Func<Memory<TValue>, bool> populate, Action<Memory<TValue>, bool> operate)
	{
		try
		{
			IMemoryOwner<TValue> orMakeAndPin_EnsureSingleGeneration = _cacheManager.GetOrMakeAndPin_EnsureSingleGeneration(key, (size, populate), ((int size, Func<Memory<TValue>, bool> populate) state) =>
			{
				IMemoryOwner<TValue> memoryOwner = SharedMemoryPool<TValue>.Rent(state.size, out var mem);
				bool populate2;
				try
				{
					populate2 = state.populate(mem);
				}
				catch
				{
					memoryOwner.Dispose();
					throw;
				}
				if (populate2)
				{
					return memoryOwner;
				}
				memoryOwner.Dispose();
				return (IMemoryOwner<TValue>)null;
			});
			if (orMakeAndPin_EnsureSingleGeneration == null)
			{
				operate(Memory<TValue>.Empty, arg2: false);
			}
			else
			{
				operate(orMakeAndPin_EnsureSingleGeneration.Memory.Slice(0, size), arg2: true);
			}
		}
		catch
		{
		}
		finally
		{
			_cacheManager.Unpin(key);
		}
	}

	public void Touch(TKey key, int size, Action<Memory<TValue>> populate)
	{
		BuildOrUseExisting(key, size, populate, (Memory<TValue> nop) =>
		{
		});
	}

	public void ForceEvictAll()
	{
		_cacheManager.ForceEvictAll();
	}

	public IArrayCacheManager<TKeyTransform, TValue> GetView<TKeyTransform>(Func<TKeyTransform, TKey> keyTransformer)
	{
		return new ArrayCacheView<TKey, TKeyTransform, TValue>(this, keyTransformer);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				_cacheManager.OnItemEvicted -= DisposeHandle;
				_cacheManager.ForceEvictAll();
			}
			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
