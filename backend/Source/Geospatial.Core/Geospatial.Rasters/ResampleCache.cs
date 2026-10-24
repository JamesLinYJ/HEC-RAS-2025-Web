using System;
using System.Collections.Generic;

namespace Geospatial.Rasters;

public class ResampleCache : IDisposable
{
	public object SpatialCache;

	private List<IDisposable> _disposables;

	private bool _isDisposed;

	public void TrackDisposable(IDisposable disposable)
	{
		if (_disposables == null)
		{
			_disposables = new List<IDisposable>();
		}
		_disposables.Add(disposable);
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}
		_isDisposed = true;
		if (_disposables != null)
		{
			foreach (IDisposable disposable in _disposables)
			{
				disposable.Dispose();
			}
		}
		SpatialCache = null;
		_disposables = null;
	}

	~ResampleCache()
	{
		if (!_isDisposed)
		{
			_ = _disposables;
		}
	}
}
