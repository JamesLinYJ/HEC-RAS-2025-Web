using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class TransformedTimeSeriesResampler : ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps
{
	private readonly ITimeSeriesResampler<float> _resampler;

	protected readonly ISpatialTransformer _spatialTransformer;

	protected readonly ISpatialTransformer _reversed;

	protected readonly IValueTransformer<float> _valueTransformer;

	protected readonly IValueTransformer<double> _valueTransformerD;

	public Extent ValidExtent
	{
		get
		{
			if (_spatialTransformer != null)
			{
				return _resampler.ValidExtent.Transform(_spatialTransformer);
			}
			return _resampler.ValidExtent;
		}
	}

	public float? NoDataValue => _resampler.NoDataValue;

	public IReadOnlyList<DateTime> Times => _resampler.Times;

	public bool CanInterpolateTimesteps => _resampler.CanInterpolateTimesteps;

	public int ProfileCount => _resampler.ProfileCount;

	public TransformedTimeSeriesResampler(ITimeSeriesResampler<float> resampler, ISpatialTransformer spatialTransformer, IValueTransformer<float> valueTransformer)
	{
		_resampler = resampler;
		_spatialTransformer = spatialTransformer;
		_reversed = spatialTransformer?.GetInvertedTransformer();
		_valueTransformer = valueTransformer;
		if (_valueTransformer == null)
		{
			return;
		}
		_valueTransformerD = new ActionValueTransformer<double>((Memory<double> buf, double? ndv) =>
		{
			Span<double> bufSpan = buf.Span;
			float[] floatValues = new float[buf.Length];
			for (int i = 0; i < buf.Length; i++)
			{
				floatValues[i] = (float)bufSpan[i];
			}
			_valueTransformer.Transform(floatValues, (float?)ndv);
			for (int j = 0; j < buf.Length; j++)
			{
				bufSpan[j] = floatValues[j];
			}
		});
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<float> outData, ResampleCache cache = null)
	{
		if (_reversed != null)
		{
			ResampleArgs warpedArgs = GetWarpedArgs(args);
			if (warpedArgs.Raster.IsNull())
			{
				args.EnsureBuffer(outData, NoDataValue);
				return;
			}
			int cellCount = args.Raster.CellCount;
			using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(cellCount);
			Memory<float> slice = memoryOwner.Memory.Slice(0, cellCount);
			_resampler.Resample(warpedArgs, time, slice);
			RasterOperations.Warp(warpedArgs.Raster, slice, args.Raster, outData, _reversed);
		}
		else
		{
			_resampler.Resample(args, time, outData);
		}
		if (_valueTransformer != null)
		{
			_valueTransformer.Transform(outData, NoDataValue);
		}
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<float> outData, ResampleCache cache = null)
	{
		if (_reversed != null)
		{
			ResampleArgs warpedArgs = GetWarpedArgs(args);
			if (warpedArgs.Raster.IsNull())
			{
				args.EnsureBuffer(outData, NoDataValue);
				return;
			}
			Memory<float> mem;
			using (SharedMemoryPool<float>.Rent(warpedArgs.Raster.CellCount, out mem))
			{
				_resampler.Resample(warpedArgs, profileIndex, mem);
				RasterOperations.Warp(warpedArgs.Raster, mem, args.Raster, outData, _reversed);
			}
		}
		else
		{
			_resampler.Resample(args, profileIndex, outData);
		}
		if (_valueTransformer != null)
		{
			_valueTransformer.Transform(outData, NoDataValue);
		}
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		this.Resample_NaiiveLoop(args, startBandIndex, bandCount, outData);
	}

	private ResampleArgs GetWarpedArgs(ResampleArgs args)
	{
		if (_reversed == null)
		{
			return args;
		}
		return args.Transform(_reversed);
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		if (_reversed != null)
		{
			pt = _reversed.Transform(pt);
		}
		TimeSeries timeSeries = _resampler.SamplePointTimeSeries(pt);
		if (_valueTransformer != null)
		{
			timeSeries = timeSeries.Clone(_valueTransformerD);
		}
		return timeSeries;
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] outData)
	{
		if (_reversed != null)
		{
			int length = points.Length;
			using IMemoryOwner<Point> memoryOwner = SharedMemoryPool<Point>.Rent(length);
			Memory<Point> slice = memoryOwner.Memory.Slice(0, length);
			_reversed.Transform(points, slice);
			_resampler.SamplePoints(slice, bandStart, bandCount, outData);
		}
		else
		{
			_resampler.SamplePoints(points, bandStart, bandCount, outData);
		}
		if (_valueTransformer != null)
		{
			for (int i = 0; i < outData.Length; i++)
			{
				_valueTransformer.Transform(outData[i], -9999f);
			}
		}
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		if (_reversed != null)
		{
			Memory<Point> mem;
			using (SharedMemoryPool<Point>.Rent(points.Length, out mem))
			{
				_reversed.Transform(points, mem);
				_resampler.SamplePoints(mem, bandIndex, outData);
			}
		}
		else
		{
			_resampler.SamplePoints(points, bandIndex, outData);
		}
		if (_valueTransformer != null)
		{
			_valueTransformer.Transform(outData, -9999f);
		}
	}
}
