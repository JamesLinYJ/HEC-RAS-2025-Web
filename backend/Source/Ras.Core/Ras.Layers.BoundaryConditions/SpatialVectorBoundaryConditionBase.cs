using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Geospatial;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.MetImport;
using Geospatial.IO.Rasters;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors;
using H5Assist;
using Ras.Layers.Meteorology;
using Utility;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;
using Utility.Units;

namespace Ras.Layers.BoundaryConditions;

[DebuggerDisplay("{ToString(),nq}")]
public abstract class SpatialVectorBoundaryConditionBase : SpatialBoundaryConditionBase, ITimeSeriesVelocityResampler<float>, ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps
{
	private Versioned<ITimeSeriesVelocityResampler<float>> _versionedResampler = new Versioned<ITimeSeriesVelocityResampler<float>>();

	[Metadata("Vx", Optional = true)]
	public float ConstantVx { get; set; }

	[Metadata("Vy", Optional = true)]
	public float ConstantVy { get; set; }

	public float ConstantMagnitude => (float)Math.Sqrt(ConstantVx * ConstantVx + ConstantVy * ConstantVy);

	public string ImportedRasterDatasetA => ImportedRasterDataset + " Vx";

	public string ImportedRasterDatasetB => ImportedRasterDataset + " Vy";

	public ITimeSeriesVelocityResampler<float> Resampler => _versionedResampler.Get(VersionXY, BuildResampler);

	public Extent ValidExtent => Resampler?.ValidExtent ?? Extent.NullExtent;

	public float? NoDataValue => Resampler?.NoDataValue;

	public IReadOnlyList<DateTime> Times => Resampler?.Times;

	public bool CanInterpolateTimesteps => Resampler?.CanInterpolateTimesteps ?? false;

	public int ProfileCount => Resampler?.ProfileCount ?? 0;

	public event TimestepAddedEvent TimestepAdded
	{
		add
		{
		}
		remove
		{
		}
	}

	public SpatialVectorBoundaryConditionBase(BoundaryCondition bc, string h5GroupName)
		: base(bc, h5GroupName)
	{
	}

	protected override void SetDefaultInternal()
	{
		ConstantVx = 0f;
		ConstantVy = 0f;
	}

