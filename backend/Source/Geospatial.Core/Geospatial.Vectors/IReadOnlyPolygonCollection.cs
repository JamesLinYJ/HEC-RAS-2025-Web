using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public interface IReadOnlyPolygonCollection : IReadOnlyVectorCollection<Polygon>, IEnumerable<Polygon>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Polygon>, IReadOnlyCollection<Polygon>, ISpatiallyIndexedSegmentCollection, ILineSegmentSource
{
	Polyline ILineSegmentSource.GetLine(int fid)
	{
		return this[fid];
	}
}
