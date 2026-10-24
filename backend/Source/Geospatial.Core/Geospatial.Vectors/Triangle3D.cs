namespace Geospatial.Vectors;

public readonly struct Triangle3D(Point3D p0, Point3D p1, Point3D p2)
{
	public readonly Point3D PointA = p0;

	public readonly Point3D PointB = p1;

	public readonly Point3D PointC = p2;

	public bool ComputeZ(Point pt, out double z)
	{
		return ComputeZ(pt.X, pt.Y, out z);
	}

	public bool ComputeZ(double ptx, double pty, out double Z)
	{
		if (Contains(ptx, pty, out var lambda, out var lambda2, out var lambda3))
		{
			Z = PointA.Z * lambda + PointB.Z * lambda2 + PointC.Z * lambda3;
			return true;
		}
		Z = double.NaN;
		return false;
	}

	public bool Contains(Point pt)
	{
		double lambda;
		double lambda2;
		double lambda3;
		return Contains(pt.X, pt.Y, out lambda, out lambda2, out lambda3);
	}

	public bool Contains(Point pt, out double lambda0, out double lambda1, out double lambda2)
	{
		return Contains(pt.X, pt.Y, out lambda0, out lambda1, out lambda2);
	}

	public bool Contains(double ptx, double pty, out double lambda0, out double lambda1, out double lambda2, double tolerance = 1E-06)
	{
		Triangle.InterpolationWeights(PointA.X, PointA.Y, PointB.X, PointB.Y, PointC.X, PointC.Y, ptx, pty, out lambda0, out lambda1, out lambda2);
		if (lambda0 >= 0.0 - tolerance && lambda1 >= 0.0 - tolerance && lambda2 >= 0.0 - tolerance && lambda0 <= 1.0 + tolerance && lambda1 <= 1.0 + tolerance)
		{
			return lambda2 <= 1.0 + tolerance;
		}
		return false;
	}
}
