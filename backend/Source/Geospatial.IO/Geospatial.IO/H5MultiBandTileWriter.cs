using System;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.IO;

public class H5MultiBandTileWriter<T> : IMultiBandTileReaderWriter<T>, IMultiBandTileProvider<T>, IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition, IMultiBandTileWriter<T>, IMultiBandRasterWriter<T> where T : unmanaged, IEquatable<T>
{
	public const int DefaultTileSize = 256;

	private static readonly ILogger _logger = LogHelper.GetLogger("H5TileWriter");

	private bool _isDisposed;

	private readonly bool _shouldClean;

	private readonly H5Writer _hw;

	private readonly string _dsetName;

	private H5DatasetID _dset;

	public string Filename { get; }

	public bool CanWriteTilesInParallel { get; } = true;

	public RasterDefinition TiledRasterDefinition { get; }

	public RasterDefinition RasterDefinition { get; }

	public int TileSize => 256;

	public BlockSize BlockSize { get; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue { get; }

	public Projection Projection { get; set; }

	public H5Writer Writer => _hw;

	public int ProfileCount { get; }

	public BlockSize VerticallyChunkedBlockSize
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public H5MultiBandTileWriter(string filename, string datasetName, RasterDefinition worldRaster, int profileCount, T? noDataValue, Projection projection = null)
	{
		Filename = filename;
		_dsetName = datasetName;
		RasterDefinition = worldRaster;
		ProfileCount = profileCount;
		TiledRasterDefinition = RasterDefinition.TiledRaster(TileSize);
		NoDataValue = noDataValue;
		Projection = projection;
		BlockSize = new BlockSize(TileSize, TileSize);
		_shouldClean = true;
		_hw = new H5Writer(filename);
		InitDs();
	}

	public H5MultiBandTileWriter(H5Writer writer, string datasetName, RasterDefinition worldRaster, int profileCount, T? noDataValue, Projection projection = null)
	{
		_hw = writer;
		Filename = writer.Filename;
		_dsetName = datasetName;
		RasterDefinition = worldRaster;
		ProfileCount = profileCount;
		TiledRasterDefinition = RasterDefinition.TiledRaster(TileSize);
		NoDataValue = noDataValue;
		Projection = projection;
		BlockSize = new BlockSize(TileSize, TileSize);
		_shouldClean = false;
		InitDs();
	}

	private void InitDs()
	{
		_hw.Create2dExtendibleDataset(_dsetName, 1, BlockSize.PixelCount, NoDataValue.GetValueOrDefault());
		_dset = _hw.OpenDataset(_dsetName);
		ulong nRows = (ulong)ProfileCount;
		ulong nCols = (ulong)(TiledRasterDefinition.CellCountLong * TileSize * TileSize);
		_hw.SetExtent(_dset, nRows, nCols);
		H5RasterWriter.WriteNoData(_hw, _dsetName, NoDataValue.GetValueOrDefault());
		H5RasterWriter.SignRaster(_hw, _dsetName, RasterDefinition, isTiledStorage: true);
	}

	public void WriteBlock(int profileIndex, int blockIndex, Memory<T> data)
	{
		int[] chunkIndex = new int[2] { profileIndex, blockIndex };
		_hw.WriteChunkDirect(_dset, chunkIndex, data);
	}

	public bool ReadBlock(int profileIndex, long blockIndex, Memory<T> data)
	{
		int[] chunkIndex = new int[2]
		{
			profileIndex,
			(int)blockIndex
		};
		_hw.ReadChunkDirect(_dset, chunkIndex, data);
		return true;
	}

	public RasterDefinition GetTileRasterDefinition(Pixel tileIdx)
	{
		return new RasterDefinition(TiledRasterDefinition.CellExtent(tileIdx), TileSize, TileSize);
	}

	public bool ReadVerticalBlock(int verticalBlockIndex, Memory<T> block)
	{
		throw new NotImplementedException();
	}

	public void Flush()
	{
		_dset.Dispose();
		_dset = _hw.OpenDataset(_dsetName);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			_dset.Dispose();
			if (_shouldClean)
			{
				_hw.Dispose();
			}
			_isDisposed = true;
		}
	}

	~H5MultiBandTileWriter()
	{
		if (!_isDisposed)
		{
			_logger.LogWarning("H5TileWriter not disposed - caught by Garbage Collector. ({fn})", Filename);
		}
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
