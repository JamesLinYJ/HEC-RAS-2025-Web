using System;
using Geospatial.Systems.Shapes;

namespace Geospatial.Rendering.Systems;

public class MockLineProvider : PoolProvider<MockLine>, ILineProvider
{
	public MockLineProvider()
		: base((Func<MockLine>)(() => new MockLine(100.0, 100.0)), 0)
	{
	}

	public void Return(IShape shape)
	{
		base.Return(shape as MockLine);
	}

	ILine ILineProvider.Acquire()
	{
		return Acquire();
	}
}
