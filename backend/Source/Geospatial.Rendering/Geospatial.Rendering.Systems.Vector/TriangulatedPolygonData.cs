using System;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems.Vector;

public readonly struct TriangulatedPolygonData(Memory<Point> vertices, Memory<uint> indices)
{
	public readonly Memory<Point> Vertices = vertices;

	public readonly Memory<uint> Indices = indices;
}
