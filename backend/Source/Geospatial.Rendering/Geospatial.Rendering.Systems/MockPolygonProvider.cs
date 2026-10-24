using System;
using Geospatial.Systems.Shapes;

namespace Geospatial.Rendering.Systems;

public class MockPolygonProvider : PoolProvider<MockPolygon>, IPolygonProvider
{
	public MockPolygonProvider()
		: base((Func<MockPolygon>)(() => new MockPolygon(100.0, 100.0)), 0)
	{
	}

	public void Return(IShape shape)
	{
		base.Return(shape as MockPolygon);
	}

	IPolygon IPolygonProvider.Acquire()
	{
		return Acquire();
	}
}
