using System;
using System.IO;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using TiffBinaryReader;
using TiffBinaryReader.TagTypeEnums;
using Utility.Logging;

namespace Geospatial.IO;

internal class TiffTileWriter<T> : ITileReaderWriter<T>, ITileProvider<T>, IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition, ITileWriter<T>, IRasterWriter<T>, IDisposable where T : unmanaged, IEquatable<T>
{
	public const int DefaultTileSize = 256;

	private static readonly ILogger _logger = LogHelper.GetLogger("TiffTileWriter");

	private bool _isDisposed;

	private readonly int _directoryIdx;

	private readonly bool _shouldClean;

	protected readonly TiffFileWriter _tiff;

	public string Filename { get; }

	public bool CanWriteTilesInParallel { get; } = true;

	public RasterDefinition TiledRasterDefinition { get; }

	public RasterDefinition RasterDefinition { get; }

	public int TileSize => 256;

	public BlockSize BlockSize { get; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue { get; }

	public Projection Projection { get; set; }

	public TiffFileWriter TiffWriter => _tiff;

	public static TiffTileWriter<byte> CreateImageTiff(string filename, RasterDefinition rasterDefinition, Projection projection = null, bool includeAlpha = false)
	{
		TagMetadata md = (includeAlpha ? TagMetadata.ForImageRGBA(rasterDefinition.Cols, rasterDefinition.Rows) : TagMetadata.ForImage(rasterDefinition.Cols, rasterDefinition.Rows));
		return new TiffTileWriter<byte>(filename, rasterDefinition, null, md, projection);
	}

	public TiffTileWriter(string filename, RasterDefinition worldRaster, T? noDataValue, int bandCount = 1, Projection projection = null)
	{
		if (File.Exists(filename))
		{
			throw new Exception(filename + " already exists.");
		}
		Filename = filename;
		RasterDefinition = worldRaster;
		TiledRasterDefinition = RasterDefinition.TiledRaster(TileSize);
		NoDataValue = noDataValue;
		Projection = projection;
		BlockSize = new BlockSize(256, 256);
		TagMetadata directoryToCreate = TagMetadata.ForWriting(RasterDefinition.Cols, RasterDefinition.Rows, isOverlay: false, noDataValue, bandCount, PlanarConfigs.Contiguous);
		_tiff = new TiffFileWriter(Filename, directoryToCreate);
		_shouldClean = true;
		_directoryIdx = 0;
	}

	public TiffTileWriter(string filename, RasterDefinition worldRaster, T? noDataValue, TagMetadata md, Projection projection = null)
	{
		if (File.Exists(filename))
		{
			throw new Exception(filename + " already exists.");
		}
		Filename = filename;
		RasterDefinition = worldRaster;
		TiledRasterDefinition = RasterDefinition.TiledRaster(TileSize);
		NoDataValue = noDataValue;
		Projection = projection;
		BlockSize = new BlockSize(256, 256);
		_tiff = new TiffFileWriter(Filename, md);
		_shouldClean = true;
		_directoryIdx = 0;
	}

	public TiffTileWriter(TiffFileWriter writer, int directory, RasterDefinition worldRaster, T? noDataValue)
	{
		RasterDefinition = worldRaster;
		TiledRasterDefinition = RasterDefinition.TiledRaster(TileSize);
		NoDataValue = noDataValue;
		BlockSize = new BlockSize(256, 256);
		_tiff = writer;
		_shouldClean = false;
		_directoryIdx = directory;
	}

	public void WriteBlock(int blockIdx, Memory<T> data)
	{
		_tiff.WriteTileParallel(_directoryIdx, blockIdx, data);
	}

	public bool ReadBlock(long blockIdx, Memory<T> data)
	{
		if (_tiff.TryGetTile(_directoryIdx, (int)blockIdx, data))
		{
			return true;
		}
		data.Span.Fill(NoDataValue.GetValueOrDefault());
		return false;
	}

	public RasterDefinition GetTileRasterDefinition(Pixel tileIdx)
	{
		return new RasterDefinition(TiledRasterDefinition.CellExtent(tileIdx), TileSize, TileSize);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		if (_shouldClean)
		{
			_tiff.Dispose();
			GDALRaster.SetLocation(Filename, RasterDefinition);
			if (Projection != null)
			{
				GDALRaster.SetProjection(Filename, Projection);
			}
		}
		_isDisposed = true;
	}

	~TiffTileWriter()
	{
		if (!_isDisposed && _shouldClean)
		{
			_logger.LogWarning("TiffTileWriter not disposed - caught by Garbage Collector. ({fn})", Filename);
		}
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
