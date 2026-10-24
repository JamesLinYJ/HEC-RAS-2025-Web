using System;
using System.Runtime.CompilerServices;

namespace Geospatial.Vectors;

public readonly struct BarycentricWeights(double w0, double w1, double w2)
{
	public readonly double Weight0 = w0;

	public readonly double Weight1 = w1;

	public readonly double Weight2 = w2;

	public double this[int idx] => idx switch
	{
		0 => Weight0, 
		1 => Weight1, 
		2 => Weight2, 
		_ => throw new IndexOutOfRangeException(), 
	};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public double Interpolate(double v0, double v1, double v2)
	{
		return Weight0 * v0 + Weight1 * v1 + Weight2 * v2;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Interpolate(float v0, float v1, float v2)
	{
		return (float)(Weight0 * (double)v0 + Weight1 * (double)v1 + Weight2 * (double)v2);
	}
}
