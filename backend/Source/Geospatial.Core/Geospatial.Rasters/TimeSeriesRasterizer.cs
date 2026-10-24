using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class TimeSeriesRasterizer<T> : ITimeSeriesRaster<T>, ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, IMultiBandRaster<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly ITimeSeriesResampler<T> _resampler;

	public int ProfileCount => Times.Count;

	public RasterDefinition RasterDefinition { get; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue => _resampler.NoDataValue;

	public bool CanInterpolateTimesteps => true;

	public IReadOnlyList<DateTime> Times => _resampler.Times;

	public TimeSeriesRasterizer(ITimeSeriesResampler<T> resampler, RasterDefinition raster)
	{
		_resampler = resampler;
		RasterDefinition = raster;
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null)
	{
		PixelRange pixelRange = RasterDefinition.GetPixelRange(args.Raster.Extent);
		RasterDefinition registeredRaster = RasterDefinition.GetRegisteredRaster(pixelRange);
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(registeredRaster.CellCount, out mem))
		{
			_resampler.Resample(args.Clone(registeredRaster), time, mem);
			new RasterData<T>(registeredRaster, mem).Resample(args, outData);
		}
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<T> outData, ResampleCache cache = null)
	{
		Resample(args, Times[profileIndex], outData);
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<T> result)
	{
		_resampler.SamplePoints(points, profileIndex, result);
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		return _resampler.SamplePointTimeSeries(pt);
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		ResampleArgs args = new ResampleArgs(RasterDefinition.GetSubRaster(range));
		Resample(args, Times[bandIndex], result);
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] outData)
	{
		_resampler.SamplePoints(points, bandStart, bandCount, outData);
	}
}
