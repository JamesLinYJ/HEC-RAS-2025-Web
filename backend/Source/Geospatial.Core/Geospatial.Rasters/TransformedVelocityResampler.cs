using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Utility.Memory;
using Utility.Units;

namespace Geospatial.Rasters;

internal class TransformedVelocityResampler : ITimeSeriesVelocityResampler<float>, ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps
{
	public class TransformationException : Exception
	{
		public TransformationException(string msg)
			: base(msg)
		{
		}
	}

	private readonly ITimeSeriesVelocityResampler<float> _resampler;

	protected readonly ISpatialTransformer _transformer;

	protected readonly ISpatialTransformer _reversed;

	protected readonly VelocityTransformOptions _opts;

	protected readonly IValueTransformer<float> _magTransformer;

	protected readonly IValueTransformer<float> _dirTransformer;

	public Extent ValidExtent => _resampler.ValidExtent.Transform(_transformer);

	public float? NoDataValue => _resampler.NoDataValue;

	public IReadOnlyList<DateTime> Times => _resampler.Times;

	public bool CanInterpolateTimesteps => _resampler.CanInterpolateTimesteps;

	public int ProfileCount => _resampler.ProfileCount;

	public TransformedVelocityResampler(ITimeSeriesVelocityResampler<float> res, ISpatialTransformer spatialTransformer, VelocityTransformOptions opts, IValueTransformer<float> magnitudeTransformer, IValueTransformer<float> directionTransformer)
	{
		_resampler = res;
		_transformer = spatialTransformer;
		if (spatialTransformer != null)
		{
			if (!_transformer.IsReversible)
			{
				throw new Exception("spatialTransformer must be reversible.");
			}
			_reversed = spatialTransformer.GetInvertedTransformer();
		}
		_opts = opts;
		_magTransformer = magnitudeTransformer;
		_dirTransformer = directionTransformer;
	}

	public void ResampleVxVy(ResampleArgs args, int profileIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outVelMag)
	{
		if (_transformer != null)
		{
			if (_opts == VelocityTransformOptions.PreserveValues)
			{
				ResampleVxVy_PreserveValues(args, profileIndex, outVx, outVy, outVelMag);
			}
			else if (_opts == VelocityTransformOptions.Scale)
			{
				ResampleVxVy_Scale(args, profileIndex, outVx, outVy, outVelMag);
			}
			else
			{
				if (_opts != VelocityTransformOptions.PreserveMagnitude)
				{
					throw new NotImplementedException();
				}
				ResampleVxVy_PreserveMagnitude(args, profileIndex, outVx, outVy, outVelMag);
			}
		}
		else
		{
			_resampler.ResampleVxVy(args, profileIndex, outVx, outVy, outVelMag);
		}
		if (_magTransformer != null)
		{
			_magTransformer.Transform(outVelMag, NoDataValue);
			VelocityMath.ChangeMagnitude(outVx, outVy, outVelMag, NoDataValue);
		}
		if (_dirTransformer != null)
		{
			float[] floatValues = new float[outVelMag.Length];
			if (NoDataValue.HasValue)
			{
				floatValues.AsSpan().Fill(NoDataValue.Value);
			}
			VelocityMath.ConvertToDir(outVx, outVy, floatValues, NoDataValue, DirectionConvention.GoingToward);
			_dirTransformer.Transform(floatValues, NoDataValue);
			VelocityMath.ChangeDirection(outVx, outVy, floatValues, DirectionConvention.GoingToward, NoDataValue);
		}
	}

