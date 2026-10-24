using System;

namespace Geospatial.Vectors;

public struct Vector3D : IVector, IVector3D, IVector4D
{
	public double DeltaX;

	public double DeltaY;

	public double DeltaZ;

	double IVector.DeltaX => DeltaX;

	double IVector.DeltaY => DeltaY;

	bool IVector3D.HasZ => true;

	double IVector3D.DeltaZ => DeltaZ;

	bool IVector4D.HasM => false;

	double IVector4D.DeltaM
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public Vector3D(double dx, double dy, double dz)
	{
		DeltaX = dx;
		DeltaY = dy;
		DeltaZ = dz;
	}

	public Vector3D(Point3D pA, Point3D pB)
	{
		DeltaX = pB.X - pA.X;
		DeltaY = pB.Y - pA.Y;
		DeltaZ = pB.Z - pA.Z;
	}

	public Vector3D CrossProduct(Vector3D other)
	{
		return CrossProduct(this, other);
	}

	public static Vector3D CrossProduct(Vector3D A, Vector3D B)
	{
		return new Vector3D(A.DeltaY * B.DeltaZ - A.DeltaZ * B.DeltaY, A.DeltaZ * B.DeltaX - A.DeltaX * B.DeltaZ, A.DeltaX * B.DeltaY - A.DeltaY * B.DeltaX);
	}
}
