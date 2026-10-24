using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class SingleBandTileProvider<T> : ITileProvider<T>, IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandTileProvider<T> _src;

	private readonly int _bandIndex;

	public BlockSize BlockSize => _src.BlockSize;

	public RasterDefinition RasterDefinition => _src.RasterDefinition;

	public Extent ValidExtent => _src.ValidExtent;

	public T? NoDataValue => _src.NoDataValue;

	public int TileSize => _src.TileSize;

	public RasterDefinition TiledRasterDefinition => _src.TiledRasterDefinition;

	public SingleBandTileProvider(IMultiBandTileProvider<T> src, int bandIndex)
	{
		_src = src;
		_bandIndex = bandIndex;
	}

	public bool ReadBlock(long blockIndex, Memory<T> block)
	{
		return _src.ReadBlock(_bandIndex, blockIndex, block);
	}
}
