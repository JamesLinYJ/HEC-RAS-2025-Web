using System;

namespace RasMapperLib;

public class FeatureChangedSuppressor : IDisposable
{
	private FeatureLayer _ftl;

	private bool disposedValue;

	public FeatureChangedSuppressor(FeatureLayer ftl)
	{
		_ftl = ftl;
		_ftl.SuppressFeatureChanged(boolValue: true);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposedValue && disposing)
		{
			_ftl.SuppressFeatureChanged(boolValue: false);
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
