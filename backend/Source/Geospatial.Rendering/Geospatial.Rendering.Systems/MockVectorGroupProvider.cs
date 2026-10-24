using System;
using Geospatial.Systems.Shapes;

namespace Geospatial.Rendering.Systems;

public class MockVectorGroupProvider : PoolProvider<MockVectorGroup>, IVectorGroupProvider
{
	public MockVectorGroupProvider()
		: base((Func<MockVectorGroup>)(() => new MockVectorGroup(100.0, 100.0)), 0)
	{
	}

	public void Return(IShape shape)
	{
		Return(shape as MockVectorGroup);
	}

	IVectorCanvas IVectorGroupProvider.Acquire()
	{
		return Acquire();
	}
}
