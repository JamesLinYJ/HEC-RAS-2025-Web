namespace Geospatial.Systems.Shapes;

public interface ILineProvider
{
	ILine Acquire();

	void Return(IShape shape);
}
