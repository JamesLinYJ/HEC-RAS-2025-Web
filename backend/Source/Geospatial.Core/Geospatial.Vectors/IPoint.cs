namespace Geospatial.Vectors;

public interface IPoint
{
	double X { get; set; }

	double Y { get; set; }

	IVector To(IPoint pt);

	Point ToPoint()
	{
		return new Point(X, Y);
	}
}
