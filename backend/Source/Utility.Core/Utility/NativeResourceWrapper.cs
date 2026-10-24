using System;
using System.Threading.Tasks;

namespace Utility;

public class NativeResourceWrapper<T> : IDisposable where T : IDisposable
{
	private T _resource;

	private Func<T> _factory;

	private NativeResourceMode _resourceMode;

	private bool _isDisposed;

	public NativeResourceMode ResourceMode => _resourceMode;

	public bool OpensOnDemand => _resourceMode == NativeResourceMode.OpenOnDemand;

	public bool IsDisposed => _isDisposed;

	public T ResourceIfExists => _resource;

	public static NativeResourceWrapper<T> Loan(T resource)
	{
		return new NativeResourceWrapper<T>(resource);
	}

	public static NativeResourceWrapper<T> Loan(NativeResourceWrapper<T> resource)
	{
		return new NativeResourceWrapper<T>(resource._resource, (resource.ResourceMode != NativeResourceMode.KeepOpen) ? resource.ResourceMode : NativeResourceMode.OnLoan)
		{
			_factory = resource._factory
		};
	}

	public NativeResourceWrapper<T> CopyAsLoanedResource()
	{
		return Loan(this);
	}

	public static NativeResourceWrapper<T> Give(T resource)
	{
		return new NativeResourceWrapper<T>(resource, NativeResourceMode.KeepOpen);
	}

	private NativeResourceWrapper(T resource, NativeResourceMode resMode)
	{
		_resource = resource;
		_resourceMode = resMode;
	}

	public static implicit operator NativeResourceWrapper<T>(T resource)
	{
		return Loan(resource);
	}

	public NativeResourceWrapper(Func<T> factory)
	{
		_factory = factory;
		_resourceMode = NativeResourceMode.OpenOnDemand;
	}

	public NativeResourceWrapper(T resource)
	{
		_resource = resource;
		_resourceMode = NativeResourceMode.OnLoan;
	}

	public void UseResource(Action<T> readAction)
	{
		if (_resourceMode == NativeResourceMode.OpenOnDemand)
		{
			using (T factory = _factory())
			{
				readAction(factory);
				return;
			}
		}
		readAction(_resource);
	}

	public TOut UseResource<TOut>(Func<T, TOut> readAction)
	{
		if (_resourceMode == NativeResourceMode.OpenOnDemand)
		{
			using (T factory = _factory())
			{
				return readAction(factory);
			}
		}
		return readAction(_resource);
	}

	public async Task<TOut> UseResourceAsync<TOut>(Func<T, TOut> readAction)
	{
		if (_resourceMode == NativeResourceMode.OpenOnDemand)
		{
			return await Task.Run(() =>
			{
				using T factory = _factory();
				return readAction(factory);
			});
		}
		return await Task.Run(() => readAction(_resource));
	}

	public void ChangeToKeepOpen(T newResource)
	{
		if (_resourceMode == NativeResourceMode.KeepOpen)
		{
			T resource = _resource;
			resource?.Dispose();
		}
		else if (_resourceMode == NativeResourceMode.OpenOnDemand)
		{
			_factory = null;
		}
		else
		{
			_ = _resourceMode;
		}
		_resourceMode = NativeResourceMode.KeepOpen;
		_resource = newResource;
	}

	public void ChangeToOpenOnDemand(Func<T> newFactory)
	{
		if (_resourceMode == NativeResourceMode.KeepOpen)
		{
			T resource = _resource;
			resource?.Dispose();
			_resource = default;
		}
		else if (_resourceMode == NativeResourceMode.OpenOnDemand)
		{
			_factory = null;
		}
		else
		{
			_ = _resourceMode;
		}
		_resourceMode = NativeResourceMode.OpenOnDemand;
		_factory = newFactory;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		if (disposing && _resourceMode == NativeResourceMode.KeepOpen)
		{
			scoped ref T reference = ref _resource;
			T reference2 = default;
			if (reference2 == null)
			{
				reference2 = reference;
				reference = ref reference2;
				if (reference2 == null)
				{
					goto IL_0048;
				}
			}
			reference.Dispose();
		}
		goto IL_0048;
		IL_0048:
		_isDisposed = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
public class NativeResourceWrapper<TInput, T> : IDisposable where T : IDisposable
{
	private readonly T _resource;

	private readonly Func<TInput, T> _factory;

	private readonly NativeResourceMode _resourceMode;

	private bool _isDisposed;

	public NativeResourceMode ResourceMode => _resourceMode;

	public bool OpenOnDemand => _resourceMode == NativeResourceMode.OpenOnDemand;

	public bool IsDisposed => _isDisposed;

	public T ResourceIfExists => _resource;

	public static NativeResourceWrapper<T> Loan(T resource)
	{
		return new NativeResourceWrapper<T>(resource);
	}

	public static NativeResourceWrapper<TInput, T> Give(T resource)
	{
		return new NativeResourceWrapper<TInput, T>(resource, NativeResourceMode.KeepOpen);
	}

	private NativeResourceWrapper(T resource, NativeResourceMode resMode)
	{
		_resource = resource;
		_resourceMode = resMode;
	}

	public NativeResourceWrapper(Func<TInput, T> factory)
	{
		_factory = factory;
		_resourceMode = NativeResourceMode.OpenOnDemand;
	}

	public NativeResourceWrapper(T resource)
	{
		_resource = resource;
		_resourceMode = NativeResourceMode.OnLoan;
	}

	public void UseResource(TInput input, Action<T> readAction)
	{
		if (_resourceMode == NativeResourceMode.OpenOnDemand)
		{
			using (T factory = _factory(input))
			{
				readAction(factory);
				return;
			}
		}
		readAction(_resource);
	}

	public TOut UseResource<TOut>(TInput input, Func<T, TOut> readAction)
	{
		if (_resourceMode == NativeResourceMode.OpenOnDemand)
		{
			using (T factory = _factory(input))
			{
				return readAction(factory);
			}
		}
		return readAction(_resource);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		if (disposing && _resourceMode == NativeResourceMode.KeepOpen)
		{
			T resource = _resource;
			if (resource != null)
			{
				resource.Dispose();
			}
		}
		_isDisposed = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
