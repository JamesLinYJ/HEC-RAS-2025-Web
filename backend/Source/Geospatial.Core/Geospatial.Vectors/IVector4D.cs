namespace Geospatial.Vectors;

public interface IVector4D : IVector3D, IVector
{
	bool HasM { get; }

	double DeltaM { get; }
}
