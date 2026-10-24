using System.Collections.Generic;

namespace Geospatial.Vectors;

public interface ISpatialInterpolator
{
	Extent ValidExtent { get; }

	void Initialize(IList<Point> points);

	void Query(Point queryPoint, out int[] indexes, out double[] weights);
}
