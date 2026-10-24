using System;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class TransformedBandedResampler<T> : IMultiBandResampler<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandResampler<T> _baseRes;

	private readonly ISpatialTransformer _transformer;

	protected readonly ISpatialTransformer _reversed;

	public int ProfileCount => _baseRes.ProfileCount;

	public Extent ValidExtent { get; }

	public T? NoDataValue => _baseRes.NoDataValue;

	public TransformedBandedResampler(IMultiBandResampler<T> baseRes, ISpatialTransformer transformer)
	{
		_baseRes = baseRes;
		_transformer = transformer;
		_reversed = transformer.GetInvertedTransformer();
		try
		{
			ValidExtent = transformer.TransformExtentRasterized(baseRes.ValidExtent);
			if (!ValidExtent.IsFiniteAndValid())
			{
				ValidExtent = Extent.EffectivelyInfinite;
			}
		}
		catch
		{
			ValidExtent = Extent.EffectivelyInfinite;
		}
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<T> outData, ResampleCache cache = null)
	{
		args.EnsureBuffer(outData, NoDataValue);
		if (!args.TryTransform(_reversed, out var warpedArgs))
		{
			return;
		}
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(warpedArgs.Raster.CellCount, out mem))
		{
			_baseRes.Resample(warpedArgs, profileIndex, mem);
			RasterOperations.Warp(warpedArgs.Raster, mem, args.Raster, outData, _reversed);
		}
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<T> outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		this.SamplePoints_NaiiveLoop(points, startBandIndex, bandCount, outData);
	}
}
