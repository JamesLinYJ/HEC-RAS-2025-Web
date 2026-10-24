using Geospatial.Vectors;

namespace Geospatial.Systems.Shapes;

public interface IComplexPolygon : IShape
{
	ImmutableMultiPartPolygon Polygon { get; set; }
}
