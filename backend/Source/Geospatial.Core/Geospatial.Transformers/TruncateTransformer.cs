using System;
using Utility;

namespace Geospatial.Transformers;

public class TruncateTransformer : IValueTransformer<float>
{
	private readonly int _nbits;

	public TruncateTransformer(int nBitsPrecision)
	{
		_nbits = nBitsPrecision;
	}

	public void Transform(Memory<float> data, float? noDataValue)
	{
		Buffers.TruncateBinary(data.Span, _nbits);
	}
}
