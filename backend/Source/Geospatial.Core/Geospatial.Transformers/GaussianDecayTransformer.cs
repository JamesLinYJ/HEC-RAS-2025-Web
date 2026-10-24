using System;

namespace Geospatial.Transformers;

public class GaussianDecayTransformer : IValueTransformer<float>
{
	private readonly double _sigmaSquaredTimesTwo;

	private readonly float _maxDistance;

	private readonly bool _hasMaxDistance;

	public GaussianDecayTransformer(double sigma, float maxDistance = float.MaxValue)
	{
		_sigmaSquaredTimesTwo = 2.0 * sigma * sigma;
		_maxDistance = maxDistance;
		_hasMaxDistance = maxDistance < float.MaxValue;
	}

	public void Transform(Memory<float> data, float? noDataValue)
	{
		Span<float> floatSpan = data.Span;
		bool hasValue = noDataValue.HasValue;
		float valueOrDefault = noDataValue.GetValueOrDefault();
		for (int i = 0; i < floatSpan.Length; i++)
		{
			float computed = floatSpan[i];
			if (!hasValue || computed != valueOrDefault)
			{
				if (_hasMaxDistance && computed > _maxDistance)
				{
					floatSpan[i] = 0f;
					continue;
				}
				double sigmaSquaredTimesTwoTerm = Math.Exp((double)(0f - computed * computed) / _sigmaSquaredTimesTwo);
				floatSpan[i] = (float)sigmaSquaredTimesTwoTerm;
			}
		}
	}
}
