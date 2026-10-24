namespace Geospatial.Systems.Shapes;

public interface ITextProvider
{
	IText Acquire();

	void Return(IShape shape);
}
