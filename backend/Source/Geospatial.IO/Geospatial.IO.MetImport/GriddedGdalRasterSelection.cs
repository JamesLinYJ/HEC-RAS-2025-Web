using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using H5Assist;
using Utility.Logging;

namespace Geospatial.IO.MetImport;

public class GriddedGdalRasterSelection : IGriddedDataSelection, IDisposable
{
	private string _vrtPath;

	private GDALRaster _raster;

	private GdalBandedRasterDataSource<float> _dataSource;

	private ITimeSeriesRaster<float> _rasterizer;

	private Projection _projection;

	public ISurface<float> Surface => _rasterizer;

	public Projection Projection => _projection;

	public bool SupportsPrecipConversion => true;

	internal static OperationResult CreateFromSelections(IReadOnlyList<(GdalRasterSummary, int)> selectedBands, out GriddedGdalRasterSelection selection)
	{
		selection = new GriddedGdalRasterSelection();
		OperationResult operationResult = VrtHelpers.BuildVRTFromSelected(selectedBands, out var vrtPath);
		selection._vrtPath = vrtPath;
		if (!operationResult)
		{
			return operationResult;
		}
		selection._raster = new GDALRaster(selection._vrtPath);
		bool allowParallelReads = selectedBands.All(((GdalRasterSummary, int) b) => Drivers.IsParallelReadSafe(b.Item1.Driver));
		selection._dataSource = new GdalBandedRasterDataSource<float>(selection._raster, allowParallelReads);
		selection._rasterizer = selection._dataSource.AsBandedRasterizer().AsTimeSeriesRaster(selection._raster.TryParseDatetimesPerBand());
		selection._projection = selection._raster.GetProjection();
		return true;
	}

	public void Dispose()
	{
		GC.SuppressFinalize(this);
		_dataSource?.Dispose();
		_raster?.Dispose();
		_raster = null;
		if (File.Exists(_vrtPath))
		{
			File.Delete(_vrtPath);
		}
	}

	~GriddedGdalRasterSelection()
	{
		Dispose();
	}

	public GriddedWriteInfo WriteToH5(H5Writer writer, string dataset)
	{
		TypedBandedRasterWriterConfig<float> typedBandedRasterWriterConfig = TypedBandedRasterWriterConfig<float>.Default(-9999f);
		typedBandedRasterWriterConfig.CreateOverviews = true;
		H5RasterWriter.WriteTimeSeriesRaster(writer, _rasterizer, dataset, typedBandedRasterWriterConfig);
		H5RasterWriter.WriteRasterMetadata(writer, dataset, new RasterMetadata
		{
			Projection = (_raster.GetProjection()?.GetWKT() ?? "")
		});
		return new GriddedWriteInfo
		{
			IsVector = false
		};
	}
}
