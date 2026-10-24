namespace Geospatial.Vectors;

public interface IPoint4D : IPoint3D, IPoint
{
	bool HasM { get; }

	double M { get; set; }

	IVector4D To(IPoint4D pt);
}
