using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class ClippedBandedRaster<T> : IMultiBandRaster<T>, IMultiBandResampler<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandRaster<T> _base;

	private readonly PixelRange _clipPixelRange;

	public int ProfileCount => _base.ProfileCount;

	public Extent ValidExtent => _base.RasterDefinition.RangeExtent(_clipPixelRange);

	public T? NoDataValue => _base.NoDataValue;

	public RasterDefinition RasterDefinition => _base.RasterDefinition.GetSubRaster(_clipPixelRange);

	public ClippedBandedRaster(IMultiBandRaster<T> baseRaster, PixelRange pr)
	{
		_base = baseRaster;
		_clipPixelRange = pr;
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		_base.GetRange(ConvertPixelRange(range), bandIndex, result);
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null)
	{
		_base.Resample(args, bandIndex, outData, cache);
		CLIP(args.Raster, outData);
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		_base.Resample(args, startBandIndex, bandCount, outData);
		for (int i = 0; i < outData.Length; i++)
		{
			CLIP(args.Raster, outData[i]);
		}
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		_base.SamplePoints(points, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData)
	{
		_base.SamplePoints(points, bandIndex, outData);
	}

	private void CLIP(RasterDefinition rdef, Memory<T> data)
	{
		Extent preserveExtent = RasterDefinition.RangeExtent(_clipPixelRange);
		RasterOperations.PreserveRegion(rdef, data, preserveExtent, NoDataValue.GetValueOrDefault());
	}

	private PixelRange ConvertPixelRange(PixelRange input)
	{
		return new PixelRange(_clipPixelRange.MinRow + input.MinRow, _clipPixelRange.MinRow + input.MaxRow, _clipPixelRange.MinCol + input.MinCol, _clipPixelRange.MinCol + input.MaxCol);
	}
}
