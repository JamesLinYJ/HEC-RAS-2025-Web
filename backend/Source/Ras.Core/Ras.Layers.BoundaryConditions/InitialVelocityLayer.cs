using System;
using System.IO;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Ras.Engine;
using Ras.Layers.Meteorology;
using Ras.Mapping;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers.BoundaryConditions;

public class InitialVelocityLayer : SpatialVectorBoundaryConditionBase, IInitialConditionLayer
{
	public const string DefaultHDFGroupName = "/Boundary Conditions/Initial Conditions/Velocity";

	public const string VelocityDataTypeName = "Velocity";

	private const int DefaultIngestCellCount = 4194304;

	private static readonly ILogger _logger = LogHelper.GetLogger("InitialVelocityLayer");

	public override string LayerTypeName => "Initial Velocity";

	public override bool CanRatioData => false;

	[HashContributor]
	[Metadata("Source Timestep", Optional = true)]
	public string SourceTimestep { get; set; }

	[HashContributor]
	[Metadata("Ingest Cell Count", Optional = true)]
	public int IngestCellCount { get; set; }

	private int EffectiveIngestCellCount
	{
		get
		{
			if (IngestCellCount <= 0)
			{
				return 4194304;
			}
			return IngestCellCount;
		}
	}

	public IResample<float> SurfaceVx => TryGetComponent(xComponent: true);

	public IResample<float> SurfaceVy => TryGetComponent(xComponent: false);

	public InitialVelocityLayer(BoundaryCondition bc)
		: base(bc, "/Boundary Conditions/Initial Conditions/Velocity")
	{
	}

	public void SetExternalSource(string connectionName, string path, string timestep)
	{
		IsEnabled = true;
		SpatialDataType = SpatialDataType.Gridded;
		GridDataLocation = DataLocation.Remote;
		ExternalGriddedConnection = connectionName;
		ExternalGriddedPath = path;
		SourceTimestep = timestep;
		NotifyRasterDataChange();
	}

	public OperationResult IngestExternalSource(ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (!IsEnabled)
		{
			return true;
		}
		if (SpatialDataType != SpatialDataType.Gridded || GridDataLocation != DataLocation.Remote)
		{
			return true;
		}
		pr.ReportMessage("Ingesting Initial Velocity from {Name}...", ExternalGriddedConnection);
		Project project = BoundaryCondition?.Project;
		OperationResult operationResult = InitialConditionSource.TryResolveConnectionFile(project, ExternalGriddedConnection, out var filename);
		if (!operationResult)
		{
			return operationResult;
		}
		if (!Path.GetExtension(filename).RoughlyEquals(".h5"))
		{
			return OperationResult.Fail(new Message("Initial velocity connection {Name} is not a RAS result: {Filename}", ExternalGriddedConnection, filename));
		}
		OperationResult operationResult2 = InitialConditionSource.TryOpenSourceResult(project, filename, out var source, out var owned);
		if (!operationResult2)
		{
			return operationResult2;
		}
		try
		{
			if (source.AssociatedTerrain == null)
			{
				return OperationResult.Fail(new Message("Initial velocity source result {Filename} has no associated terrain.", filename));
			}
			ResultMapResampler resultMapResampler = new ResultMapResampler(source, MapTypes.Velocity);
			if (!InitialConditionSource.TryResolveProfileIndex(resultMapResampler.Times, SourceTimestep, out var profileIndex))
			{
				return OperationResult.Fail(new Message("Initial velocity source result {Filename} has no output timesteps.", filename));
			}
			if (!(resultMapResampler.Mapper is VelocityMapper velocityMapper))
			{
				return OperationResult.Fail(new Message("Expected a velocity mapper for {Filename}.", filename));
			}
			RasterDefinition rasterDefinition = RasterDefinition.ComputeBoundingRaster(resultMapResampler.ValidExtent, EffectiveIngestCellCount);
			float[] floatValues = new float[rasterDefinition.CellCount];
			source.AssociatedTerrain.Resample(new ResampleArgs(rasterDefinition), floatValues);
			GeometryMapPoints mps = new GeometryMapPoints(source.Geometry, rasterDefinition, GeometrySamplingParameters.StoringToDisk);
			float[] floatValuesArray = new float[rasterDefinition.CellCount];
			float[] floatValues2 = new float[rasterDefinition.CellCount];
			float[] floatValues3 = new float[rasterDefinition.CellCount];
			velocityMapper.Compute(profileIndex, mps, floatValues, floatValuesArray, floatValues2, floatValues3);
			DateTime[] dateTimes = new DateTime[1] { resultMapResampler.Times[profileIndex] };
			ITimeSeriesRaster<float> vx = new MultiBandRaster<float>(rasterDefinition, new float[1][] { floatValuesArray }, -9999f).AsTimeSeriesRaster(dateTimes);
			ITimeSeriesRaster<float> vy = new MultiBandRaster<float>(rasterDefinition, new float[1][] { floatValues2 }, -9999f).AsTimeSeriesRaster(dateTimes);
			_logger.LogDebug("Initial velocity resolved to {Filename} timestep {Timestep}", filename, dateTimes[0]);
			ImportRasters(vx, vy, project?.Projection, "Velocity", "", pr);
			NotifyRasterDataChange();
			return true;
		}
		finally
		{
			if (owned)
			{
				source?.Dispose();
			}
		}
	}

	public override OperationResult InitializeComputeDriver(ComputeControl computeControl, BoundaryConditionCollection bcs)
	{
		return true;
	}

	private IResample<float> TryGetComponent(bool xComponent)
	{
		if (!IsEnabled)
		{
			return null;
		}
		if (SpatialDataType == SpatialDataType.Constant)
		{
			return new ConstantValueResampler<float>(xComponent ? ConstantVx : ConstantVy, Extent.EffectivelyInfinite, -9999f);
		}
		if (SpatialDataType != SpatialDataType.Gridded)
		{
			return null;
		}
		string dataset = (xComponent ? ImportedRasterDatasetA : ImportedRasterDatasetB);
		OperationResult operationResult = H5RasterWriter.TryReadTimeSeriesRaster(BoundaryCondition.GetH5Wrapper(), dataset, out RasterMetadata md, out H5TimeSeriesRasterDataSource<float> result);
		if (!operationResult.Result || result == null)
		{
			AddMessages(operationResult);
			return null;
		}
		Projection projection = TryGetProjection();
		if (!string.IsNullOrEmpty(md.Projection) && projection != null && md.Projection != projection.GetWKT())
		{
			AddMessage(new Message("Initial velocity components are stored in a different projection than the project. Not supported."));
			return null;
		}
		ITimeSeriesRaster<float> timeSeriesRaster = result.AsTimeSeriesRasterizer();
		if (!InitialConditionSource.TryResolveProfileIndex(timeSeriesRaster.Times, SourceTimestep, out var profileIndex))
		{
			if (timeSeriesRaster.ProfileCount == 0)
			{
				return null;
			}
			profileIndex = 0;
		}
		return timeSeriesRaster.AsSingleProfile(profileIndex);
	}
}
