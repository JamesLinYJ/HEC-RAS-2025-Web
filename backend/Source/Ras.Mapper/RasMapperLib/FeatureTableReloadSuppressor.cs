using System;

namespace RasMapperLib;

public class FeatureTableReloadSuppressor : IDisposable
{
	private FeatureLayer _ftl;

	private bool disposedValue;

	public FeatureTableReloadSuppressor(FeatureLayer ftl)
	{
		_ftl = ftl;
		_ftl.SuppressTableReload(boolValue: true);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposedValue && disposing)
		{
			_ftl.SuppressTableReload(boolValue: false);
		}
		disposedValue = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
	}

	void IDisposable.Dispose()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Dispose
		this.Dispose();
	}
}
