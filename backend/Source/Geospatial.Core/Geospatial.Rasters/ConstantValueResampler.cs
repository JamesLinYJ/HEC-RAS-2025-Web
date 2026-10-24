using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class ConstantValueResampler<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	public T? NoDataValue { get; set; }

	public Extent ValidExtent { get; set; }

	public T Value { get; set; }

	public static ConstantValueResampler<T> None()
	{
		return new ConstantValueResampler<T>(default, Extent.NullExtent);
	}

	public ConstantValueResampler(T localValue, Extent ext, T? noDataValue = null)
	{
		Value = localValue;
		ValidExtent = ext;
		NoDataValue = noDataValue;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		PixelRange pixelRange = args.Raster.GetPixelRange(ValidExtent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		if (pixelRange.IsFullRange(args.Raster))
		{
			outData.Span.Fill(Value);
			return;
		}
		Span<T> outDataSpan = outData.Span;
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			int start = i * args.Raster.Cols + pixelRange.MinCol;
			outDataSpan.Slice(start, pixelRange.ColCount).Fill(Value);
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<T> result, SamplingParams sampleParams = default(SamplingParams))
	{
		if (sampleParams.IsNull())
		{
			sampleParams = SamplingParams.Default;
		}
		T valueOrDefault = NoDataValue.GetValueOrDefault();
		if (ValidExtent.IsInfiniteOrInvalid())
		{
			result.Span.Fill(Value);
			return;
		}
		Span<Point> pointsSpan = points.Span;
		Span<T> buffer = result.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			if (ValidExtent.Contains(pointsSpan[i]))
			{
				buffer[i] = Value;
			}
			else if (sampleParams.InitializeBufferToNoData)
			{
				buffer[i] = valueOrDefault;
			}
		}
	}
}
