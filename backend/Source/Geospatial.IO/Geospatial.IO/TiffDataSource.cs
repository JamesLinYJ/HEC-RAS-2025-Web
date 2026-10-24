using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using TiffBinaryReader;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.IO;

public class TiffDataSource<T> : IPyramidTileProvider<T>, ISurface<T>, IDisposable where T : unmanaged, IEquatable<T>
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TiffDataSource");

	public const int DefaultCacheSizeBytes = 134217728;

	private readonly bool _shouldDispose;

	private bool _isDisposed;

	private TiffFile _reader;

	private readonly IArrayCacheManager<PyramidTileIndex, T> _cacheManager;

	public TiffFile Reader => _reader;

	public ITileProvider<T>[] PyramidLevel { get; protected set; }

	public ITileProvider<T> BasePyramidLevel => PyramidLevel.FirstOrDefault();

	public IArrayCacheManager<PyramidTileIndex, T> CacheManager => _cacheManager;

	public Extent ValidExtent => PyramidLevel.FirstOrDefault()?.ValidExtent ?? Extent.NullExtent;

	public T? NoDataValue => PyramidLevel.FirstOrDefault()?.NoDataValue;

	private TiffDataSource(TiffFile reader, bool shouldDispose, IArrayCacheManager<PyramidTileIndex, T> cache)
	{
		_reader = reader;
		_shouldDispose = shouldDispose;
		if (cache == null)
		{
			_cacheManager = new ArrayCacheManager<PyramidTileIndex, T>
			{
				MaxCacheSizeBytes = 134217728
			};
		}
		else
		{
			_cacheManager = cache;
		}
		Initialize();
	}

	public TiffDataSource(string filename, IArrayCacheManager<PyramidTileIndex, T> cache = null)
	{
		_reader = new TiffFile(filename);
		_shouldDispose = true;
		if (cache == null)
		{
			_cacheManager = new ArrayCacheManager<PyramidTileIndex, T>
			{
				MaxCacheSizeBytes = 134217728
			};
		}
		else
		{
			_cacheManager = cache;
		}
		Initialize();
	}

	public static TiffDataSource<T> Give(TiffFile reader, IArrayCacheManager<PyramidTileIndex, T> cache = null)
	{
		return new TiffDataSource<T>(reader, shouldDispose: true, cache);
	}

	public TiffDataSource(TiffFile reader, IArrayCacheManager<PyramidTileIndex, T> cache = null)
	{
		_reader = reader;
		_shouldDispose = false;
		if (cache == null)
		{
			_cacheManager = new ArrayCacheManager<PyramidTileIndex, T>();
		}
		else
		{
			_cacheManager = cache;
		}
		Initialize();
	}

	public TiffDataSource(Pool<Stream> fileStreams, IArrayCacheManager<PyramidTileIndex, T> cache = null)
	{
		_reader = new TiffFile(fileStreams);
		_shouldDispose = true;
		if (cache == null)
		{
			_cacheManager = new ArrayCacheManager<PyramidTileIndex, T>();
		}
		else
		{
			_cacheManager = cache;
		}
		Initialize();
	}

	public static TiffDataSource<T> TryLoad(string filename, IArrayCacheManager<PyramidTileIndex, T> useCache = null)
	{
		TiffFile.TryLoad(filename, out var file, out var _, out var canReadAllDirectories);
		if (!canReadAllDirectories)
		{
			file?.Dispose();
			return null;
		}
		foreach (TiffDirectory directory in file.Directories)
		{
			directory.TryEnableNoDataShortCircuit<T>();
		}
		return Give(file, useCache);
	}

	public static List<TiffDataSource<T>> TryLoad(IEnumerable<string> filenames, bool useSharedCache)
	{
		List<string> filenamesList = filenames.ToList();
		List<TiffDataSource<T>> tiffDataSources = new List<TiffDataSource<T>>(filenamesList.Count);
		if (useSharedCache)
		{
			ArrayCacheManager<FilePyramidTileIndex, T> arrayCacheManager = new ArrayCacheManager<FilePyramidTileIndex, T>
			{
				MaxCacheSizeBytes = 134217728
			};
			int i;
			for (i = 0; i < filenamesList.Count; i++)
			{
				string filename = filenamesList[i];
				IArrayCacheManager<PyramidTileIndex, T> view = arrayCacheManager.GetView((PyramidTileIndex newKey) => new FilePyramidTileIndex(i, newKey));
				tiffDataSources.Add(TryLoad(filename, view));
			}
		}
		else
		{
			for (int intervalIndex = 0; intervalIndex < filenamesList.Count; intervalIndex++)
			{
				string filename2 = filenamesList[intervalIndex];
				tiffDataSources.Add(TryLoad(filename2));
			}
		}
		return tiffDataSources;
	}

	public static void Invoke(string filename, Action<TiffDataSource<T>> action)
	{
		using TiffDataSource<T> filename2 = new TiffDataSource<T>(filename);
		action(filename2);
	}

	private void Initialize()
	{
		_cacheManager.LogIdentifier = _reader.Filename;
		PyramidLevel = new ITileProvider<T>[_reader.Directories.Count];
		for (int i = 0; i < PyramidLevel.Length; i++)
		{
			int iTmp = i;
			Func<int, PyramidTileIndex> keyTransformer = (int tIdx) => new PyramidTileIndex(iTmp, tIdx);
			PyramidLevel[i] = new TiffTileProvider<T>(_reader, i, _cacheManager.GetView(keyTransformer));
		}
	}

	public ITileProvider<T> GetAppropriatePyramidLevel(double targetCellSize)
	{
		for (int i = 0; i < PyramidLevel.Length; i++)
		{
			if (PyramidLevel[i].RasterDefinition.CellSize > targetCellSize)
			{
				if (i == 0)
				{
					return PyramidLevel[0];
				}
				return PyramidLevel[i - 1];
			}
		}
		return PyramidLevel.LastOrDefault();
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		if (disposing)
		{
			if (_shouldDispose)
			{
				_reader?.Dispose();
			}
			_reader = null;
		}
		_isDisposed = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public static string DebugDisplay(string filename)
	{
		int pixelCount = 1048576;
		int binCount = 16;
		int pixelCount2 = 4096;
		if (!File.Exists(filename))
		{
			return "File does not exist.";
		}
		TiffDataSource<T> tiffDataSource = TryLoad(filename);
		if (tiffDataSource == null)
		{
			return "Could not load tiff file: " + filename.InDoubleQuotes();
		}
		long cellCountLongSum = 0L;
		for (int i = 0; i < tiffDataSource.PyramidLevel.Length; i++)
		{
			cellCountLongSum += tiffDataSource.PyramidLevel[i].RasterDefinition.CellCountLong * 4;
		}
		ITileProvider<T> tileProvider = tiffDataSource.PyramidLevel[0];
		FileInfo fileInfo = new FileInfo(filename);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Size: " + FileHelpers.PrettyByteCount(fileInfo.Length));
		double lengthOverCellCountLongSum = (double)fileInfo.Length / (double)cellCountLongSum;
		stringBuilder.AppendLine("Compression Ratio: ~" + Math.Round(lengthOverCellCountLongSum, 3).ToString("P"));
		stringBuilder.AppendLine(tiffDataSource.PyramidLevel.Length + " Pyramid Levels");
		stringBuilder.AppendLine(tileProvider.TiledRasterDefinition.Rows + " Tiles Tall, " + tileProvider.TiledRasterDefinition.Cols + " Tiles Wide");
		stringBuilder.AppendLine(tileProvider.RasterDefinition.Rows + " Rows x " + tileProvider.RasterDefinition.Cols + " Cols = " + tileProvider.RasterDefinition.CellCountLong.ToString("N0"));
		stringBuilder.AppendLine();
		IRasterPyramid<T> rasterPyramid = tiffDataSource.AsRasterizer();
		if (rasterPyramid is IResample<float> resampler)
		{
			stringBuilder.AppendLine("Histogram:");
			Histogram histogram = resampler.DebugComputeHistogram(pixelCount, binCount);
			stringBuilder.AppendLine(histogram.DebugDisplay(includeBinCenterText: true, 24));
			stringBuilder.AppendLine();
		}
		stringBuilder.AppendLine(rasterPyramid.DebugDisplayNoData(pixelCount2));
		return stringBuilder.ToString();
	}
}
