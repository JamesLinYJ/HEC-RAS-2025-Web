using System;

namespace Geospatial;

public class FreezeHandle : IDisposable
{
	private ISpatialVersion _item;

	private bool _isDisposed;

	public FreezeHandle(ISpatialVersion iSpatialVersion)
	{
		_item = iSpatialVersion;
		_item.FreezeOne();
	}

	~FreezeHandle()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	private void Dispose(bool disposing)
	{
		if (!_isDisposed && disposing)
		{
			_item.ThawOne();
			_isDisposed = true;
		}
	}

	public static K ExecuteFrozen<T, K>(ISpatialVersion iSpatialVersion, T model, Func<T, K> action)
	{
		try
		{
			iSpatialVersion.FreezeOne();
			return action(model);
		}
		finally
		{
			iSpatialVersion.ThawOne();
		}
	}
}
