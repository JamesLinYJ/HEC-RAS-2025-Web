using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Geospatial;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.MetImport;
using Geospatial.IO.Rasters;
using Geospatial.PairedData;
using Geospatial.Rasterizers;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors;
using H5Assist;
using Ras.Engine;
using Ras.Layers.Meteorology;
using Utility;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers.BoundaryConditions;

public abstract class SpatialScalarBoundaryConditionBase : SpatialBoundaryConditionBase, ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps
{
	private float _constantValue;

	private Versioned<ITimeSeriesResampler<float>> _resampler = new Versioned<ITimeSeriesResampler<float>>();

	[Metadata("Constant")]
	public float ConstantValue
	{
		get
		{
			return _constantValue;
		}
		set
		{
			_constantValue = value;
			Interlocked.Increment(ref _versionXy);
		}
	}

	public ITimeSeriesResampler<float> Resampler => _resampler.Get(VersionXY, BuildResampler);

	public bool CanInterpolateTimesteps => Resampler?.CanInterpolateTimesteps ?? false;

	public int ProfileCount => Resampler?.ProfileCount ?? 0;

	public Extent ValidExtent => Resampler?.ValidExtent ?? Extent.NullExtent;

	public float? NoDataValue => Resampler?.NoDataValue;

	public IReadOnlyList<DateTime> Times => Resampler?.Times;

	protected SpatialScalarBoundaryConditionBase(BoundaryCondition bc, string h5GroupName)
		: base(bc, h5GroupName)
	{
	}

	public void SetConstantValue(float constantValue)
	{
		IsEnabled = true;
		SpatialDataType = SpatialDataType.Constant;
		ConstantValue = constantValue;
	}

	protected override void SetDefaultInternal()
	{
		ConstantValue = 0f;
	}

	protected override bool IsDefaultInternal()
	{
		return ConstantValue == 0f;
	}

	public virtual void ImportRaster(ITimeSeriesRaster<float> timed, Projection prj, string dataType, string units, ProgressReporter prog = null)
	{
		if (!BoundaryCondition.CanElevateToWriter())
		{
			throw new NotSupportedException("Could not elevate to Boundary Condition writer.");
		}
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		long sizeInBytes = timed.SizeInBytes();
		bool notIsInMemory = !BoundaryCondition.IsInMemory && sizeInBytes > 104857600;
		string dataset = ImportedRasterDataset;
		H5Writer hw = null;
		try
		{
			if (notIsInMemory)
			{
				hw = new H5Writer(Path.GetTempFileName());
			}
			else
			{
				hw = H5Writer.CreateInMemory(4096);
			}
			H5RasterWriter.WriteTimeSeriesRaster(hw, timed, dataset, TypedBandedRasterWriterConfig<float>.Default(-9999f), prog);
			RasterMetadata metadata = new RasterMetadata
			{
				Projection = (prj?.GetWKT() ?? ""),
				Units = units,
				DataType = dataType
			};
			H5RasterWriter.WriteRasterMetadata(hw, dataset, metadata);
			SetUseImportedRaster();
			BoundaryCondition.UseWriter((H5Writer hwTarget) =>
			{
				if (hwTarget.ExistsAsDataset(dataset))
				{
					hwTarget.Delete(dataset);
				}
				hw.CopyTo(hwTarget, dataset, dataset);
				Save(hwTarget);
			});
		}
		finally
		{
			if (notIsInMemory)
			{
				string filename = hw.Filename;
				hw.Dispose();
				if (File.Exists(filename))
				{
					File.Delete(filename);
				}
			}
			else
			{
				hw.Dispose();
			}
		}
	}

	public OperationResult GetNativeGriddedRaster(out ITimeSeriesRaster<float> nativeData, out ISpatialTransformer projectToNativeTransformer)
	{
		if (SpatialDataType != SpatialDataType.Gridded)
		{
			throw new Exception("Data type must be gridded for GetNativeGriddedRaster");
		}
		if (GridDataLocation == DataLocation.Local)
		{
			OperationResult operationResult = H5MultiBandPyramidTileProvider<float>.TryOpen(BoundaryCondition.GetH5Wrapper(), ImportedRasterDataset, out var result);
			if (!operationResult.Result)
			{
				nativeData = null;
				projectToNativeTransformer = null;
				return operationResult;
			}
			IMultiBandRaster<float> resampler = result.AsRaster();
			DateTime[] times = null;
			RasterMetadata md = default;
			BoundaryCondition.UseReader((H5Reader hr) =>
			{
				times = H5RasterWriter.ReadTimesAttribute(hr, ImportedRasterDataset);
				md = H5RasterWriter.ReadRasterMetadata(hr, ImportedRasterDataset);
			});
			nativeData = resampler.AsTimeSeriesRaster(times);
			if (string.IsNullOrEmpty(md.Projection))
			{
				projectToNativeTransformer = null;
				return OperationResult.Success();
			}
			Projection projection = TryGetProjection();
			if (projection == null)
			{
				projectToNativeTransformer = null;
				return OperationResult.Success();
			}
			WKTProjection destProj = new WKTProjection(md.Projection);
			projectToNativeTransformer = new ReprojectionTransformer(projection, destProj);
			return OperationResult.Success();
		}
		_ = GridDataLocation;
		_ = 1;
		throw new NotImplementedException();
	}

