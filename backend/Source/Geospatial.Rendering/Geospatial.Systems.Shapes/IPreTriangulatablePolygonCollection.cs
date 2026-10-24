using System;
using Geospatial.Vectors;

namespace Geospatial.Systems.Shapes;

public interface IPreTriangulatablePolygonCollection
{
	Memory<Point> TriangulatedVertices { get; }

	void SetTriangulatedData(Memory<Point> vertices, Memory<uint> indices);
}
