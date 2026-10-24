using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class PinnedProfileRasterizer<T> : IRaster<T>, IResample<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandRaster<T> _ds;

	private readonly int _pfIdx;

	public Extent ValidExtent => _ds.ValidExtent;

	public T? NoDataValue => _ds.NoDataValue;

	public RasterDefinition RasterDefinition => _ds.RasterDefinition;

	public PinnedProfileRasterizer(IMultiBandRaster<T> data, int pf)
	{
		_ds = data;
		_pfIdx = pf;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		_ds.Resample(args, _pfIdx, outData);
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_ds.SamplePoints(points, _pfIdx, outData);
	}

	public void GetRange(PixelRange range, Memory<T> outData)
	{
		throw new NotImplementedException();
	}
}
