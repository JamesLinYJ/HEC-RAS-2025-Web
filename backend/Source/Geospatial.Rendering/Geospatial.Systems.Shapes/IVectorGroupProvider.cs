namespace Geospatial.Systems.Shapes;

public interface IVectorGroupProvider
{
	IVectorCanvas Acquire();

	void Return(IShape shape);
}
