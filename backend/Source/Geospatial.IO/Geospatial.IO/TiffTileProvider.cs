using System;
using System.Linq;
using System.Runtime.InteropServices;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using TiffBinaryReader;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.IO;

internal class TiffTileProvider<T> : ITileProvider<T>, IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition, IPinnedTileProvider<T> where T : unmanaged, IEquatable<T>
{
	private readonly ILogger _logger = LogHelper.GetLogger("TiffTileProvider");

	private readonly TiffFile _reader;

	private readonly IArrayCacheManager<int, T> _tileCache;

	private readonly int _sizeofT;

	private readonly int _pyramidLevel;

	public string ErrorMessage { get; private set; }

	public RasterDefinition TiledRasterDefinition { get; private set; }

	public RasterDefinition RasterDefinition { get; private set; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public bool IsGeoReferenced { get; private set; }

	public T? NoDataValue { get; set; }

	public int TileCount => TiledRasterDefinition.CellCount;

	public int TileSize { get; private set; }

	public BlockSize BlockSize => new BlockSize(TileSize, TileSize);

	public TiffTileProvider(TiffFile reader, int directoryIdx, IArrayCacheManager<int, T> tileCache)
	{
		_reader = reader;
		TiffDirectory tiffDirectory = _reader.Directories[directoryIdx];
		Initialize(tiffDirectory);
		_pyramidLevel = directoryIdx;
		_sizeofT = Marshal.SizeOf<T>();
		_tileCache = tileCache;
		if (tiffDirectory.Metadata.TryGetNoDataAsValue<T>(out var noDataValue))
		{
			NoDataValue = noDataValue;
		}
		else
		{
			NoDataValue = null;
		}
	}

	private void Initialize(TiffDirectory dir)
	{
		RasterDefinition = TryReadRasterInfo(dir);
		if (RasterDefinition.IsNull())
		{
			IsGeoReferenced = false;
			int yComponent = (int)dir.Metadata.ImageHeight;
			int cols = (int)dir.Metadata.ImageWidth;
			RasterDefinition = new RasterDefinition(new Point(0.0, yComponent), 1.0, yComponent, cols);
		}
		TileSize = dir.Metadata.TileWidth;
		int tileHeight = dir.Metadata.TileHeight;
		if (TileSize != tileHeight)
		{
			throw new NotSupportedException("TileWidth must match TileHeight currently.");
		}
		TiledRasterDefinition = RasterDefinition.TiledRaster(TileSize);
		if (!dir.CanReadData)
		{
			ErrorMessage = dir.LoadResult.GetConcatenatedMessages();
		}
	}

	public static RasterDefinition TryReadRasterInfo(TiffDirectory dir)
	{
		if (dir.Tiff.HasParentTiff)
		{
			RasterDefinition rasterDefinition = TryReadRasterInfo(dir.Tiff.ParentTiff.Directories[0]);
			if (rasterDefinition.IsNull())
			{
				return RasterDefinition.NullRaster;
			}
			int rows = (int)dir.Metadata.ImageHeight;
			int cols = (int)dir.Metadata.ImageWidth;
			return new RasterDefinition(rasterDefinition.Extent, rows, cols);
		}
		if (dir.DirectoryIndex == 0L)
		{
			Tag tag = dir.Metadata.Tags.FirstOrDefault((Tag t) => t.TagType.Name == "ModelTiePoint");
			if (tag == null)
			{
				return RasterDefinition.NullRaster;
			}
			Span<double> valuesAs = tag.ValuesAs<double>();
			double x = valuesAs[3];
			double y = valuesAs[4];
			Tag tag2 = dir.Metadata.Tags.FirstOrDefault((Tag t) => t.TagType.Name == "ModelPixelScale");
			if (tag2 == null)
			{
				return RasterDefinition.NullRaster;
			}
			double cellSize = tag2.ValuesAs<double>()[0];
			int rows2 = (int)dir.Metadata.ImageHeight;
			int cols2 = (int)dir.Metadata.ImageWidth;
			return new RasterDefinition(new Point(x, y), cellSize, rows2, cols2);
		}
		RasterDefinition rasterDefinition2 = TryReadRasterInfo(dir.Tiff.Directories[0]);
		if (rasterDefinition2.IsNull())
		{
			return RasterDefinition.NullRaster;
		}
		int rows3 = (int)dir.Metadata.ImageHeight;
		int cols3 = (int)dir.Metadata.ImageWidth;
		return new RasterDefinition(rasterDefinition2.Extent, rows3, cols3);
	}

	protected void ReadTileInternal(int pyramidLevel, int tileIdx, Memory<T> data)
	{
		_reader.Directories[pyramidLevel].GetTile(tileIdx, data);
	}

	public bool ReadBlock(long idx1D, Memory<T> data)
	{
		int tileSizePolynomial = TileSize * TileSize;
		if (data.Length != tileSizePolynomial)
		{
			throw new ArgumentException();
		}
		int idx1D2 = (int)idx1D;
		(TiffTileProvider<T>, int, int, Memory<T>) model = (this, _pyramidLevel, idx1D2, data);
		_tileCache.BuildOrUseExisting(idx1D2, tileSizePolynomial, model, ((TiffTileProvider<T>, int, int, Memory<T>) tuple, Memory<T> mem) =>
		{
			tuple.Item1.ReadTileInternal(tuple.Item2, tuple.Item3, mem);
		}, ((TiffTileProvider<T>, int, int, Memory<T>) tuple, Memory<T> mem) =>
		{
			mem.CopyTo(tuple.Item4);
		});
		return true;
	}

	public bool TryReadBlockPinned(long idx1D, out ReadOnlyMemory<T> data)
	{
		int size = TileSize * TileSize;
		int idx1D2 = (int)idx1D;
		(TiffTileProvider<T>, int, int) model = (this, _pyramidLevel, idx1D2);
		return _tileCache.TryUseExistingOrBuildPinned(idx1D2, size, model, ((TiffTileProvider<T>, int, int) tuple, Memory<T> mem) =>
		{
			tuple.Item1.ReadTileInternal(tuple.Item2, tuple.Item3, mem);
		}, out data);
	}

	public void ReleaseBlock(long idx1D)
	{
		_tileCache.Unpin((int)idx1D);
	}

	public void EvictCache()
	{
	}
}
