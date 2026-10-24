using System;

namespace Geospatial.Rasters.Convolution;

public static class Kernels
{
	private static readonly int[] D8Map = new int[9] { 32, 64, 128, 16, 0, 1, 8, 4, 2 };

	public static WindowKernel<float, float> PlanarDeviation(float NDV)
	{
		return (Span<float> sp) =>
		{
			for (int i = 0; i < 9; i++)
			{
				if (sp[i] == NDV)
				{
					return NDV;
				}
			}
			float spTerm = (sp[5] + sp[2] + sp[8] - sp[3] - sp[0] - sp[6]) / 6f;
			float spTerm2 = (sp[0] + sp[1] + sp[2] - sp[6] - sp[7] - sp[8]) / 6f;
			float sp2 = sp[4];
			float sp2Term = sp2 - spTerm + spTerm2;
			float sp2Term2 = sp2 + spTerm2;
			float sp2Term3 = sp2 + spTerm + spTerm2;
			float sp2Term4 = sp2 - spTerm;
			float sp2Term5 = sp2 + spTerm;
			float sp2Term6 = sp2 - spTerm - spTerm2;
			float sp2Term7 = sp2 - spTerm2;
			float sp2Term8 = sp2 + spTerm - spTerm2;
			return Math.Abs(sp[0] - sp2Term) + Math.Abs(sp[1] - sp2Term2) + Math.Abs(sp[2] - sp2Term3) + Math.Abs(sp[3] - sp2Term4) + Math.Abs(sp[5] - sp2Term5) + Math.Abs(sp[6] - sp2Term6) + Math.Abs(sp[7] - sp2Term7) + Math.Abs(sp[8] - sp2Term8);
		};
	}

	public static WindowKernel<float, float> SimpleAverage(float NDV)
	{
		return (Span<float> sp) =>
		{
			float spSum = 0f;
			int ordinal = 0;
			for (int i = 0; i < sp.Length; i++)
			{
				float sp2 = sp[i];
				if (sp2 != NDV)
				{
					spSum += sp2;
					ordinal++;
				}
			}
			return (ordinal == 0) ? NDV : (spSum / (float)ordinal);
		};
	}

	public static WindowKernel<float, float> FillNDV_SimpleAverage(float NDV)
	{
		return (Span<float> sp) =>
		{
			float sp2 = sp[4];
			if (sp2 != NDV)
			{
				return sp2;
			}
			float spSum = 0f;
			int ordinal = 0;
			for (int i = 0; i < sp.Length; i++)
			{
				float sp3 = sp[i];
				if (sp3 != NDV)
				{
					spSum += sp3;
					ordinal++;
				}
			}
			return (ordinal == 0) ? NDV : (spSum / (float)ordinal);
		};
	}

	public static WindowKernel<float, int> D8(float NDV)
	{
		return (Span<float> sp) =>
		{
			if (sp[4] == NDV)
			{
				return -1;
			}
			float maxValue = float.MaxValue;
			int ordinal = -1;
			for (int i = 0; i < sp.Length; i++)
			{
				float sp2 = sp[i];
				if (sp2 != NDV && sp2 < maxValue)
				{
					maxValue = sp2;
					ordinal = i;
				}
			}
			return D8Map[ordinal];
		};
	}

	public static WindowKernel<float, float> D8F(float NDV)
	{
		return (Span<float> sp) =>
		{
			if (sp[4] == NDV)
			{
				return -1f;
			}
			float maxValue = float.MaxValue;
			int ordinal = -1;
			for (int i = 0; i < sp.Length; i++)
			{
				float sp2 = sp[i];
				if (sp2 != NDV && sp2 < maxValue)
				{
					maxValue = sp2;
					ordinal = i;
				}
			}
			return D8Map[ordinal];
		};
	}

	public static WindowKernel<float, int> MaxIndex(float NDV)
	{
		return (Span<float> sp) =>
		{
			float minValue = float.MinValue;
			int result = -1;
			for (int i = 0; i < sp.Length; i++)
			{
				float sp2 = sp[i];
				if (sp2 != NDV && sp2 > minValue)
				{
					minValue = sp2;
					result = i;
				}
			}
			return result;
		};
	}
}
