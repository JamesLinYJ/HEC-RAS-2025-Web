using System;
using Utility.VisualElements;

namespace Geospatial.Rendering.Systems;

public class MockImageProvider : PoolProvider<MockImage>, IImageProvider
{
	public readonly int FixedWidth;

	public readonly int FixedHeight;

	public MockImageProvider(int width, int height)
		: base((Func<MockImage>)(() => new MockImage(width, height)), 0)
	{
		FixedWidth = width;
		FixedHeight = height;
	}

	public IImage Acquire(double width, double height)
	{
		if (width != (double)FixedWidth || height != (double)FixedHeight)
		{
			throw new ArgumentException();
		}
		return Acquire();
	}

	public void Return(IImage label)
	{
		base.Return(label as MockImage);
	}
}