	private ITimeSeriesResampler<float> BuildResampler()
	{
		if (SpatialDataType == SpatialDataType.Constant)
		{
			return new ConstantTimeSeriesResampler<float>(ConstantValue, Extent.Infinite)
			{
				NoDataValue = -9999f
			};
		}
		if (IsResult)
		{
			if (!H5RasterWriter.TryReadTimeSeriesRaster(BoundaryCondition.GetH5Wrapper(), ImportedRasterDataset, out RasterMetadata _, out H5TimeSeriesRasterDataSource<float> result).Result)
			{
				return null;
			}
			return result?.AsTimeSeriesRasterizer();
		}
		if (SpatialDataType == SpatialDataType.Point)
		{
			if (Gauges == null || Gauges.Count == 0)
			{
				return null;
			}
			TimeSeriesGauge[] timeSeriesGauges = new TimeSeriesGauge[Gauges.Count];
			for (int i = 0; i < Gauges.Count; i++)
			{
				MetGaugedData metGaugedData = Gauges[i];
				TimeSeries timeSeries = metGaugedData.Evaluate(BoundaryCondition.StartTime, BoundaryCondition.EndTime);
				timeSeriesGauges[i] = new TimeSeriesGauge(timeSeries, metGaugedData.Station.Location);
			}
			SimpleTimeSeriesResampler resampler = new SimpleTimeSeriesResampler(timeSeriesGauges, new TinInterpolator());
			if (Raster.IsNull())
			{
				return resampler.WithTransformer(null, new ScaleValueTransformer<float>(Ratio));
			}
			return resampler.WithTransformer(null, new ScaleValueTransformer<float>(Ratio)).AsTimeSeriesRaster(Raster);
		}
		if (SpatialDataType == SpatialDataType.Gridded)
		{
			if (GridDataLocation == DataLocation.Local)
			{
				OperationResult operationResult = H5MultiBandPyramidTileProvider<float>.TryOpen(BoundaryCondition.GetH5Wrapper(), ImportedRasterDataset, out var result2);
				if (!operationResult.Result)
				{
					AddMessages(operationResult);
					return null;
				}
				IMultiBandRaster<float> resampler2 = result2.AsRaster();
				DateTime[] times = null;
				RasterMetadata md2 = default;
				BoundaryCondition.UseReader((H5Reader hr) =>
				{
					times = H5RasterWriter.ReadTimesAttribute(hr, ImportedRasterDataset);
					md2 = H5RasterWriter.ReadRasterMetadata(hr, ImportedRasterDataset);
				});
				ITimeSeriesRaster<float> timeSeriesRaster = resampler2.AsTimeSeriesRaster(times);
				if (string.IsNullOrEmpty(md2.Projection))
				{
					AddMessage(MessageCodes.GetDatasetProjectionNotFound(BoundaryCondition.Name, HDFGroupName));
					return timeSeriesRaster;
				}
				Projection projection = BoundaryCondition.Project.Projection;
				if (projection == null)
				{
					return timeSeriesRaster;
				}
				ReprojectionTransformer spatialTransformer = new ReprojectionTransformer(new WKTProjection(md2.Projection), projection);
				return timeSeriesRaster.WithTransformer(spatialTransformer);
			}
			_ = GridDataLocation;
			_ = 1;
			throw new NotImplementedException();
		}
		throw new NotImplementedException();
	}

	public override OperationResult PrepareForCompute()
	{
		if (SpatialDataType != SpatialDataType.Constant && SpatialDataType != SpatialDataType.Point)
		{
			if (SpatialDataType != SpatialDataType.Gridded)
			{
				throw new NotSupportedException();
			}
			if (GridDataLocation != DataLocation.Local)
			{
				if (GridDataLocation == DataLocation.Remote)
				{
					throw new NotImplementedException();
				}
				throw new NotSupportedException();
			}
		}
		return true;
	}

	public override OperationResult InitializeComputeDriver(ComputeControl computeControl, BoundaryConditionCollection bcs)
	{
		if (!IsEnabled)
		{
			return true;
		}
		if (BoundaryCondition.Result.Geometry.FlowAreaLayer.First().Mesh == null)
		{
			throw new Exception("No mesh found");
		}
		return false;
	}

	public override void CopyDataToInternal(H5Writer hw)
	{
		string ds = ImportedRasterDataset;
		BoundaryCondition.UseReader((H5Reader hr) =>
		{
			if (hr.ExistsAsDataset(ds))
			{
				hr.CopyTo(hw, ds, ds);
			}
		});
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<float> outData, ResampleCache cache = null)
	{
		Resampler?.Resample(args, time, outData, cache);
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		return Resampler?.SamplePointTimeSeries(pt);
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<float> outData, ResampleCache cache = null)
	{
		Resampler?.Resample(args, bandIndex, outData, cache);
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		Resampler?.Resample(args, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		Resampler?.SamplePoints(points, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		Resampler?.SamplePoints(points, bandIndex, outData);
	}

	public override void WriteGriddedSelection(IGriddedDataSelection selection)
	{
		BoundaryCondition.UseWriter((H5Writer writer) =>
		{
			selection.WriteToH5(writer, ImportedRasterDataset);
			Save(writer);
			NotifyRasterDataChange();
		});
	}

	public override string ToString()
	{
		if (SpatialDataType == SpatialDataType.Constant)
		{
			return "Constant: " + ConstantValue + " " + (IsEnabled ? "[ENABLED]" : "[DISABLED]");
		}
		if (SpatialDataType == SpatialDataType.Point)
		{
			return "Gauges (" + Gauges.Count + ") " + (IsEnabled ? "[ENABLED]" : "[DISABLED]");
		}
		if (SpatialDataType == SpatialDataType.Gridded)
		{
			return "Gridded " + (IsEnabled ? "[ENABLED]" : "[DISABLED]");
		}
		return "Error";
	}
}
