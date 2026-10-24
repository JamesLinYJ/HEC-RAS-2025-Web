using System;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class ComposedMultiBandResampler<T> : IMultiBandResampler<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandResampler<T> _baseRes;

	private readonly IMultiBandResampler<T> _altBandedResampler;

	private readonly IResample<T> _altResampler;

	private readonly ConverterDelegates.Convert<T> _composeInPlace;

	public int ProfileCount => _baseRes.ProfileCount;

	public Extent ValidExtent => _baseRes.ValidExtent;

	public T? NoDataValue => _baseRes.NoDataValue;

	public ComposedMultiBandResampler(IMultiBandResampler<T> baseRes, IResample<T> altResampler, ConverterDelegates.Convert<T> composeInPlace)
	{
		_baseRes = baseRes;
		_altResampler = altResampler;
		_composeInPlace = composeInPlace;
	}

	public ComposedMultiBandResampler(IMultiBandResampler<T> baseRes, IMultiBandResampler<T> altResampler, ConverterDelegates.Convert<T> composeInPlace)
	{
		if (altResampler.ProfileCount != baseRes.ProfileCount)
		{
			throw new ArgumentException("Input resamplers must have the same number of profiles to compose.");
		}
		_baseRes = baseRes;
		_altBandedResampler = altResampler;
		_composeInPlace = composeInPlace;
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<T> outData, ResampleCache cache = null)
	{
		args.EnsureBuffer(outData, NoDataValue);
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(args.Raster.CellCount, out mem))
		{
			_baseRes.Resample(args, profileIndex, mem);
			GetInternal(args, profileIndex, outData);
			_composeInPlace(mem, outData);
		}
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<T> outData)
	{
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(points.Length, out mem))
		{
			_baseRes.SamplePoints(points, profileIndex, mem);
			GetInternal(points, profileIndex, outData);
			_composeInPlace(mem, outData);
		}
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		this.SamplePoints_NaiiveLoop(points, startBandIndex, bandCount, outData);
	}

	private void GetInternal(ResampleArgs args, int profileIndex, Memory<T> outData)
	{
		if (_altResampler != null)
		{
			_altResampler.Resample(args, outData);
		}
		else
		{
			_altBandedResampler.Resample(args, profileIndex, outData);
		}
	}

	private void GetInternal(Memory<Point> points, int profileIndex, Memory<T> outData)
	{
		if (_altResampler != null)
		{
			_altResampler.SamplePoints(points, outData);
		}
		else
		{
			_altBandedResampler.SamplePoints(points, profileIndex, outData);
		}
	}
}
