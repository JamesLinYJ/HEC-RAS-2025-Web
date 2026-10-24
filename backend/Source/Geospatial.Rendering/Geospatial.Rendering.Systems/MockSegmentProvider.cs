using System;
using Geospatial.Systems.Shapes;

namespace Geospatial.Rendering.Systems;

public class MockSegmentProvider : PoolProvider<MockSegment>, ISegmentProvider
{
	public MockSegmentProvider()
		: base((Func<MockSegment>)(() => new MockSegment(100.0, 100.0)), 0)
	{
	}

	public void Return(IShape shape)
	{
		base.Return(shape as MockSegment);
	}

	ISegment ISegmentProvider.Acquire()
	{
		return Acquire();
	}
}
