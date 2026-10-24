using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class AccumulatedAsRateRasterizer<T> : ITimeSeriesRaster<T>, ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, IMultiBandRaster<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IAccumulatedRasterizer<T> _rasterizer;

	public Extent ValidExtent => _rasterizer.ValidExtent;

	public T? NoDataValue => _rasterizer.NoDataValue;

	public bool CanInterpolateTimesteps => false;

	public IReadOnlyList<DateTime> Times => _rasterizer.Times;

	public int ProfileCount => _rasterizer.Times.Count;

	public RasterDefinition RasterDefinition => _rasterizer.RasterDefinition;

	public AccumulatedAsRateRasterizer(IAccumulatedRasterizer<T> rasterizer)
	{
		_rasterizer = rasterizer;
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null)
	{
		_rasterizer.ResampleRates(args, Times[bandIndex], outData);
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		_rasterizer.ResampleRates(new ResampleArgs(_rasterizer.RasterDefinition.GetSubRaster(range)), Times[bandIndex], result);
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null)
	{
		_rasterizer.ResampleRates(args, time, outData);
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		throw new NotImplementedException();
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		throw new NotImplementedException();
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData)
	{
		throw new NotImplementedException();
	}
}
