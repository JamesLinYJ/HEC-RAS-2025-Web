namespace Geospatial.Systems.Shapes;

public interface IEllipseProvider
{
	IEllipse Acquire();

	void Return(IShape shape);
}
