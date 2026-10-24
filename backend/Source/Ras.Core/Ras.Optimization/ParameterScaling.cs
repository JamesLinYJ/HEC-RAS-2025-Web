using System;

namespace Ras.Optimization;

internal static class ParameterScaling
{
	public static double Warp(double doubleValue, ParameterScale scale)
	{
		if (scale != ParameterScale.Log)
		{
			return doubleValue;
		}
		return Math.Log10(doubleValue);
	}

	public static double Unwarp(double doubleValue, ParameterScale scale)
	{
		if (scale != ParameterScale.Log)
		{
			return doubleValue;
		}
		return Math.Pow(10.0, doubleValue);
	}

	public static ParameterScale[] Resolve(ParameterScale[] scales, int dimension)
	{
		if (scales == null)
		{
			return new ParameterScale[dimension];
		}
		if (scales.Length != dimension)
		{
			throw new ArgumentException($"Scales length {scales.Length} does not match dimension {dimension}.", "scales");
		}
		return (ParameterScale[])scales.Clone();
	}

	public static void ValidateBounds(double[] min, ParameterScale[] scales)
	{
		for (int i = 0; i < scales.Length; i++)
		{
			if (scales[i] == ParameterScale.Log && min[i] <= 0.0)
			{
				throw new ArgumentException($"Dimension {i} is log-scaled but Min ({min[i]}) is not positive.");
			}
		}
	}
}
