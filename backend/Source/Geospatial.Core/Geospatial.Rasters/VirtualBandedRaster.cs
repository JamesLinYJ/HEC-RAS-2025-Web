using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class VirtualBandedRaster<T> : IMultiBandRaster<T>, IMultiBandResampler<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandResampler<T> _res;

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue => _res.NoDataValue;

	public RasterDefinition RasterDefinition { get; }

	public int ProfileCount => _res.ProfileCount;

	public VirtualBandedRaster(IMultiBandResampler<T> res, RasterDefinition rdef)
	{
		_res = res;
		RasterDefinition = rdef;
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<T> outData, ResampleCache cache = null)
	{
		_res.Resample(args, profileIndex, outData, cache);
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] outData)
	{
		_res.SamplePoints(points, bandStart, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData)
	{
		_res.SamplePoints(points, bandIndex, outData);
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		ResampleArgs args = new ResampleArgs(RasterDefinition.GetSubRaster(range));
		_res.Resample(args, bandIndex, result);
	}
}
