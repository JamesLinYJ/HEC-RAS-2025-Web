using System;
using System.Collections.Generic;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class CachedTimeSeriesRasterProvider<T> : CachedMultiBandRasterProvider<T>, ITimeSeriesRasterProvider<T>, IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, IDiscreteTimesteps where T : unmanaged, IEquatable<T>
{
	private readonly ITimeSeriesRasterProvider<T> _inner;

	public IReadOnlyList<DateTime> Times => _inner.Times;

	public CachedTimeSeriesRasterProvider(ITimeSeriesRasterProvider<T> inner, IArrayCacheManager<BlockKey, T> cache)
		: base((IMultiBandRasterProvider<T>)inner, cache)
	{
		_inner = inner;
	}
}
