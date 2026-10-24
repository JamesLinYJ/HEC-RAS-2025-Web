using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Systems.Shapes;

public interface IContiguousComplexPolygonCollection : IDisposable
{
	Memory<Point> Points { get; }

	Memory<int> PolygonStartIndices { get; }

	int PointCount { get; }

	void DisposeInternalResources();

	void SetComplexPolygons(IReadOnlyList<ImmutableMultiPartPolygon> pgons);

	void SetComplexPolygons(IReadOnlyList<ComplexPolygon> pgons);
}
