using System;
using System.Numerics;
using Utility;

namespace Geospatial.Transformers;

public class ScaleValueTransformer<T> : IValueTransformer<T> where T : unmanaged, IBinaryFloatingPointIeee754<T>
{
	private readonly T _scale;

	public ScaleValueTransformer(T scaleFactor)
	{
		_scale = scaleFactor;
	}

	public void Transform(Memory<T> data, T? noDataValue)
	{
		if (noDataValue.HasValue)
		{
			T noDataValueTerm = noDataValue.Value;
			Span<T> buffer = data.Span;
			for (int i = 0; i < buffer.Length; i++)
			{
				if (!(buffer[i] == noDataValueTerm))
				{
					buffer[i] *= _scale;
				}
			}
		}
		else
		{
			Buffers.MultiplyConstant(data.Span, _scale);
		}
	}
}
