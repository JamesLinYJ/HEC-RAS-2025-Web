namespace Geospatial.Systems.Shapes;

public interface IPolygonProvider
{
	IPolygon Acquire();

	void Return(IShape shape);
}
