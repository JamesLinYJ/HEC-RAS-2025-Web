using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class SingleProfileChainWriter<T> : ITileReaderWriter<T>, ITileProvider<T>, IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition, ITileWriter<T>, IRasterWriter<T> where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandTileReaderWriter<T> _writer;

	private readonly int _pinnedProfile;

	public BlockSize BlockSize => _writer.BlockSize;

	public int TileSize => _writer.TileSize;

	public RasterDefinition TiledRasterDefinition => _writer.TiledRasterDefinition;

	public bool CanWriteTilesInParallel => _writer.CanWriteTilesInParallel;

	public RasterDefinition RasterDefinition => _writer.RasterDefinition;

	public Extent ValidExtent => _writer.ValidExtent;

	public T? NoDataValue => _writer.NoDataValue;

	public SingleProfileChainWriter(IMultiBandTileReaderWriter<T> writer, int pinnedProfile)
	{
		_writer = writer;
		_pinnedProfile = pinnedProfile;
	}

	public bool ReadBlock(long blockIndex, Memory<T> block)
	{
		return _writer.ReadBlock(_pinnedProfile, blockIndex, block);
	}

	public void WriteBlock(int blockIndex, Memory<T> block)
	{
		_writer.WriteBlock(_pinnedProfile, blockIndex, block);
	}
}
