using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility.Units;

namespace Geospatial.Rasters;

internal class VelocityRasterizer<T> : ITimeSeriesVelocityRasterizer<T>, ITimeSeriesVelocityResampler<T>, ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly ITimeSeriesVelocityResampler<T> _resampler;

	public int ProfileCount => Times.Count;

	public RasterDefinition RasterDefinition { get; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue => _resampler.NoDataValue;

	public IReadOnlyList<DateTime> Times => _resampler.Times;

	public bool CanInterpolateTimesteps => _resampler.CanInterpolateTimesteps;

	public VelocityRasterizer(ITimeSeriesVelocityResampler<T> resampler, RasterDefinition raster)
	{
		_resampler = resampler;
		RasterDefinition = raster;
	}

	public void ResampleVxVy(ResampleArgs args, int bandIndex, Memory<T> outVx, Memory<T> outVy, Memory<T> outVelMag)
	{
		_resampler.ResampleVxVy(args, bandIndex, outVx, outVy, outVelMag);
	}

	public void ResampleMagDir(ResampleArgs args, int bandIndex, Memory<T> outMag, Memory<T> outDir, DirectionConvention dirConvention = DirectionConvention.GoingToward)
	{
		_resampler.ResampleMagDir(args, bandIndex, outMag, outDir, dirConvention);
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] outVx, Memory<T>[] outVy, Memory<T>[] outVelMag)
	{
		_resampler.SamplePointsVxVy(points, bandStart, bandCount, outVx, outVy, outVelMag);
	}

	public void GetRangeVxVy(PixelRange range, int bandIndex, Memory<T> vxResult, Memory<T> vyResult, Memory<T> magResult)
	{
		throw new NotImplementedException();
	}

	public void GetRangeMagDir(PixelRange range, int bandIndex, Memory<T> magResult, Memory<T> dirResult)
	{
		throw new NotImplementedException();
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandIndex, Memory<T> outVx, Memory<T> outVy, Memory<T> outMagnitude)
	{
		_resampler.SamplePointsVxVy(points, bandIndex, outVx, outVy, outMagnitude);
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null)
	{
		_resampler.Resample(args, time, outData, cache);
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		return _resampler.SamplePointTimeSeries(pt);
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null)
	{
		_resampler.Resample(args, bandIndex, outData, cache);
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		_resampler.Resample(args, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		_resampler.SamplePoints(points, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData)
	{
		_resampler.SamplePoints(points, bandIndex, outData);
	}
}
