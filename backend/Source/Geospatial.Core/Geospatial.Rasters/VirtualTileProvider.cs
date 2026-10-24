using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class VirtualTileProvider<T> : ITileProvider<T>, IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IResample<T> _resampler;

	private readonly SamplingParams _sampleParams;

	public int TileSize { get; }

	public RasterDefinition TiledRasterDefinition { get; }

	public BlockSize BlockSize { get; }

	public RasterDefinition RasterDefinition { get; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue => _resampler.NoDataValue;

	public VirtualTileProvider(IResample<T> resampler, double cellSize, int tileSize = 256, SamplingParams? sampleParams = null)
	{
		_resampler = resampler;
		_sampleParams = sampleParams ?? SamplingParams.Default;
		TileSize = tileSize;
		BlockSize = new BlockSize(TileSize, TileSize);
		RasterDefinition = new RasterDefinition(_resampler.ValidExtent, cellSize);
		TiledRasterDefinition = RasterDefinition.TiledRaster(TileSize);
	}

	public bool ReadBlock(long blockIndex, Memory<T> block)
	{
		_resampler.Resample(new ResampleArgs(GetTileRasterDef(blockIndex), _sampleParams), block);
		return true;
	}

	private RasterDefinition GetTileRasterDef(long blockIndex)
	{
		return new RasterDefinition(TiledRasterDefinition.CellExtent(blockIndex), TileSize, TileSize);
	}
}
