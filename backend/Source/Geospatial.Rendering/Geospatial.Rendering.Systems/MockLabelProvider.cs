using System;
using Utility.VisualElements;

namespace Geospatial.Rendering.Systems;

public class MockLabelProvider : PoolProvider<MockLabel>, ILabelProvider
{
	public MockLabelProvider()
		: base((Func<MockLabel>)(() => new MockLabel()), 0)
	{
	}

	ILabel ILabelProvider.Acquire()
	{
		return Acquire();
	}

	public void Return(ILabel label)
	{
		base.Return(label as MockLabel);
	}
}
