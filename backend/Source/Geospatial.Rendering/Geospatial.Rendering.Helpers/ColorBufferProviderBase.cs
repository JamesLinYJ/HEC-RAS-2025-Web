using System;
using Utility.Memory;

namespace Geospatial.Rendering.Helpers;

public abstract class ColorBufferProviderBase
{
	private ByteBackedIntMemory _byteBackedMemory;

	private Memory<int> _clrBuffer;

	private bool _loadedColorBuffer;

	public Memory<int> ColorBufferARGB => TryCheckLoadColorBuffer();

	public byte[] ColorBufferBytes => _byteBackedMemory?.Bytes;

	protected abstract int ColorBufferSize { get; }

	public ColorBufferProviderBase()
	{
	}

	public ColorBufferProviderBase(Memory<int> loanedColorBuffer)
	{
		_clrBuffer = loanedColorBuffer;
		_loadedColorBuffer = true;
	}

	protected void DisposeColorBuffer()
	{
		_byteBackedMemory?.Dispose();
		_byteBackedMemory = null;
		_clrBuffer = default;
		_loadedColorBuffer = false;
	}

	private Memory<int> TryCheckLoadColorBuffer()
	{
		RentColorBuffer();
		return _clrBuffer;
	}

	private void RentColorBuffer()
	{
		if (!_loadedColorBuffer)
		{
			_loadedColorBuffer = true;
			_byteBackedMemory = new ByteBackedIntMemory(ColorBufferSize);
			_clrBuffer = _byteBackedMemory.Memory.Slice(0, ColorBufferSize);
		}
	}
}
