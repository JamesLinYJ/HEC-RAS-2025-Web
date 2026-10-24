using System;
using Utility;

namespace Geospatial.Transformers;

public class TruncateValueTransformer : IValueTransformer<float>
{
	private readonly int _nBits;

	public TruncateValueTransformer(int nBits)
	{
		_nBits = nBits;
	}

	public void Transform(Memory<float> data, float? noDataValue)
	{
		if (noDataValue.HasValue)
		{
			Buffers.TruncateBinary(data.Span, _nBits, noDataValue.Value);
		}
		else
		{
			Buffers.TruncateBinary(data.Span, _nBits);
		}
	}
}
