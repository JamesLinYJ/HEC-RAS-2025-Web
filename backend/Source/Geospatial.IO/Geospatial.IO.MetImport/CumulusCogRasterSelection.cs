using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Source;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Files;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.IO.MetImport;

public class CumulusCogRasterSelection : IGriddedDataSelection, IDisposable
{
	private static readonly ILogger _logger = LogHelper.GetLogger("CumulusCogRasterSelection");

	private string _vrtPath;

	private readonly List<string> _intermediateVrtPaths = new List<string>();

	private GDALRaster _raster;

	private GdalBandedRasterDataSource<float> _dataSource;

	private ITimeSeriesRaster<float> _sourceRasterizer;

	private ITimeSeriesRaster<float> _rasterizer;

	private PrefetchArrayCacheManager<BlockKey, float> _blockCache;

	private IDisposable _cacheChain;

	private Projection _projection;

	private bool _isDisposed;

	public ISurface<float> Surface => _rasterizer;

	public Projection Projection => _projection;

	public bool SupportsPrecipConversion => true;

	internal static OperationResult CreateFromSources(IReadOnlyList<CumulusCogRaster> sources, Extent clipExtent, Projection targetProjection, out CumulusCogRasterSelection selection)
	{
		selection = new CumulusCogRasterSelection();
		if (sources == null || sources.Count == 0)
		{
			return OperationResult.Fail("No Cumulus grids selected.");
		}
		List<CumulusCogRaster> source = sources.OrderBy((CumulusCogRaster s) => s.Timestep).ToList();
		bool hasTargetProjectionAndIsFiniteAndValid = targetProjection != null && clipExtent.IsFiniteAndValid();
		if (!hasTargetProjectionAndIsFiniteAndValid)
		{
			clipExtent.IsFiniteAndValid();
		}
		else
			_ = 0;
		string tempFileName = Path.GetTempFileName();
		File.Delete(tempFileName);
		selection._vrtPath = tempFileName;
		OperationResult operationResult = GDALRaster.BuildClonedBandVrt(source.Select((CumulusCogRaster o) => o.SourcePath).ToList(), tempFileName);
		if (!operationResult)
		{
			string concatenatedMessages = operationResult.GetConcatenatedMessages(";");
			return OperationResult.Fail("Building a VRT from the Cumulus COGs failed. " + concatenatedMessages);
		}
		try
		{
			selection._raster = new GDALRaster(selection._vrtPath);
			selection._dataSource = new GdalBandedRasterDataSource<float>(selection._raster, allowParallelReads: true)
			{
				ReadFailureMode = ReadFailureMode.FillNoData
			};
			List<DateTime> dateTimes = source.Select((CumulusCogRaster o) => o.Timestep).ToList();
			ITimeSeriesRasterProvider<float> withTimes = selection._dataSource.AsTimeSeriesProvider(dateTimes);
			withTimes.RasterDefinition.GetBlockDimensions(withTimes.BlockSize, out var blocksWide, out var blocksTall);
			long blocksPerProfile = (long)blocksWide * (long)blocksTall;
			int profileCount = withTimes.ProfileCount;
			selection._blockCache = new PrefetchArrayCacheManager<BlockKey, float>(new ArrayCacheManager<BlockKey, float>(), withTimes.BlockSize.PixelCount, (BlockKey key) => BlockPrefetchWindow.GetWindow(key, profileCount, blocksPerProfile), (BlockKey key, Memory<float> buf) => withTimes.ReadBlock(key.ProfileIndex, key.BlockIndex, buf))
			{
				MaxCacheSizeBytes = 268435456
			};
			ITimeSeriesRasterProvider<float> timeSeriesRasterProvider = withTimes.WithBlockCache(selection._blockCache);
			selection._cacheChain = timeSeriesRasterProvider as IDisposable;
			ITimeSeriesRaster<float> timeSeriesRaster = timeSeriesRasterProvider.AsTimeSeriesRasterizer();
			selection._projection = selection._raster.GetProjection();
			if (hasTargetProjectionAndIsFiniteAndValid)
			{
				ReprojectionTransformer orMake = ReprojectionTransformer.GetOrMake(targetProjection, selection._projection);
				clipExtent.Transform(orMake, out var transformedEdgeDivisorPoints);
				Polygon clipPolygon = new Polygon(transformedEdgeDivisorPoints);
				timeSeriesRaster = timeSeriesRaster.AsClippedToPolygon(clipPolygon).AsTimeSeriesRaster(dateTimes);
			}
			selection._sourceRasterizer = timeSeriesRaster;
			if (selection._projection == null)
			{
				throw new Exception("Incoming data has no projection - likely error.");
			}
			selection._rasterizer = timeSeriesRaster;
		}
		catch (Exception ex)
		{
			return OperationResult.Fail("Opening the Cumulus COGs", ex);
		}
		return true;
	}

	public GriddedWriteInfo WriteToH5(H5Writer writer, string dataset)
	{
		TypedBandedRasterWriterConfig<float> typedBandedRasterWriterConfig = TypedBandedRasterWriterConfig<float>.Default(-9999f);
		typedBandedRasterWriterConfig.CreateOverviews = true;
		H5RasterWriter.WriteTimeSeriesRaster(writer, _sourceRasterizer, dataset, typedBandedRasterWriterConfig);
		RasterMetadata metadata = new RasterMetadata
		{
			Projection = (_projection?.GetWKT() ?? "")
		};
		H5RasterWriter.WriteRasterMetadata(writer, dataset, metadata);
		return new GriddedWriteInfo
		{
			IsVector = false
		};
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		if (disposing)
		{
			_blockCache?.Dispose();
			_cacheChain?.Dispose();
			_dataSource?.Dispose();
			_raster?.Dispose();
			if (!string.IsNullOrEmpty(_vrtPath) && File.Exists(_vrtPath))
			{
				FileHelpers.TryDelete(_vrtPath);
			}
			foreach (string intermediateVrtPath in _intermediateVrtPaths)
			{
				if (!string.IsNullOrEmpty(intermediateVrtPath) && File.Exists(intermediateVrtPath))
				{
					FileHelpers.TryDelete(intermediateVrtPath);
				}
			}
		}
		_isDisposed = true;
	}
}
