using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using H5Assist;
using Utility.Logging;
using Utility.Units;

namespace Geospatial.IO.MetImport;

public class GriddedGdalVectorSelection : IGriddedDataSelection, IDisposable
{
	private string _aVrtPath;

	private string _bVrtPath;

	private GDALRaster _aRaster;

	private GdalBandedRasterDataSource<float> _aDataSource;

	private ITimeSeriesRaster<float> _aRasterizer;

	private GDALRaster _bRaster;

	private GdalBandedRasterDataSource<float> _bDataSource;

	private ITimeSeriesRaster<float> _bRasterizer;

	private ITimeSeriesVelocityRasterizer<float> _velocityRasterizer;

	private Projection _projection;

	public ISurface<float> Surface => _velocityRasterizer;

	public Projection Projection => _projection;

	public static OperationResult CreateFromUVFiles(string fileU, string fileV, out GriddedGdalVectorSelection selection)
	{
		GdalRasterSummary fileU2 = GdalRasterSummary.LoadFull(fileU);
		GdalRasterSummary fileV2 = GdalRasterSummary.LoadFull(fileV);
		return CreateFromUVFiles(fileU2, fileV2, out selection);
	}

	public static OperationResult CreateFromUVFiles(GdalRasterSummary fileU, GdalRasterSummary fileV, out GriddedGdalVectorSelection selection)
	{
		List<(GdalRasterSummary, int)> valueTuples = new List<(GdalRasterSummary, int)>();
		for (int i = 0; i < fileU.BandCount; i++)
		{
			valueTuples.Add((fileU, i));
		}
		List<(GdalRasterSummary, int)> valueTuplesList = new List<(GdalRasterSummary, int)>();
		for (int j = 0; j < fileV.BandCount; j++)
		{
			valueTuplesList.Add((fileV, j));
		}
		return CreateFromSelections(valueTuples, valueTuplesList, isMagnitudeDirection: false, out selection);
	}

	public static OperationResult CreateFromSelections(IReadOnlyList<(GdalRasterSummary, int)> aBands, IReadOnlyList<(GdalRasterSummary, int)> bBands, bool isMagnitudeDirection, out GriddedGdalVectorSelection selection)
	{
		selection = new GriddedGdalVectorSelection();
		OperationResult operationResult = VrtHelpers.BuildVRTFromSelected(aBands, out var vrtPath);
		selection._aVrtPath = vrtPath;
		if (!operationResult)
		{
			return operationResult;
		}
		OperationResult operationResult2 = VrtHelpers.BuildVRTFromSelected(bBands, out var vrtPath2);
		selection._bVrtPath = vrtPath2;
		if (!operationResult2)
		{
			return operationResult2;
		}
		selection._aRaster = new GDALRaster(selection._aVrtPath);
		selection._aDataSource = new GdalBandedRasterDataSource<float>(selection._aRaster, aBands.All(((GdalRasterSummary, int) b) => Drivers.IsParallelReadSafe(b.Item1.Driver)));
		selection._aRasterizer = selection._aDataSource.AsBandedRasterizer().AsTimeSeriesRaster(selection._aRaster.TryParseDatetimesPerBand());
		selection._bRaster = new GDALRaster(selection._bVrtPath);
		selection._bDataSource = new GdalBandedRasterDataSource<float>(selection._bRaster, bBands.All(((GdalRasterSummary, int) b) => Drivers.IsParallelReadSafe(b.Item1.Driver)));
		selection._bRasterizer = selection._bDataSource.AsBandedRasterizer().AsTimeSeriesRaster(selection._bRaster.TryParseDatetimesPerBand());
		if (isMagnitudeDirection)
		{
			selection._velocityRasterizer = VelocityRaster.FromMagnitudeDirection(selection._aRasterizer, selection._bRasterizer, DirectionConvention.GoingToward);
		}
		else
		{
			selection._velocityRasterizer = VelocityRaster.FromVxVy(selection._aRasterizer, selection._bRasterizer);
		}
		selection._projection = selection._aRaster.GetProjection();
		return true;
	}

	public void Dispose()
	{
		GC.SuppressFinalize(this);
		_aDataSource?.Dispose();
		_aRaster?.Dispose();
		_bDataSource?.Dispose();
		_bRaster?.Dispose();
		if (File.Exists(_aVrtPath))
		{
			File.Delete(_aVrtPath);
		}
		if (File.Exists(_bVrtPath))
		{
			File.Delete(_bVrtPath);
		}
	}

	~GriddedGdalVectorSelection()
	{
		Dispose();
	}

	public GriddedWriteInfo WriteToH5(H5Writer writer, string dataset)
	{
		TypedBandedRasterWriterConfig<float> typedBandedRasterWriterConfig = TypedBandedRasterWriterConfig<float>.Default(-9999f);
		typedBandedRasterWriterConfig.CreateOverviews = true;
		string dataset2 = dataset + " Vx";
		string dataset3 = dataset + " Vy";
		ITimeSeriesRaster<float> vxRasterizer = _velocityRasterizer.GetVxRasterizer();
		H5RasterWriter.WriteTimeSeriesRaster(writer, vxRasterizer, dataset2, typedBandedRasterWriterConfig);
		H5RasterWriter.WriteRasterMetadata(writer, dataset2, new RasterMetadata
		{
			Projection = (_aRaster.GetProjection()?.GetWKT() ?? "")
		});
		ITimeSeriesRaster<float> vyRasterizer = _velocityRasterizer.GetVyRasterizer();
		H5RasterWriter.WriteTimeSeriesRaster(writer, vyRasterizer, dataset3, typedBandedRasterWriterConfig);
		H5RasterWriter.WriteRasterMetadata(writer, dataset3, new RasterMetadata
		{
			Projection = (_bRaster.GetProjection()?.GetWKT() ?? "")
		});
		return new GriddedWriteInfo
		{
			IsVector = true
		};
	}
}
