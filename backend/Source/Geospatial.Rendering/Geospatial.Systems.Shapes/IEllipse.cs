namespace Geospatial.Systems.Shapes;

public interface IEllipse : IShape
{
	double Width { get; set; }

	double Height { get; set; }

	float X { get; set; }

	float Y { get; set; }
}
