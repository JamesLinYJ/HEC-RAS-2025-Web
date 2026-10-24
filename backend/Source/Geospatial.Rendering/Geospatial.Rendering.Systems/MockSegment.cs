using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class MockSegment : MockShape, ISegment, IShape
{
	public Segment Segment { get; set; }

	public MockSegment()
	{
	}

	public MockSegment(double wd, double ht)
		: base(wd, ht)
	{
	}
}
