using System;
using System.Linq;
using Geospatial;
using Geospatial.GDALAssist;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors;
using H5Assist;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Ras.Engine;
using Utility.Logging;
using Utility.Progress;
using Utility.Units;

namespace Ras.Layers.BoundaryConditions;

public class PrecipitationLayer : SpatialScalarBoundaryConditionBase
{
	public const string DefaultHDFGroupName = "/Boundary Conditions/Meteorology/Precipitation";

	private static readonly ILogger _logger = LogHelper.GetLogger("PrecipitationLayer");

	public override string LayerTypeName => "Precipitation";

	public PrecipitationLayer(BoundaryCondition bc)
		: base(bc, "/Boundary Conditions/Meteorology/Precipitation")
	{
	}

	public override bool IsReallyEnabledForCompute()
	{
		if (!IsEnabled)
		{
			return false;
		}
		if (SpatialDataType == SpatialDataType.Constant && ConstantValue == 0f)
		{
			return false;
		}
		if (Ratio == 0f)
		{
			return false;
		}
		return true;
	}

	public override OperationResult InitializeComputeDriver(ComputeControl computeControl, BoundaryConditionCollection bcs)
	{
		if (!IsReallyEnabledForCompute())
		{
			return true;
		}
		Result result = BoundaryCondition.Result;
		Mesh mesh = result.Geometry.FlowAreaLayer.First().Mesh;
		if (mesh == null)
		{
			throw new Exception("No mesh found");
		}
		if (result == null)
		{
			throw new Exception("Cannot initialize solver without a Plan");
		}
		Plan plan = result.Plan;
		float convertTo = (float)Time.Seconds.ConvertTo(Time.Hours, 1.0);
		if (computeControl.Units == UnitSystem.SI)
		{
			convertTo *= 0.001f;
		}
		else
		{
			if (computeControl.Units != UnitSystem.USCustomary)
			{
				throw new Exception("Unit system unknown while setting precipitation");
			}
			convertTo *= 1f / 12f;
		}
		if (SpatialDataType == SpatialDataType.Constant)
		{
			float[] constBuf = new float[1] { ConstantValue * Ratio };
			bcs.SetPrecipitation(new DateTime[2] { BoundaryCondition.StartTime, BoundaryCondition.EndTime }, plan.StartTime, (int i) => constBuf, convertTo, JaggedWeights<float>.ConstantField(mesh.CellCount));
			return true;
		}
		if (SpatialDataType == SpatialDataType.Point)
		{
			_ = Resampler;
			return false;
		}
		if (SpatialDataType == SpatialDataType.Gridded)
		{
			OperationResult nativeGriddedRaster = GetNativeGriddedRaster(out var nativeData, out var projectToNativeTransformer);
			if (!nativeGriddedRaster.Result)
			{
				return nativeGriddedRaster;
			}
			Extent ext = mesh.Extent.RatioAroundCenter(1.05);
			Point[] cellCenters = mesh.CellCenters;
			if (projectToNativeTransformer != null)
			{
				ext = ext.Transform(projectToNativeTransformer);
				cellCenters = projectToNativeTransformer.Transform(cellCenters);
			}
			IMultiBandRaster<float> clipped = nativeData.AsClipped(nativeData.RasterDefinition.GetPixelRange(ext));
			if (clipped.RasterDefinition.IsNull())
			{
				return OperationResult.Fail("Precipitation layer does not cover the mesh.");
			}
			JaggedWeights<float> nearestNeighborWeights = clipped.RasterDefinition.GetNearestNeighborWeights<float>(cellCenters);
			bcs.SetPrecipitation(nativeData.Times.ToArray(), plan.StartTime, (int i) => clipped.GetBand(i), convertTo, nearestNeighborWeights);
			return true;
		}
		throw new NotSupportedException();
	}

