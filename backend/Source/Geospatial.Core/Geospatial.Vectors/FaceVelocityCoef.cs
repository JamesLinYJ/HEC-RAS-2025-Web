using System;
using System.Diagnostics;

namespace Geospatial.Vectors;

[DebuggerDisplay("({A11}, {A12}, {A22})")]
public struct FaceVelocityCoef
{
	public double A11;

	public double A22;

	public double A12;

	private int _ct;

	public void AddFaceNormal(Vector norm)
	{
		A11 += norm.X * norm.X;
		A22 += norm.Y * norm.Y;
		A12 += norm.X * norm.Y;
		_ct++;
	}

	public void AddWeightedFaceNormal(Vector norm, float wK)
	{
		A11 += norm.X * norm.X * (double)wK;
		A22 += norm.Y * norm.Y * (double)wK;
		A12 += norm.X * norm.Y * (double)wK;
		_ct++;
	}

	public void Complete()
	{
		double a11A22Term = A11 * A22 - A12 * A12;
		if (a11A22Term == 0.0)
		{
			if (_ct == 0)
			{
				throw new Exception("No inputs for the coefficient matrix");
			}
			A11 = 1.0 / (double)_ct;
			A12 = 0.0;
			A22 = 1.0 / (double)_ct;
		}
		else
		{
			double computed = 1.0 / a11A22Term;
			A11 *= computed;
			A22 *= computed;
			A12 *= computed;
		}
	}

	public Vector Compute(float B1, float B2)
	{
		double dx = A22 * (double)B1 - A12 * (double)B2;
		double dy = (0.0 - A12) * (double)B1 + A11 * (double)B2;
		return new Vector(dx, dy);
	}

	public Vector Compute(double B1, double B2)
	{
		double dx = A22 * B1 - A12 * B2;
		double dy = (0.0 - A12) * B1 + A11 * B2;
		return new Vector(dx, dy);
	}
}
