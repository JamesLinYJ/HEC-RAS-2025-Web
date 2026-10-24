using System;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class TransformedResampler<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private readonly IResample<T> _resampler;

	protected readonly ISpatialTransformer _transformer;

	protected readonly ISpatialTransformer _reversed;

	public Extent ValidExtent { get; }

	public T? NoDataValue => _resampler.NoDataValue;

	public TransformedResampler(IResample<T> resampler, ISpatialTransformer transformer)
	{
		if (!transformer.IsReversible)
		{
			throw new ArgumentException("Must have a reversible transformer for TransformedResampler");
		}
		_resampler = resampler;
		_transformer = transformer;
		_reversed = transformer.GetInvertedTransformer();
		ValidExtent = _resampler.ValidExtent.Transform(transformer);
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		ResampleArgs resampleArgs = args.Transform(_reversed);
		if (!resampleArgs.Raster.IsValid())
		{
			args.EnsureBuffer(outData, NoDataValue);
			return;
		}
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(resampleArgs.Raster.CellCount, NoDataValue.GetValueOrDefault(), out mem))
		{
			_resampler.Resample(resampleArgs, mem);
			RasterOperations.Warp(resampleArgs.Raster, mem, args.Raster, outData, _reversed);
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		Point[] transform = _reversed.Transform(points);
		_resampler.SamplePoints(transform, outData);
	}
}
