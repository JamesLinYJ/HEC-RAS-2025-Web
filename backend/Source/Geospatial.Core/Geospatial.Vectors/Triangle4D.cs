using System;
using Geospatial.Rasters;
using Utility;

namespace Geospatial.Vectors;

public struct Triangle4D
{
	public Point4D PointA;

	public Point4D PointB;

	public Point4D PointC;

	public Extent Extent
	{
		get
		{
			MathHelpers.MaxMin3(PointA.X, PointB.X, PointC.X, out var max, out var min);
			MathHelpers.MaxMin3(PointA.Y, PointB.Y, PointC.Y, out var max2, out var min2);
			return new Extent(min, max, min2, max2);
		}
	}

	public Triangle4D(Point4D p0, Point4D p1, Point4D p2)
	{
		PointA = p0;
		PointB = p1;
		PointC = p2;
	}

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

	public bool ComputeM(Point pt, out double m)
	{
		return ComputeM(pt.X, pt.Y, out m);
	}

	public bool ComputeM(double ptx, double pty, out double M)
	{
		if (Contains(ptx, pty, out var lambda, out var lambda2, out var lambda3))
		{
			M = PointA.M * lambda + PointB.M * lambda2 + PointC.M * lambda3;
			return true;
		}
		M = double.NaN;
		return false;
	}

	public bool ComputeZM(Point pt, out double Z, out double M)
	{
		return ComputeZM(pt.X, pt.Y, out Z, out M);
	}

	public bool ComputeZM(double ptx, double pty, out double Z, out double M)
	{
		if (Contains(ptx, pty, out var lambda, out var lambda2, out var lambda3))
		{
			Z = PointA.Z * lambda + PointB.Z * lambda2 + PointC.Z * lambda3;
			M = PointA.M * lambda + PointB.M * lambda2 + PointC.M * lambda3;
			return true;
		}
		Z = double.NaN;
		M = double.NaN;
		return false;
	}

	public bool ComputeZMSafe(double ptx, double pty, out double Z, out double M)
	{
		if (Contains(ptx, pty, out var lambda, out var lambda2, out var lambda3))
		{
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			if (lambda > 0.0 && !double.IsNaN(PointA.M))
			{
				computed += lambda * PointA.Z;
				computed2 += lambda * PointA.M;
				computed3 += lambda;
			}
			if (lambda2 > 0.0 && !double.IsNaN(PointB.M))
			{
				computed += lambda2 * PointB.Z;
				computed2 += lambda2 * PointB.M;
				computed3 += lambda2;
			}
			if (lambda3 > 0.0 && !double.IsNaN(PointC.M))
			{
				computed += lambda3 * PointC.Z;
				computed2 += lambda3 * PointC.M;
				computed3 += lambda3;
			}
			Z = computed / computed3;
			M = computed2 / computed3;
			return true;
		}
		Z = double.NaN;
		M = double.NaN;
		return false;
	}

	public bool ComputeXY(Point zmPoint, out Point xyPoint, double tolerance = 1E-06)
	{
		Triangle.InterpolationWeights(PointA.Z, PointA.M, PointB.Z, PointB.M, PointC.Z, PointC.M, zmPoint.X, zmPoint.Y, out var lambda, out var lambda2, out var lambda3);
		if (!(lambda >= 0.0 - tolerance) || !(lambda2 >= 0.0 - tolerance) || !(lambda3 >= 0.0 - tolerance) || !(lambda <= 1.0 + tolerance) || !(lambda2 <= 1.0 + tolerance) || !(lambda3 <= 1.0 + tolerance))
		{
			if (zmPoint.Y == PointA.M && PointA.M == PointB.M && PointA.M == PointC.M)
			{
				CollinearWeights(zmPoint.X, PointA.Z, PointB.Z, PointC.Z, out lambda, out lambda2, out lambda3);
			}
			else
			{
				if (zmPoint.X != PointA.Z || PointA.Z != PointB.Z || PointA.Z != PointC.Z)
				{
					xyPoint = Point.NullPoint;
					return false;
				}
				CollinearWeights(zmPoint.Y, PointA.M, PointB.M, PointC.M, out lambda, out lambda2, out lambda3);
			}
		}
		xyPoint = lambda * PointA.AsPoint2D() + lambda2 * PointB.AsPoint2D() + lambda3 * PointC.AsPoint2D();
		return true;
	}

	public static void CollinearWeights(double coord, double cA, double cB, double cC, out double lambda0, out double lambda1, out double lambda2)
	{
		lambda0 = (lambda1 = (lambda2 = double.NaN));
		if ((cA - cB) * (cA - cC) < 0.0)
		{
			if ((coord - cA) * (coord - cB) <= 0.0)
			{
				lambda1 = (coord - cA) / (cB - cA);
				lambda0 = 1.0 - lambda1;
				lambda2 = 0.0;
			}
			else if ((coord - cA) * (coord - cC) <= 0.0)
			{
				lambda2 = (coord - cA) / (cC - cA);
				lambda0 = 1.0 - lambda2;
				lambda1 = 0.0;
			}
		}
		else if ((cB - cA) * (cB - cC) < 0.0)
		{
			if ((coord - cA) * (coord - cB) <= 0.0)
			{
				lambda1 = (coord - cA) / (cB - cA);
				lambda0 = 1.0 - lambda1;
				lambda2 = 0.0;
			}
			else if ((coord - cB) * (coord - cC) <= 0.0)
			{
				lambda2 = (coord - cB) / (cC - cB);
				lambda1 = 1.0 - lambda2;
				lambda0 = 0.0;
			}
		}
		else if ((cC - cA) * (cC - cB) < 0.0)
		{
			if ((coord - cA) * (coord - cC) <= 0.0)
			{
				lambda2 = (coord - cA) / (cC - cA);
				lambda0 = 1.0 - lambda2;
				lambda1 = 0.0;
			}
			else if ((coord - cB) * (coord - cC) <= 0.0)
			{
				lambda2 = (coord - cB) / (cC - cB);
				lambda1 = 1.0 - lambda2;
				lambda0 = 0.0;
			}
		}
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

	public void Rasterize(RasterDefinition raster, Action<int, double, double, double, double> fillCellAction)
	{
		Triangle.Rasterize(raster, PointA, PointB, PointC, fillCellAction);
	}
}
