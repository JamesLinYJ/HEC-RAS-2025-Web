using System;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using H5Assist;
using Utility.Memory;

namespace Geospatial.IO.MetImport;

public class GriddedDssRasterSelection : IGriddedDataSelection, IDisposable
{
	private ITimeSeriesRaster<float> _rasterizer;

	private Projection _projection;

	private PrefetchArrayCacheManager<BlockKey, float> _blockCache;

	private IDisposable _cacheChain;

	private bool _isDisposed;

	public ISurface<float> Surface => _rasterizer;

	public Projection Projection => _projection;

	public static GriddedDssRasterSelection Create(DssTimeSeriesRasterDataSource dssRaster)
	{
		GriddedDssRasterSelection griddedDssRasterSelection = new GriddedDssRasterSelection();
		dssRaster.RasterDefinition.GetBlockDimensions(dssRaster.BlockSize, out var blocksWide, out var blocksTall);
		long blocksPerProfile = (long)blocksWide * (long)blocksTall;
		int profileCount = dssRaster.ProfileCount;
		griddedDssRasterSelection._blockCache = new PrefetchArrayCacheManager<BlockKey, float>(new ArrayCacheManager<BlockKey, float>(), dssRaster.BlockSize.PixelCount, (BlockKey key) => BlockPrefetchWindow.GetWindow(key, profileCount, blocksPerProfile), (BlockKey key, Memory<float> buf) => dssRaster.ReadBlock(key.ProfileIndex, key.BlockIndex, buf))
		{
			MaxCacheSizeBytes = 268435456
		};
		ITimeSeriesRasterProvider<float> timeSeriesRasterProvider = dssRaster.WithBlockCache(griddedDssRasterSelection._blockCache);
		griddedDssRasterSelection._cacheChain = timeSeriesRasterProvider as IDisposable;
		griddedDssRasterSelection._rasterizer = timeSeriesRasterProvider.AsTimeSeriesRasterizer();
		griddedDssRasterSelection._projection = dssRaster.Projection;
		return griddedDssRasterSelection;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				_blockCache?.Dispose();
				_blockCache = null;
				_cacheChain?.Dispose();
				_cacheChain = null;
			}
			_isDisposed = true;
		}
	}

	public GriddedWriteInfo WriteToH5(H5Writer writer, string dataset)
	{
		TypedBandedRasterWriterConfig<float> typedBandedRasterWriterConfig = TypedBandedRasterWriterConfig<float>.Default(-9999f);
		typedBandedRasterWriterConfig.CreateOverviews = true;
		H5RasterWriter.WriteTimeSeriesRaster(writer, _rasterizer, dataset, typedBandedRasterWriterConfig);
		H5RasterWriter.WriteRasterMetadata(writer, dataset, new RasterMetadata
		{
			Projection = (_projection?.GetWKT() ?? "")
		});
		return new GriddedWriteInfo
		{
			IsVector = false
		};
	}
}
