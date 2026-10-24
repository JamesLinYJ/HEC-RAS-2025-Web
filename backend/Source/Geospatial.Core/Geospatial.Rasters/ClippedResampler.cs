using System;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Rasters;

internal class ClippedResampler<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ClippedResampler");

	private readonly IResample<T> _res;

	public Extent ValidExtent { get; }

	public T? NoDataValue => _res.NoDataValue;

	public RasterDefinition RasterDefinition { get; }

	public ClippedResampler(IResample<T> res, Extent newExt)
	{
		_res = res;
		ValidExtent = newExt;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		if (!ValidExtent.Overlaps(args.Raster.Extent))
		{
			args.EnsureBuffer(outData, NoDataValue);
			return;
		}
		if (ValidExtent.ContainsOrEquals(args.Raster.Extent))
		{
			_res.Resample(args, outData);
			return;
		}
		_logger.LogOnce(LogLevel.Warning, "Clipped resample not implemented for partially included tiles - sampling all source material.");
		_res.Resample(args, outData);
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_res.SamplePoints(points, outData, sampleParams);
	}
}