	protected override bool IsDefaultInternal()
	{
		if (ConstantVx == 0f && ConstantVy == 0f)
		{
			return SpatialDataType == SpatialDataType.Constant;
		}
		return false;
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

	public OperationResult GetNativeGriddedRaster(out ITimeSeriesVelocityRasterizer<float> nativeData, out ISpatialTransformer projectToNativeTransformer)
	{
		if (SpatialDataType != SpatialDataType.Gridded)
		{
			throw new Exception("Data type must be gridded for GetNativeGriddedRaster");
		}
		if (GridDataLocation == DataLocation.Local)
		{
			OperationResult operationResult = H5MultiBandPyramidTileProvider<float>.TryOpen(BoundaryCondition.GetH5Wrapper(), ImportedRasterDatasetA, out var result);
			if (!operationResult.Result)
			{
				nativeData = null;
				projectToNativeTransformer = null;
				return operationResult;
			}
			operationResult = H5MultiBandPyramidTileProvider<float>.TryOpen(BoundaryCondition.GetH5Wrapper(), ImportedRasterDatasetB, out var result2);
			if (!operationResult.Result)
			{
				nativeData = null;
				projectToNativeTransformer = null;
				return operationResult;
			}
			IMultiBandRaster<float> resampler = result.AsRaster();
			IMultiBandRaster<float> resamplerY = result2.AsRaster();
			DateTime[] times = null;
			RasterMetadata md = default;
			BoundaryCondition.UseReader((H5Reader hr) =>
			{
				times = H5RasterWriter.ReadTimesAttribute(hr, ImportedRasterDatasetA);
				md = H5RasterWriter.ReadRasterMetadata(hr, ImportedRasterDatasetA);
			});
			nativeData = VelocityRaster.FromVxVy(resampler.AsTimeSeriesRaster(times), resamplerY.AsTimeSeriesRaster(times));
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

	public override void CopyDataToInternal(H5Writer hw)
	{
		_ = ImportedRasterDatasetA;
		BoundaryCondition.UseReader((H5Reader hr) =>
		{
			if (hr.ExistsAsDataset(ImportedRasterDatasetA))
			{
				hr.CopyTo(hw, ImportedRasterDatasetA, ImportedRasterDatasetA);
			}
			if (hr.ExistsAsDataset(ImportedRasterDatasetB))
			{
				hr.CopyTo(hw, ImportedRasterDatasetB, ImportedRasterDatasetB);
			}
		});
	}

	public virtual void ImportRasters(ITimeSeriesRaster<float> vx, ITimeSeriesRaster<float> vy, Projection prj, string dataType, string units, ProgressReporter prog = null)
	{
		if (!BoundaryCondition.CanElevateToWriter())
		{
			throw new NotSupportedException("Could not elevate to Boundary Condition writer.");
		}
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		long sizeInBytesVxTerm = vx.SizeInBytes() + vy.SizeInBytes();
		bool notIsInMemory = !BoundaryCondition.IsInMemory && sizeInBytesVxTerm > 104857600;
		string datasetA = ImportedRasterDatasetA;
		string datasetB = ImportedRasterDatasetB;
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
			RasterMetadata metadata = new RasterMetadata
			{
				Projection = (prj?.GetWKT() ?? ""),
				Units = units,
				DataType = dataType
			};
			H5RasterWriter.WriteTimeSeriesRaster(hw, vx, datasetA, TypedBandedRasterWriterConfig<float>.Default(-9999f), prog);
			H5RasterWriter.WriteRasterMetadata(hw, datasetA, metadata);
			H5RasterWriter.WriteTimeSeriesRaster(hw, vy, datasetB, TypedBandedRasterWriterConfig<float>.Default(-9999f), prog);
			H5RasterWriter.WriteRasterMetadata(hw, datasetB, metadata);
			SetUseImportedRaster();
			BoundaryCondition.UseWriter((H5Writer hwTarget) =>
			{
				if (hwTarget.ExistsAsDataset(datasetA))
				{
					hwTarget.Delete(datasetA);
				}
				if (hwTarget.ExistsAsDataset(datasetB))
				{
					hwTarget.Delete(datasetB);
				}
				hw.CopyTo(hwTarget, datasetA, datasetA);
				hw.CopyTo(hwTarget, datasetB, datasetB);
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

	public void SetConstantVxVy(float vx, float vy)
	{
		IsEnabled = true;
		SpatialDataType = SpatialDataType.Constant;
		ConstantVx = vx;
		ConstantVy = vy;
	}

	public void SetConstantMagDir(float magnitude, float direction)
	{
		IsEnabled = true;
		SpatialDataType = SpatialDataType.Constant;
		VelocityMath.ConvertToVxVy((double)direction, (double)magnitude, DirectionConvention.ComingFrom, out double vx, out double vy);
		ConstantVx = (float)vx;
		ConstantVy = (float)vy;
	}

	public void AddGaugedData(MetGaugedData gauge)
	{
		_gauges.Add(gauge);
	}

	public void AddConstantGauge(MeteorologicStation station, float vx, float vy)
	{
		MetGaugedData metGaugedData = new MetGaugedData();
		metGaugedData.Station = station;
		metGaugedData.Constant = vx;
		metGaugedData.Constant2 = vy;
		AddGaugedData(metGaugedData);
	}

	public void AddTableGauge(MeteorologicStation station)
	{
	}

	public ITimeSeriesVelocityResampler<float> BuildResampler()
	{
		if (!IsEnabled)
		{
			return null;
		}
		if (SpatialDataType == SpatialDataType.Constant)
		{
			return VelocityRaster.FromConstantVxVy(ConstantVx, ConstantVy);
		}
		if (SpatialDataType == SpatialDataType.Point)
		{
			throw new NotImplementedException();
		}
		if (SpatialDataType == SpatialDataType.Gridded)
		{
			OperationResult operationResult = H5MultiBandPyramidTileProvider<float>.TryOpen(BoundaryCondition.GetH5Wrapper(), ImportedRasterDatasetA, out var result);
			if (!operationResult.Result)
			{
				AddMessages(operationResult);
				return null;
			}
			if (!H5MultiBandPyramidTileProvider<float>.TryOpen(BoundaryCondition.GetH5Wrapper(), ImportedRasterDatasetB, out var result2).Result)
			{
				AddMessages(operationResult);
				return null;
			}
			DateTime[] timesA = null;
			DateTime[] timesB = null;
			RasterMetadata metaA = default;
			RasterMetadata metaB = default;
			BoundaryCondition.UseReader((H5Reader hr) =>
			{
				timesA = H5RasterWriter.ReadTimesAttribute(hr, ImportedRasterDatasetA);
				timesB = H5RasterWriter.ReadTimesAttribute(hr, ImportedRasterDatasetB);
				metaA = H5RasterWriter.ReadRasterMetadata(hr, ImportedRasterDatasetA);
				metaB = H5RasterWriter.ReadRasterMetadata(hr, ImportedRasterDatasetB);
			});
			if (metaA.Projection != metaB.Projection)
			{
				throw new Exception("Non-matching projections on BC A and B data");
			}
			IMultiBandRaster<float> resampler = result.AsRaster();
			IMultiBandRaster<float> resampler2 = result2.AsRaster();
			ITimeSeriesRaster<float> vx = resampler.AsTimeSeriesRaster(timesA);
			ITimeSeriesRaster<float> vy = resampler2.AsTimeSeriesRaster(timesB);
			ITimeSeriesVelocityRasterizer<float> timeSeriesVelocityRasterizer = VelocityRaster.FromVxVy(vx, vy);
			if (string.IsNullOrEmpty(metaA.Projection))
			{
				AddMessage(MessageCodes.GetDatasetProjectionNotFound(BoundaryCondition.Name, HDFGroupName));
				return timeSeriesVelocityRasterizer;
			}
			Projection projection = BoundaryCondition.Project.Projection;
			if (projection == null)
			{
				return timeSeriesVelocityRasterizer;
			}
			ReprojectionTransformer transformer = new ReprojectionTransformer(new WKTProjection(metaA.Projection), projection);
			return timeSeriesVelocityRasterizer.WithTransformer(transformer, VelocityTransformOptions.PreserveValues, null, null);
		}
		throw new NotSupportedException("SpatialDataType");
	}

	public void ResampleVxVy(ResampleArgs args, int bandIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outVelMag)
	{
		Resampler?.ResampleVxVy(args, bandIndex, outVx, outVy, outVelMag);
	}

	public void ResampleMagDir(ResampleArgs args, int bandIndex, Memory<float> outMag, Memory<float> outDir, DirectionConvention dirConvention)
	{
		Resampler?.ResampleMagDir(args, bandIndex, outMag, outDir, dirConvention);
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] vxResult, Memory<float>[] vyResult, Memory<float>[] outVelMag)
	{
		Resampler?.SamplePointsVxVy(points, bandStart, bandCount, vxResult, vyResult, outVelMag);
	}

	public override void WriteGriddedSelection(IGriddedDataSelection selection)
	{
		SpatialDataType = SpatialDataType.Gridded;
		BoundaryCondition.UseWriter((H5Writer writer) =>
		{
			selection.WriteToH5(writer, ImportedRasterDataset);
			Save(writer);
			NotifyRasterDataChange();
		});
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outMagnitude)
	{
		throw new NotImplementedException();
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<float> outData, ResampleCache cache = null)
	{
		throw new NotImplementedException();
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		throw new NotImplementedException();
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<float> outData, ResampleCache cache = null)
	{
		throw new NotImplementedException();
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		throw new NotImplementedException();
	}
}
