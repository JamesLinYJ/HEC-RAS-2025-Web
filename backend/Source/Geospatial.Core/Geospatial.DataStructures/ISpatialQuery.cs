using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.DataStructures;

public interface ISpatialQuery<T>
{
	List<T> GetElements(Point pt, double distance, out bool isApproximate);

	List<T> GetElements(Extent extent, out bool isApproximate);
}
