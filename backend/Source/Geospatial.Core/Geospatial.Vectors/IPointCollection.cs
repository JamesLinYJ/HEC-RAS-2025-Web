using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public interface IPointCollection : IVectorCollection<Point>, IReadOnlyVectorCollection<Point>, IEnumerable<Point>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Point>, IReadOnlyCollection<Point>, IReadOnlyPointCollection, ISpatiallyIndexedPointCollection
{
}
