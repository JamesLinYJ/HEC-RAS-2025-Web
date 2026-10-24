using System;
using System.Diagnostics;
using Geospatial.Rendering.Helpers;
using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering.TiledRenderingSystem;

[DebuggerDisplay("{Layer.Name}")]
public class Frame : ColorBufferProviderBase, IDisposable
{
	private bool _isDisposed;

	private int _colorBufferSize;

	public IRenderLayer Layer { get; set; }

	public long LastRenderState { get; set; }

	protected override int ColorBufferSize => _colorBufferSize;

	public Frame(IRenderLayer layer, int frameSize)
	{
		Layer = layer;
		LastRenderState = -1L;
		_colorBufferSize = frameSize;
	}

	public bool IsValid()
	{
		if (Layer == null)
		{
			return true;
		}
		return Layer.GetRenderState() == LastRenderState;
	}

	~Frame()
	{
		try
		{
			if (!_isDisposed)
			{
				Dispose(disposing: false);
			}
		}
		catch
		{
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			DisposeColorBuffer();
			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
