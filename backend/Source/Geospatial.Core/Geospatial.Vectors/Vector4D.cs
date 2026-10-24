namespace Geospatial.Vectors;

public struct Vector4D : IVector, IVector3D, IVector4D
{
	public double DeltaX;

	public double DeltaY;

	public double DeltaZ;

	public double DeltaM;

	double IVector.DeltaX => DeltaX;

	double IVector.DeltaY => DeltaY;

	bool IVector3D.HasZ => true;

	double IVector3D.DeltaZ => DeltaZ;

	bool IVector4D.HasM => true;

	double IVector4D.DeltaM => DeltaM;

	public Vector4D(double dx, double dy, double dz, double dm)
	{
		DeltaX = dx;
		DeltaY = dy;
		DeltaZ = dz;
		DeltaM = dm;
	}

	public Vector4D(Point4D pA, Point4D pB)
	{
		DeltaX = pB.X - pA.X;
		DeltaY = pB.Y - pA.Y;
		DeltaZ = pB.Z - pA.Z;
		DeltaM = pB.M - pA.M;
	}
}
