using System;
using Utility;
using Utility.Units;

namespace Geospatial.Transformers;

public class UnitValueTransformer : IValueTransformer<float>, IValueTransformer<double>
{
	private readonly double _scaleFactor;

	public UnitValueTransformer(Length from, Length to)
	{
		_scaleFactor = from.ConvertTo(to, 1.0);
	}

	public void Transform(Memory<float> data, float? noDataValue)
	{
		if (noDataValue.HasValue)
		{
			Buffers.MultiplyConstant(data.Span, (float)_scaleFactor, noDataValue.Value);
		}
		else
		{
			Buffers.MultiplyConstant(data.Span, (float)_scaleFactor);
		}
	}

	public void Transform(Memory<double> data, double? noDataValue)
	{
		if (noDataValue.HasValue)
		{
			Buffers.MultiplyConstant(data.Span, _scaleFactor, noDataValue.Value);
		}
		else
		{
			Buffers.MultiplyConstant(data.Span, _scaleFactor);
		}
	}
}
