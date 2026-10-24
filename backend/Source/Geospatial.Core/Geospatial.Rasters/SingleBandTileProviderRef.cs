using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal struct SingleBandTileProviderRef<T> : ITileProvider<T>, IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandTileProvider<T> _ts;

	private readonly int _pfIdx;

	public Extent ValidExtent => _ts.ValidExtent;

	public T? NoDataValue => _ts.NoDataValue;

	public BlockSize BlockSize => _ts.BlockSize;

	public RasterDefinition RasterDefinition => _ts.RasterDefinition;

	public int TileSize => _ts.TileSize;

	public RasterDefinition TiledRasterDefinition => _ts.TiledRasterDefinition;

	public SingleBandTileProviderRef(IMultiBandTileProvider<T> timeSeries, int pf)
	{
		_ts = timeSeries;
		_pfIdx = pf;
	}

	public bool ReadBlock(long blockIndex, Memory<T> block)
	{
		return _ts.ReadBlock(_pfIdx, blockIndex, block);
	}
}
