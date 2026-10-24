using System;

namespace Ras.Optimization;

public sealed class LatinHypercubeSampler
{
	private readonly Random _rng;

	public LatinHypercubeSampler(int seed)
	{
		_rng = new Random(seed);
	}

	public double[][] SampleUnit(int count, int dimension)
	{
		if (count < 1)
		{
			throw new ArgumentException("Sample count must be positive.", "count");
		}
		if (dimension < 1)
		{
			throw new ArgumentException("Dimension must be positive.", "dimension");
		}
		double[][] doubleValues = new double[count][];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = new double[dimension];
		}
		for (int j = 0; j < dimension; j++)
		{
			int[] shuffledStrata = ShuffledStrata(count);
			for (int k = 0; k < count; k++)
			{
				double nextDouble = _rng.NextDouble();
				doubleValues[k][j] = ((double)shuffledStrata[k] + nextDouble) / (double)count;
			}
		}
		return doubleValues;
	}

	public double[][] Sample(int count, double[] min, double[] max, ParameterScale[] scales = null)
	{
		if (min == null || max == null)
		{
			throw new ArgumentNullException((min == null) ? "min" : "max");
		}
		if (min.Length != max.Length)
		{
			throw new ArgumentException("Min and Max must have the same length.");
		}
		for (int i = 0; i < min.Length; i++)
		{
			if (max[i] < min[i])
			{
				throw new ArgumentException($"Max[{i}] ({max[i]}) is less than Min[{i}] ({min[i]}).");
			}
		}
		int minLength = min.Length;
		ParameterScale[] resolve = ParameterScaling.Resolve(scales, minLength);
		ParameterScaling.ValidateBounds(min, resolve);
		double[][] sampleUnit = SampleUnit(count, minLength);
		for (int j = 0; j < count; j++)
		{
			for (int k = 0; k < minLength; k++)
			{
				double warp = ParameterScaling.Warp(min[k], resolve[k]);
				double warp2 = ParameterScaling.Warp(max[k], resolve[k]);
				sampleUnit[j][k] = ParameterScaling.Unwarp(warp + sampleUnit[j][k] * (warp2 - warp), resolve[k]);
			}
		}
		return sampleUnit;
	}

	private int[] ShuffledStrata(int count)
	{
		int[] intValues = new int[count];
		for (int i = 0; i < count; i++)
		{
			intValues[i] = i;
		}
		for (int countOffset = count - 1; countOffset > 0; countOffset--)
		{
			int next = _rng.Next(countOffset + 1);
			ref int reference = ref intValues[countOffset];
			ref int reference2 = ref intValues[next];
			int reference3 = intValues[next];
			int reference22 = intValues[countOffset];
			reference = reference3;
			reference2 = reference22;
		}
		return intValues;
	}
}
