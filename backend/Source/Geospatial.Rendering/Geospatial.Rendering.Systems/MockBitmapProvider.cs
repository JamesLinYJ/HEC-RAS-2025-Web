using System;
using Utility.VisualElements;

namespace Geospatial.Rendering.Systems;

public class MockBitmapProvider : PoolProvider<MockBitmap>, IBitmapProvider
{
	public readonly int FixedWidth;

	public readonly int FixedHeight;

	public MockBitmapProvider(int width, int height)
		: base((Func<MockBitmap>)(() => new MockBitmap(new int[width * height])), 0)
	{
		FixedWidth = width;
		FixedHeight = height;
	}

	public IBitmap Acquire(int width, int height)
	{
		if (width != FixedWidth || height != FixedHeight)
		{
			throw new ArgumentException();
		}
		return Acquire();
	}

	public void Return(IBitmap bmp)
	{
		base.Return(bmp as MockBitmap);
	}
}
