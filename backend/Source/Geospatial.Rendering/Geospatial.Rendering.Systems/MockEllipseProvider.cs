using System;
using Geospatial.Systems.Shapes;

namespace Geospatial.Rendering.Systems;

public class MockEllipseProvider : PoolProvider<MockEllipse>, IEllipseProvider
{
	public MockEllipseProvider()
		: base((Func<MockEllipse>)(() => new MockEllipse(100.0, 100.0)), 0)
	{
	}

	public void Return(IShape shape)
	{
		base.Return(shape as MockEllipse);
	}

	IEllipse IEllipseProvider.Acquire()
	{
		return Acquire();
	}
}