	private void ResampleVxVy_PreserveValues(ResampleArgs args, int profileIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outVelMag)
	{
		ResampleArgs resampleArgs = args.Transform(_reversed);
		if (resampleArgs.Raster.IsNull())
		{
			throw new TransformationException("Query raster transformation failed.");
		}
		int cellCount = resampleArgs.Raster.CellCount;
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(cellCount);
		Memory<float> slice = memoryOwner.Memory.Slice(0, cellCount);
		using IMemoryOwner<float> memoryOwner2 = SharedMemoryPool<float>.Rent(cellCount);
		Memory<float> sliceSpan = memoryOwner2.Memory.Slice(0, cellCount);
		using IMemoryOwner<float> memoryOwner3 = SharedMemoryPool<float>.Rent(cellCount);
		Memory<float> outVelMag2 = memoryOwner3.Memory.Slice(0, cellCount);
		_resampler.ResampleVxVy(resampleArgs, profileIndex, slice, sliceSpan, outVelMag2);
		RasterOperations.Warp(sourceData: new Memory<float>[2] { slice, sliceSpan }, targetData: new Memory<float>[2] { outVx, outVy }, sourceRaster: resampleArgs.Raster, targetRaster: args.Raster, targetToSourceTransformer: _reversed);
		args.EnsureBuffer(outVelMag, NoDataValue);
		VelocityMath.ComputeMagnitude(outVx, outVy, outVelMag, NoDataValue);
	}

	private void ResampleVxVy_PreserveMagnitude(ResampleArgs args, int profileIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outVelMag)
	{
		throw new NotImplementedException();
	}

	private void ResampleVxVy_Scale(ResampleArgs args, int profileIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outVelMag)
	{
		args.EnsureBuffer(outVx, NoDataValue);
		args.EnsureBuffer(outVy, NoDataValue);
		args.EnsureBuffer(outVelMag, NoDataValue);
		throw new NotImplementedException();
	}

	public void ResampleMagDir(ResampleArgs args, int profileIndex, Memory<float> outMag, Memory<float> outDir, DirectionConvention dirConvention = DirectionConvention.GoingToward)
	{
		ResampleArgs resampleArgs = args.Transform(_reversed);
		int cellCount = resampleArgs.Raster.CellCount;
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(cellCount);
		Memory<float> slice = memoryOwner.Memory.Slice(0, cellCount);
		using IMemoryOwner<float> memoryOwner2 = SharedMemoryPool<float>.Rent(cellCount);
		Memory<float> sliceSpan = memoryOwner2.Memory.Slice(0, cellCount);
		_resampler.ResampleMagDir(resampleArgs, profileIndex, slice, sliceSpan);
		Memory<float>[] sourceData = new Memory<float>[2] { slice, sliceSpan };
		Memory<float>[] targetData = new Memory<float>[2] { outMag, outDir };
		RasterOperations.Warp(resampleArgs.Raster, sourceData, args.Raster, targetData, _reversed);
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] outVx, Memory<float>[] outVy, Memory<float>[] outVelMag)
	{
		if (_transformer != null)
		{
			if (_opts != VelocityTransformOptions.PreserveValues)
			{
				if (_opts == VelocityTransformOptions.Scale)
				{
					throw new NotImplementedException();
				}
				_ = _opts;
				_ = 1;
				throw new NotImplementedException();
			}
			int length = points.Length;
			using IMemoryOwner<Point> memoryOwner = SharedMemoryPool<Point>.Rent(length);
			Memory<Point> slice = memoryOwner.Memory.Slice(0, length);
			_reversed.Transform(points, slice);
			_resampler.SamplePointsVxVy(slice, bandStart, bandCount, outVx, outVy, outVelMag);
		}
		else
		{
			_resampler.SamplePointsVxVy(points, bandStart, bandCount, outVx, outVy, outVelMag);
		}
		if (_magTransformer != null)
		{
			throw new NotImplementedException();
		}
		if (_dirTransformer != null)
		{
			throw new NotImplementedException();
		}
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<float> outData, ResampleCache cache = null)
	{
		throw new NotImplementedException();
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		return _resampler.SamplePointTimeSeries(pt);
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<float> outData, ResampleCache cache = null)
	{
		_resampler.Resample(args, bandIndex, outData, cache);
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		_resampler.Resample(args, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		_resampler.SamplePoints(points, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		_resampler.SamplePoints(points, bandIndex, outData);
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outMagnitude)
	{
		_resampler.SamplePointsVxVy(points, bandIndex, outVx, outVy, outMagnitude);
	}
}
