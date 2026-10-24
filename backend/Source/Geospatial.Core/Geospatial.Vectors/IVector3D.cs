namespace Geospatial.Vectors;

public interface IVector3D : IVector
{
	bool HasZ { get; }

	double DeltaZ { get; }
}
