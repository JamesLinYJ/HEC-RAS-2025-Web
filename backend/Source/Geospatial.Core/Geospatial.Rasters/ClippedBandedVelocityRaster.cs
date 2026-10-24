using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility.Units;

namespace Geospatial.Rasters;

internal class ClippedBandedVelocityRaster<T> : ITimeSeriesVelocityRasterizer<T>, ITimeSeriesVelocityResampler<T>, ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly ITimeSeriesVelocityRasterizer<T> _base;

	private readonly PixelRange _clipPixelRange;

	public int ProfileCount => _base.Times.Count;

	public Extent ValidExtent => _base.RasterDefinition.RangeExtent(_clipPixelRange);

	public T? NoDataValue => _base.NoDataValue;

	public RasterDefinition RasterDefinition => _base.RasterDefinition.GetSubRaster(_clipPixelRange);

	public IReadOnlyList<DateTime> Times => _base.Times;

	public bool CanInterpolateTimesteps => _base.CanInterpolateTimesteps;

	public ClippedBandedVelocityRaster(ITimeSeriesVelocityRasterizer<T> baseRaster, PixelRange pr)
	{
		_base = baseRaster;
		_clipPixelRange = pr;
	}

	public void GetRangeVxVy(PixelRange range, int bandIndex, Memory<T> outVx, Memory<T> outVy, Memory<T> outVelMag)
	{
		_base.GetRangeVxVy(ConvertPixelRange(range), bandIndex, outVx, outVy, outVelMag);
	}

	public void GetRangeMagDir(PixelRange range, int bandIndex, Memory<T> magResult, Memory<T> dirResult)
	{
		_base.GetRangeMagDir(ConvertPixelRange(range), bandIndex, magResult, dirResult);
	}

	public void ResampleVxVy(ResampleArgs args, int bandIndex, Memory<T> outVx, Memory<T> outVy, Memory<T> outVelMag)
	{
		_base.ResampleVxVy(args, bandIndex, outVx, outVy, outVelMag);
	}

	public void ResampleMagDir(ResampleArgs args, int bandIndex, Memory<T> outMag, Memory<T> outDir, DirectionConvention dirConvention = DirectionConvention.GoingToward)
	{
		_base.ResampleMagDir(args, bandIndex, outMag, outDir, dirConvention);
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] outVx, Memory<T>[] outVy, Memory<T>[] outVelMag)
	{
		_base.SamplePointsVxVy(points, bandStart, bandCount, outVx, outVy, outVelMag);
	}

	private PixelRange ConvertPixelRange(PixelRange input)
	{
		return new PixelRange(_clipPixelRange.MinRow + input.MinRow, _clipPixelRange.MinRow + input.MaxRow, _clipPixelRange.MinCol + input.MinCol, _clipPixelRange.MinCol + input.MaxCol);
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandIndex, Memory<T> outVx, Memory<T> outVy, Memory<T> outMagnitude)
	{
		_base.SamplePointsVxVy(points, bandIndex, outVx, outVy, outMagnitude);
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null)
	{
		_base.Resample(args, time, outData, cache);
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		return _base.SamplePointTimeSeries(pt);
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null)
	{
		_base.Resample(args, bandIndex, outData, cache);
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		_base.Resample(args, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		_base.SamplePoints(points, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData)
	{
		_base.SamplePoints(points, bandIndex, outData);
	}
}
