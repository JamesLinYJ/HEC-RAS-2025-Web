using System;
using System.Linq;

namespace Geospatial.Rasters;

public struct InterpolationData
{
	public int[] Index;

	public double[] Weights;

	public int Count => Index?.Length ?? 0;

	public InterpolationData(int[] idx, double[] wts)
	{
		Index = idx;
		Weights = wts;
	}

	public InterpolationData(int idx, double wt)
	{
		Index = new int[1];
		Weights = new double[1];
		Index[0] = idx;
		Weights[0] = wt;
	}

	public void AddPoint(int idx, double wt)
	{
		if (Index == null)
		{
			Index = new int[1];
			Weights = new double[1];
		}
		else
		{
			int[] index = Index;
			Index = new int[Index.Count() + 1];
			if (index != null)
			{
				Array.Copy(index, Index, Math.Min(Index.Count() + 1, index.Length));
			}
			double[] weights = Weights;
			Weights = new double[Weights.Count() + 1];
			if (weights != null)
			{
				Array.Copy(weights, Weights, Math.Min(Weights.Count() + 1, weights.Length));
			}
		}
		Index[Index.Count() - 1] = idx;
		Weights[Weights.Count() - 1] = wt;
	}

	public void Normalize()
	{
		double weightSum = 0.0;
		for (int i = 0; i < Weights.Length; i++)
		{
			weightSum += Weights[i];
		}
		for (int j = 0; j < Weights.Length; j++)
		{
			Weights[j] /= weightSum;
		}
	}
}
