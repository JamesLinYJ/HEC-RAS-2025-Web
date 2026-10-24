using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RasMapperLib.Mesh;

[ComVisible(false)]
[DebuggerDisplay("({A11}, {A12}, {A22})")]
public struct FaceVelocityCoef
{
	public float A11;

	public float A22;

	public float A12;

	private int _ct;

	public void AddFaceNormal(Vector2D norm)
	{
		A11 += norm.X * norm.X;
		A22 += norm.Y * norm.Y;
		A12 += norm.X * norm.Y;
		checked
		{
			_ct++;
		}
	}

	public void AddWeightedFaceNormal(Vector2D norm, float wK)
	{
		A11 += norm.X * norm.X * wK;
		A22 += norm.Y * norm.Y * wK;
		A12 += norm.X * norm.Y * wK;
		checked
		{
			_ct++;
		}
	}

	public void Complete()
	{
		float a11A22Term = A11 * A22 - A12 * A12;
		if (a11A22Term == 0f)
		{
			if (_ct == 0)
			{
				throw new Exception("No inputs for the coefficient matrix");
			}
			A11 = 1f / (float)_ct;
			A12 = 0f;
			A22 = 1f / (float)_ct;
		}
		else
		{
			float computed = 1f / a11A22Term;
			A11 *= computed;
			A22 *= computed;
			A12 *= computed;
		}
	}

	public Vector2D Compute(float B1, float B2)
	{
		float x = A22 * B1 - A12 * B2;
		float y = (0f - A12) * B1 + A11 * B2;
		return new Vector2D(x, y);
	}
}
