using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public interface IComplexPolygonCollection : IVectorCollection<ComplexPolygon>, IReadOnlyVectorCollection<ComplexPolygon>, IEnumerable<ComplexPolygon>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<ComplexPolygon>, IReadOnlyCollection<ComplexPolygon>, IReadOnlyComplexPolygonCollection
{
}
