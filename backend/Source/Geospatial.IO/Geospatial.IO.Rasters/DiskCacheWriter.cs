using System;
using System.IO;
using System.Runtime.InteropServices;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility.Files;
using Utility.Memory;

namespace Geospatial.IO.Rasters;

public class DiskCacheWriter<T> : ITileReaderWriter<T>, ITileProvider<T>, IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition, ITileWriter<T>, IRasterWriter<T>, IDisposable where T : unmanaged, IEquatable<T>
{
	public const int DefaultTileSize = 256;

	private bool disposedValue;

	private readonly string _tempFile;

	private readonly Pool<Stream> _fsPool;

	public BlockSize BlockSize { get; }

	public int TileSize => 256;

	public RasterDefinition TiledRasterDefinition { get; }

	public RasterDefinition RasterDefinition { get; }

	public bool CanWriteTilesInParallel { get; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue { get; }

	public int TileCount { get; }

	public int BytesPerTile { get; }

	public long TotalBytes => BytesPerTile * TileCount;

	public bool ReadBlock(long blockIndex, Memory<T> block)
	{
		using PoolHandle<Stream> poolHandle = _fsPool.Acquire();
		Stream data = poolHandle.Data;
		data.Position = blockIndex * BytesPerTile;
		Span<byte> buffer = MemoryMarshal.AsBytes(block.Span);
		return data.Read(buffer) == buffer.Length;
	}

	public void WriteBlock(int blockIndex, Memory<T> block)
	{
		using PoolHandle<Stream> poolHandle = _fsPool.Acquire();
		Stream data = poolHandle.Data;
		data.Position = (long)blockIndex * (long)BytesPerTile;
		data.Write(MemoryMarshal.AsBytes(block.Span));
	}

	public RasterDefinition GetBlockRaster(int blockIndex)
	{
		return RasterDefinition.GetBlockRaster(BlockSize, blockIndex);
	}

	public DiskCacheWriter(RasterDefinition targetRasterDefinition, T? noDataValue, string tempFilePath = null)
	{
		RasterDefinition = targetRasterDefinition;
		NoDataValue = noDataValue;
		_tempFile = tempFilePath ?? Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		BlockSize = new BlockSize(256, 256);
		BytesPerTile = GetBlockRaster(0).CellCount * Marshal.SizeOf<T>();
		targetRasterDefinition.GetBlockDimensions(BlockSize, out var blocksWide, out var blocksTall);
		TileCount = blocksWide * blocksTall;
		int rows = blocksTall * BlockSize.Height;
		int cols = blocksWide * BlockSize.Width;
		TiledRasterDefinition = new RasterDefinition(targetRasterDefinition.Extent.UpperLeft, targetRasterDefinition.CellSize, rows, cols);
		_fsPool = new Pool<Stream>(() => new FileStream(_tempFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite));
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposedValue)
		{
			FileHelpers.TryDelete(_tempFile);
			disposedValue = true;
		}
	}

	~DiskCacheWriter()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
