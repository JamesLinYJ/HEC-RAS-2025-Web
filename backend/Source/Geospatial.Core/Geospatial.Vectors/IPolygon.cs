using System.Collections.Generic;

namespace Geospatial.Vectors;

public interface IPolygon
{
	IReadOnlyList<Point> Points { get; }

	IReadOnlyList<Segment> Segments { get; }

	int NextPointIndex(int ptIdx);

	int PreviousPointIndex(int ptIdx);
}
