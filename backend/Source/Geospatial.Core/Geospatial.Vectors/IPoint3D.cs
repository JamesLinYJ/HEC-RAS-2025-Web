namespace Geospatial.Vectors;

public interface IPoint3D : IPoint
{
	bool HasZ { get; }

	double Z { get; set; }

	IVector3D To(IPoint3D pt);
}
