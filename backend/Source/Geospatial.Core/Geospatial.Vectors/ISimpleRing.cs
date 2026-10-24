using System;

namespace Geospatial.Vectors;

public interface ISimpleRing
{
	Memory<Point> Points { get; }

	Extent Extent { get; }

	int Count { get; }

	int SegmentCount { get; }

	Segment GetSegment(int segIdx);

	int NextPointIndex(int ptIdx);

	int PreviousPointIndex(int ptIdx);
}