	public override void ImportRaster(ITimeSeriesRaster<float> timed, Projection prj, string dataType, string units, ProgressReporter prog = null)
	{
		Precipitation precipitation = Precipitation.TryParse(units);
		if (precipitation == null)
		{
			throw new Exception($"Error importing precipitation data - unrecognized type/units: {dataType} / {units}. Expected e.g. {"per-cum"} / {PeriodCumulativePrecipitation.Millimeters.Name}.");
		}
		Length convertToVerticalUnits = null;
		if (BoundaryCondition.Project == null)
		{
			convertToVerticalUnits = precipitation.VerticalUnits;
		}
		else
		{
			switch (BoundaryCondition.Project.Units)
			{
			case UnitSystem.USCustomary:
				convertToVerticalUnits = Length.Inches;
				break;
			case UnitSystem.SI:
				convertToVerticalUnits = Length.Millimeters;
				break;
			}
		}
		ImportRaster(timed, prj, precipitation, convertToVerticalUnits, prog);
	}

	public void ImportRaster(ITimeSeriesRaster<float> timed, Projection prj, Precipitation precipUnits, Length convertToVerticalUnits, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		IAccumulatedRasterizer<float> accumulatedRasterizer = null;
		if (precipUnits is PeriodCumulativePrecipitation { VerticalUnits: var verticalUnits })
		{
			if (convertToVerticalUnits != null && verticalUnits != convertToVerticalUnits)
			{
				timed = timed.WithTransformer_Raster(null, new ScaleValueTransformer<float>((float)verticalUnits.ConvertTo(convertToVerticalUnits, 1.0)));
			}
			accumulatedRasterizer = timed.AsAccumulatedRasterizer(TimeSeriesDataType.Delta, Time.Hours);
		}
		else
		{
			if (!(precipUnits is PeriodAveragePrecipitation { VerticalUnits: var verticalUnits2 } periodAveragePrecipitation))
			{
				prog.ReportMessage("Could not identify data type '{DataTypeName}' for precipitation. Data will be imported, but check results carefully.");
				base.ImportRaster(timed, prj, precipUnits.DataTypeName, convertToVerticalUnits.Name, prog);
				return;
			}
			double convertTo = Time.Hours.ConvertTo(periodAveragePrecipitation.TimeUnits, 1.0);
			if (convertToVerticalUnits != null && verticalUnits2 != convertToVerticalUnits)
			{
				convertTo *= verticalUnits2.ConvertTo(convertToVerticalUnits, 1.0);
			}
			if (convertTo != 1.0)
			{
				timed = timed.WithTransformer_Raster(null, new ScaleValueTransformer<float>((float)convertTo));
			}
			accumulatedRasterizer = timed.AsAccumulatedRasterizer(TimeSeriesDataType.Rate, Time.Hours);
		}
		PeriodAveragePrecipitation periodAveragePrecipitation2 = ((convertToVerticalUnits == Length.Inches) ? PeriodAveragePrecipitation.InchesPerHour : PeriodAveragePrecipitation.MillimeterPerHour);
		base.ImportRaster(accumulatedRasterizer.AsRateResampler(), prj, "per-avg", periodAveragePrecipitation2.Name, prog);
		NotifyRasterDataChange();
	}

	public void DebugTransformImportedRasterData(Func<float, float> transform)
	{
		string dataset = ImportedRasterDataset;
		BoundaryCondition.UseWriter((H5Writer h5w) =>
		{
			if (!h5w.ExistsAsDataset(dataset))
			{
				throw new Exception("Expected import dataset not found: " + dataset);
			}
			using H5DatasetID dataset2 = h5w.OpenDataset(dataset);
			long[] datasetDimensions = h5w.GetDatasetDimensions(dataset);
			if (datasetDimensions.Length != 2)
			{
				throw new Exception("Expected dataset has incorrect dimensions: " + dataset + " {" + datasetDimensions.Length + "}");
			}
			int datasetDimension = (int)datasetDimensions[0];
			int datasetDimension2 = (int)datasetDimensions[1];
			long[] chunkSize = h5w.GetChunkSize(dataset);
			_ = chunkSize[0];
			_ = chunkSize[1];
			Memory<float> floatSpan = new float[datasetDimension2];
			Span<float> floatSpan2 = floatSpan.Span;
			for (int i = 0; i < datasetDimension; i++)
			{
				h5w.ReadRow(dataset2, i, floatSpan);
				for (int j = 0; j < datasetDimension2; j++)
				{
					floatSpan2[j] = transform(floatSpan2[j]);
				}
				h5w.WriteRow(dataset2, i, floatSpan);
			}
		});
		NotifyRasterDataChange();
	}
}
