using System;
using System.Collections.Generic;

namespace Geospatial.Rasters;

internal class VirtualTimeSeriesRaster<T> : VirtualTimeSeriesResampler<T>, ITimeSeriesRaster<T>, ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, IMultiBandRaster<T>, IRasterDefinition, ICacheEviction where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandRaster<T> _banded;

	public RasterDefinition RasterDefinition => _banded.RasterDefinition;

	public VirtualTimeSeriesRaster(IMultiBandRaster<T> banded, IList<DateTime> timesteps)
		: base((IMultiBandResampler<T>)banded, timesteps)
	{
		_banded = banded;
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		ResampleArgs args = new ResampleArgs(RasterDefinition.GetSubRaster(range));
		Resample(args, bandIndex, result);
	}

	public new void EvictCache()
	{
		(_banded as ICacheEviction)?.EvictCache();
	}

	public new IDisposable TryPauseCache()
	{
		return (_banded as ICacheEviction)?.TryPauseCache() ?? NoOpDisposable.Instance;
	}
}
