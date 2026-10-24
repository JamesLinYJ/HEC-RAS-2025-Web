using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class MockComplexPolygon : MockShape, IComplexPolygon, IShape
{
	public ImmutableMultiPartPolygon Polygon { get; set; }
}
