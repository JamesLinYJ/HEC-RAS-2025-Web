using System;

namespace Geospatial.Rasters;

public static class ConvertTypeResamplers
{
	public static Action<Memory<float>, Memory<double>> FloatToDoubleConverter = (Memory<float> fromBuf, Memory<double> toBuf) =>
	{
		Span<float> fromBufSpan = fromBuf.Span;
		Span<double> toBufSpan = toBuf.Span;
		for (int i = 0; i < toBufSpan.Length; i++)
		{
			toBufSpan[i] = fromBufSpan[i];
		}
	};

	public static Action<Memory<double>, Memory<float>> DoubleToFloatConverter = (Memory<double> fromBuf, Memory<float> toBuf) =>
	{
		Span<double> fromBufSpan = fromBuf.Span;
		Span<float> toBufSpan = toBuf.Span;
		for (int i = 0; i < toBufSpan.Length; i++)
		{
			toBufSpan[i] = (float)fromBufSpan[i];
		}
	};
}
