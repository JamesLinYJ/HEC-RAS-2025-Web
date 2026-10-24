using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public interface IReadOnlyPointCollection : IReadOnlyVectorCollection<Point>, IEnumerable<Point>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Point>, IReadOnlyCollection<Point>, ISpatiallyIndexedPointCollection
{
}
