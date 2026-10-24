using System;
using Utility;

namespace Geospatial.Transformers;

public class LocalMeanNormalizer : IValueTransformer<float>
{
	private readonly float _span;

	public LocalMeanNormalizer(float span = 70f)
	{
		_span = span;
	}

	public void Transform(Memory<float> data, float? noDataValue)
	{
		Span<float> floatSpan = data.Span;
		float noDataValue2 = noDataValue ?? (-9999f);
		Stats stats = Stats.Compute(floatSpan, noDataValue2);
		float mean = (double.IsNaN(stats.Mean) ? 0f : ((float)stats.Mean));
		for (int i = 0; i < floatSpan.Length; i++)
		{
			float computed = floatSpan[i];
			if (IsValid(computed, noDataValue2))
			{
				float meanTerm = (computed - mean) / _span + 0.5f;
				floatSpan[i] = Math.Clamp(meanTerm, 0f, 1f);
			}
			else
			{
				floatSpan[i] = 0.5f;
			}
		}
	}

	private static bool IsValid(float v, float nodata)
	{
		if (float.IsNaN(v) || float.IsInfinity(v))
		{
			return false;
		}
		if (v == nodata)
		{
			return false;
		}
		if (v < -9000f || v > 9000f)
		{
			return false;
		}
		return true;
	}
}
