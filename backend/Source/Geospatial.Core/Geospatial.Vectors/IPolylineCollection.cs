using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public interface IPolylineCollection : IVectorCollection<Polyline>, IReadOnlyVectorCollection<Polyline>, IEnumerable<Polyline>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Polyline>, IReadOnlyCollection<Polyline>, ISpatiallyIndexedSegmentCollection, ILineSegmentSource
{
	Polyline ILineSegmentSource.GetLine(int fid)
	{
		return this[fid];
	}
}
