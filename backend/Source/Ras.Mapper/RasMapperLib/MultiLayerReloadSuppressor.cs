using System;
using System.Collections.Generic;

namespace RasMapperLib;

public class MultiLayerReloadSuppressor : IDisposable
{
	private List<FeatureLayer> _ftls;

	private bool disposedValue;

	public MultiLayerReloadSuppressor(List<FeatureLayer> ftls)
	{
		_ftls = ftls;
		foreach (FeatureLayer ftl in _ftls)
		{
			ftl.SuppressTableReload(boolValue: true);
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposedValue && disposing)
		{
			foreach (FeatureLayer ftl in _ftls)
			{
				ftl.SuppressTableReload(boolValue: false);
			}
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
