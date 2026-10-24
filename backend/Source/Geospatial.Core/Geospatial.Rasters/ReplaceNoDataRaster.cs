using System;
using Geospatial.Vectors;
using Utility;

namespace Geospatial.Rasters;

internal class ReplaceNoDataRaster<T> : IRaster<T>, IResample<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IRaster<T> _resampler;

	private readonly T _actualNDV;

	public Extent ValidExtent => _resampler.ValidExtent;

	public T? NoDataValue => _actualNDV;

	public RasterDefinition RasterDefinition => _resampler.RasterDefinition;

	public ReplaceNoDataRaster(IRaster<T> resampler, T newNodataValue)
	{
		_resampler = resampler;
		_actualNDV = newNodataValue;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		_resampler.Resample(args, outData);
		Convert(outData.Span, _resampler.NoDataValue, _actualNDV);
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_resampler.SamplePoints(points, outData, sampleParams);
		Convert(outData.Span, _resampler.NoDataValue, _actualNDV);
	}

	private static void Convert(Span<T> sp, T? from, T to)
	{
		if (from.HasValue)
		{
			Buffers.ReplaceValue(sp, from.Value, to);
		}
	}

	public void GetRange(PixelRange range, Memory<T> outData)
	{
		_resampler.GetRange(range, outData);
	}
}
