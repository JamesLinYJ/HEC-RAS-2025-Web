using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Hec.Dss;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Extensions;
using Utility.Logging;

namespace Geospatial.IO;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class DssTimeSeriesRasterDataSource : IDisposable, ITimeSeriesRasterProvider<float>, IMultiBandRasterProvider<float>, IBlockedRasterDefinition, IRasterDefinition, ISurface<float>, IDiscreteTimesteps
{
	private static readonly ILogger _logger = LogHelper.GetLogger("DssTimeSeriesRasterDataSource");

	private readonly string _filename;

	private readonly string _pathname;

	private string _srsDefinition;

	private readonly DssReader _dssr;

	private readonly NativeResourceMode _resourceMode;

	private readonly CancellationToken _cancellationToken;

	private readonly Action<int>? _frameRead;

	private readonly Lock _readerLock = new Lock();

	private bool _isDisposed;

	private readonly List<string> _paths = new List<string>();

	private readonly List<DateTime> _times = new List<DateTime>();

	private IReadOnlyList<string> _ro;

	public string Filename => _filename;

	public string RootPathname => _pathname;

	public string SRSDefinition
	{
		get
		{
			return _srsDefinition;
		}
		private set
		{
			_srsDefinition = value;
			try
			{
				if (_srsDefinition.RoughlyStartsWith("+proj"))
				{
					Projection = new Proj4Projection(_srsDefinition);
				}
				else
				{
					Projection = new WKTProjection(_srsDefinition);
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Could not build a projection for DSS grid \"{Pathname}\" in {Filename} from SRS definition \"{SRS}\". The grid will be treated as unprojected.", _pathname, _filename, _srsDefinition ?? "(none)");
				Projection = null;
			}
		}
	}

	public Projection Projection { get; private set; }

	public IReadOnlyList<string> Pathnames
	{
		get
		{
			if (_ro == null)
			{
				_ro = _paths.AsReadOnly();
			}
			return _ro;
		}
	}

	public bool CanReadBlocksInParallel => false;

	public BlockSize BlockSize => RasterDefinition.FullBlockSize();

	public int ProfileCount { get; private set; }

	public BlockSize VerticallyChunkedBlockSize => BlockSize.NullBlockSize;

	public RasterDefinition RasterDefinition { get; private set; }

	public Extent ValidExtent
	{
		get
		{
			if (!RasterDefinition.IsNull())
			{
				return RasterDefinition.Extent;
			}
			return Extent.NullExtent;
		}
	}

	public float? NoDataValue => -9999f;

	public IReadOnlyList<DateTime> Times { get; private set; }

	public DssTimeSeriesRasterDataSource(DssReader dssr, string pathname)
	{
		_dssr = dssr;
		_filename = dssr.Filename;
		_pathname = CleanOutTimeParts(pathname);
		_resourceMode = NativeResourceMode.OnLoan;
		_cancellationToken = default;
		Init();
	}

	public DssTimeSeriesRasterDataSource(string filename, string pathname, bool keepOpen = true,
		CancellationToken cancellationToken = default, Action<int>? frameRead = null)
	{
		_filename = filename;
		_pathname = CleanOutTimeParts(pathname);
		_cancellationToken = cancellationToken;
		_frameRead = frameRead;
		if (keepOpen)
		{
			_dssr = new DssReader(filename);
			_resourceMode = NativeResourceMode.KeepOpen;
		}
		else
		{
			_resourceMode = NativeResourceMode.OpenOnDemand;
		}
		Init();
	}

	private void Init()
	{
		UseReader((DssReader dssr) =>
		{
			DssPath dssPath = new DssPath(_pathname);
			// DSS catalogs condense time-series D-part paths in Paths. A gridded
			// raster needs the exact record pathname for every valid time so the
			// frame reader can retrieve each grid independently.
			List<DssPath> filterByPart = PathAssist.FilterByPart(dssr.GetCatalog().UnCondensedPaths, dssPath.Apart, dssPath.Bpart, dssPath.Cpart, dssPath.Dpart, dssPath.Epart, dssPath.Fpart);
			Dictionary<DateTime, string> stringByDateTime = new Dictionary<DateTime, string>();
			for (int i = 0; i < filterByPart.Count; i++)
			{
				DssPath dssPath2 = filterByPart[i];
				string fullPath = dssPath2.FullPath;
				if (dssPath2.TryParseEPartAsDateTime(out var date))
				{
					stringByDateTime.Add(date, fullPath);
				}
				else if (dssPath2.TryParseDPartAsDateTime(out date))
				{
					stringByDateTime.Add(date, fullPath);
				}
			}
			IOrderedEnumerable<KeyValuePair<DateTime, string>> source = stringByDateTime.OrderBy((KeyValuePair<DateTime, string> tup) => tup.Key);
			_paths.AddRange(source.Select((KeyValuePair<DateTime, string> tup) => tup.Value));
			_times.AddRange(source.Select((KeyValuePair<DateTime, string> tup) => tup.Key));
			ProfileCount = _paths.Count;
			Grid grid = dssr.GetGrid(_paths[0], retrieveData: false);
			RasterDefinition = GetNativeRaster(grid);
			SRSDefinition = grid.SRSDefinition;
			Times = _times.AsReadOnly();
		});
	}

