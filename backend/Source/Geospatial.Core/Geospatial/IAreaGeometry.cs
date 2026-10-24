using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial;

public interface IAreaGeometry
{
	bool Contains(Polyline line);

	bool Intersects(Polyline line);

	bool Overlaps(Polyline line);

	bool Overlaps(Polygon pgon);

	List<StartEndStation> GetContainedSubsegments(Polyline line);

	bool Contains(Point pt, double tolerance = 0.0);
}
