using System.Runtime.InteropServices;

namespace RasMapperLib;

public struct TriangleShape
{
	public PointM pt0;

	public PointM pt1;

	public PointM pt2;

	public double tolerance;

	public TriangleShape(PointM apt0, PointM apt1, PointM apt2, double tol = 1E-06)
	{
		this = default;
		pt0 = apt0;
		pt1 = apt1;
		pt2 = apt2;
		tolerance = tol;
	}

	public void Assign(PointM apt0, PointM apt1, PointM apt2)
	{
		pt0 = apt0;
		pt1 = apt1;
		pt2 = apt2;
	}

	public bool Contains(PointM pt, [Optional][DefaultParameterValue(0.0)] ref double lambda0, [Optional][DefaultParameterValue(0.0)] ref double lambda1, [Optional][DefaultParameterValue(0.0)] ref double lambda2)
	{
		return Contains(pt.X, pt.Y, ref lambda0, ref lambda1, ref lambda2);
	}

	public bool Contains(double ptx, double pty, [Optional][DefaultParameterValue(0.0)] ref double lambda0, [Optional][DefaultParameterValue(0.0)] ref double lambda1, [Optional][DefaultParameterValue(0.0)] ref double lambda2)
	{
		InterpolationWeights(ptx, pty, ref lambda0, ref lambda1, ref lambda2);
		if (lambda0 >= 0.0 - tolerance && lambda1 >= 0.0 - tolerance && lambda2 >= 0.0 - tolerance && lambda0 <= 1.0 + tolerance && lambda1 <= 1.0 + tolerance)
		{
			return lambda2 <= 1.0 + tolerance;
		}
		return false;
	}

	public static bool Contains(PointM pt0, PointM pt1, PointM pt2, double ptX, double ptY, [Optional][DefaultParameterValue(0.0)] ref double lambda0, [Optional][DefaultParameterValue(0.0)] ref double lambda1, [Optional][DefaultParameterValue(0.0)] ref double lambda2)
	{
		return new TriangleShape(pt0, pt1, pt2).Contains(ptX, ptY, ref lambda0, ref lambda1, ref lambda2);
	}

	public void InterpolationWeights(double px, double py, ref double lambda0, ref double lambda1, ref double lambda2)
	{
		double pt1Pt2Term = pt1.Y - pt2.Y;
		double pt0Pt2Term = pt0.X - pt2.X;
		double pt2Pt1Term = pt2.X - pt1.X;
		double pt0Pt2Term2 = pt0.Y - pt2.Y;
		double computed = pt1Pt2Term * pt0Pt2Term + pt2Pt1Term * pt0Pt2Term2;
		double pxPt2Term = px - pt2.X;
		double pyPt2Term = py - pt2.Y;
		lambda0 = (pt1Pt2Term * pxPt2Term + pt2Pt1Term * pyPt2Term) / computed;
		lambda1 = (pt0Pt2Term * pyPt2Term - pt0Pt2Term2 * pxPt2Term) / computed;
		lambda2 = 1.0 - lambda0 - lambda1;
	}

	public bool ComputeZ(PointM pt, ref double z)
	{
		return ComputeZ(pt.X, pt.Y, ref z);
	}

	public bool ComputeZ(double ptx, double pty, ref double Z)
	{
		double lambda = default;
		double lambda2 = default;
		double lambda3 = default;
		if (Contains(ptx, pty, ref lambda, ref lambda2, ref lambda3))
		{
			Z = pt0.Z * lambda + pt1.Z * lambda2 + pt2.Z * lambda3;
			return true;
		}
		return false;
	}

	public bool ComputeM(PointM pt, ref double m)
	{
		return ComputeM(pt.X, pt.Y, ref m);
	}

	public bool ComputeM(double ptx, double pty, ref double M)
	{
		double lambda = default;
		double lambda2 = default;
		double lambda3 = default;
		if (Contains(ptx, pty, ref lambda, ref lambda2, ref lambda3))
		{
			M = pt0.M * lambda + pt1.M * lambda2 + pt2.M * lambda3;
			return true;
		}
		return false;
	}

	public bool ComputeZM(PointM pt, ref double Z, ref double M)
	{
		return ComputeZM(pt.X, pt.Y, ref Z, ref M);
	}

	public bool ComputeZM(double ptx, double pty, ref double Z, ref double M)
	{
		double lambda = default;
		double lambda2 = default;
		double lambda3 = default;
		if (Contains(ptx, pty, ref lambda, ref lambda2, ref lambda3))
		{
			Z = pt0.Z * lambda + pt1.Z * lambda2 + pt2.Z * lambda3;
			M = pt0.M * lambda + pt1.M * lambda2 + pt2.M * lambda3;
			return true;
		}
		return false;
	}
}
