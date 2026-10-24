using System;
using System.Threading.Tasks;
using Utility.VisualElements;

namespace Geospatial.Rendering.Systems;

public class MockBitmap : IBitmap
{
	private readonly int[] _bmpProxy;

	public bool HasData { get; set; }

	public MockBitmap(int[] bmp)
	{
		_bmpProxy = bmp;
	}

	public unsafe void GetUnmanagedPointer(Action<nint> action)
	{
		fixed (int* bmpProxy = _bmpProxy)
		{
			action((nint)bmpProxy);
		}
	}

	public void RenderColorBufferToBitmap(Memory<int> colorBuffer)
	{
		colorBuffer.CopyTo(_bmpProxy);
	}

	public void RenderColorBufferToBitmap(byte[] colorBuffer)
	{
	}

	public ValueTask RenderColorBufferToBitmapAsync(ReadOnlyMemory<byte> colorBuffer)
	{
		return ValueTask.CompletedTask;
	}
}