	public void UseReader(Action<DssReader> readAction)
	{
		using (_readerLock.EnterScope())
		{
			if (_resourceMode == NativeResourceMode.OpenOnDemand)
			{
				using (DssReader filename = new DssReader(_filename))
				{
					readAction(filename);
					return;
				}
			}
			readAction(_dssr);
		}
	}

	public Grid GetGrid(int profileIndex)
	{
		Grid grd = null;
		UseReader((DssReader dssr) =>
		{
			grd = dssr.GetGrid(_paths[profileIndex], retrieveData: false);
		});
		return grd;
	}

	public bool ReadBlock(int profileIndex, long blockIndex, Memory<float> block)
	{
		_cancellationToken.ThrowIfCancellationRequested();
		if (blockIndex != 0L)
		{
			throw new ArgumentOutOfRangeException();
		}
		Grid grid = null;
		UseReader((DssReader dssr) =>
		{
			grid = dssr.GetGrid(_paths[profileIndex], retrieveData: true);
		});
		_cancellationToken.ThrowIfCancellationRequested();
		CorrectGridData(grid, block);
		_frameRead?.Invoke(profileIndex);
		return true;
	}

	public static void CorrectGridData(Grid grid, Memory<float> values)
	{
		float[] data = grid.Data;
		if (data.Length != values.Length)
		{
			throw new ArgumentException();
		}
		Span<float> floatSpan = values.Span;
		for (int i = 0; i < grid.NumberOfCellsY; i++)
		{
			int numberOfCellsYGridTerm = grid.NumberOfCellsY - i - 1;
			for (int j = 0; j < grid.NumberOfCellsX; j++)
			{
				int numberOfCellsXGridTerm = numberOfCellsYGridTerm * grid.NumberOfCellsX + j;
				int index = i * grid.NumberOfCellsX + j;
				float computed = data[numberOfCellsXGridTerm];
				if (computed == grid.UndefinedValue || computed == grid.NullValue || computed < grid.MinDataValue || computed > grid.MaxDataValue)
				{
					floatSpan[index] = -9999f;
				}
				else
				{
					floatSpan[index] = computed;
				}
			}
		}
	}

	public static string CleanOutTimeParts(string dssPath)
	{
		try
		{
			return new DssPath(dssPath)
			{
				Dpart = "",
				Epart = ""
			}.FullPath;
		}
		catch
		{
			return dssPath;
		}
	}

	public static RasterDefinition GetNativeRaster(Grid grid)
	{
		int numberOfCellsX = grid.NumberOfCellsX;
		int numberOfCellsY = grid.NumberOfCellsY;
		return new RasterDefinition(GetNativeExtent(grid), numberOfCellsY, numberOfCellsX);
	}

	public static Extent GetNativeExtent(Grid grid)
	{
		float cellSize = grid.CellSize;
		int numberOfCellsX = grid.NumberOfCellsX;
		int numberOfCellsY = grid.NumberOfCellsY;
		float lowerLeftCellXTimesCellSize = (float)grid.LowerLeftCellX * cellSize;
		float lowerLeftCellXGridTerm = (float)(grid.LowerLeftCellX + numberOfCellsX) * cellSize;
		float lowerLeftCellYTimesCellSize = (float)grid.LowerLeftCellY * cellSize;
		float lowerLeftCellYGridTerm = (float)(grid.LowerLeftCellY + numberOfCellsY) * cellSize;
		return new Extent(lowerLeftCellXTimesCellSize, lowerLeftCellXGridTerm, lowerLeftCellYTimesCellSize, lowerLeftCellYGridTerm);
	}

	private string GetDebuggerDisplay()
	{
		return _filename + " - " + _pathname + " (" + ProfileCount + " Profiles)";
	}

	public bool ReadVerticalBlock(int verticalBlockIndex, Memory<float> block)
	{
		throw new NotImplementedException();
	}

	public void EvictCache()
	{
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing && _resourceMode == NativeResourceMode.KeepOpen)
			{
				_dssr?.Dispose();
			}
			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
