using System;

namespace RasMapperLib;

public class PlotLayerConsolidator : IDisposable
{
	private RASMapper _rasMapper;

	private bool disposedValue;

	public PlotLayerConsolidator(RASMapper rasMapper)
	{
		_rasMapper = rasMapper;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposedValue && disposing)
		{
			_ = _rasMapper;
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
