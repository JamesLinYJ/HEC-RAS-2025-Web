using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class VirtualRaster<T> : IRaster<T>, IResample<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IResample<T> _res;

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue => _res.NoDataValue;

	public RasterDefinition RasterDefinition { get; }

	public VirtualRaster(IResample<T> res, RasterDefinition rdef)
	{
		_res = res;
		RasterDefinition = rdef;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		_res.Resample(args, outData);
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_res.SamplePoints(points, outData, sampleParams);
	}

	public void GetRange(PixelRange range, Memory<T> outData)
	{
		ResampleArgs args = new ResampleArgs(RasterDefinition.GetSubRaster(range));
		Resample(args, outData);
	}
}
