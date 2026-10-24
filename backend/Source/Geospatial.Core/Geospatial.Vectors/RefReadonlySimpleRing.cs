using System;

namespace Geospatial.Vectors;

public ref struct RefReadonlySimpleRing
{
	public Span<Point> Points { get; }

	public Extent Extent => PointCollection.ComputeExtent(Points);

	public ref Segment GetSegment(int segIdx)
	{
		throw new NotImplementedException();
	}
}
