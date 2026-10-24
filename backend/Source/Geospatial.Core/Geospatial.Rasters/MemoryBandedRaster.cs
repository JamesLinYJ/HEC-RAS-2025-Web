using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class MemoryBandedRaster<T> : IMultiBandRaster<T>, IMultiBandResampler<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IList<Memory<T>> _bands;

	public int ProfileCount => _bands.Count;

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue { get; set; }

	public RasterDefinition RasterDefinition { get; }

	public MemoryBandedRaster(RasterDefinition rdef, IList<Memory<T>> bands)
	{
		RasterDefinition = rdef;
		_bands = bands;
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null)
	{
		new RasterData<T>(RasterDefinition, _bands[bandIndex]).Resample(args, outData);
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		this.Resample_NaiiveLoop(args, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData)
	{
		throw new NotImplementedException();
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		throw new NotImplementedException();
	}
}
