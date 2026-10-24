using System;
using System.IO;
using Geospatial.Rasters;
using Microsoft.Extensions.Logging;
using Ras.Engine;
using Ras.Layers.Meteorology;
using Ras.Mapping;
using Utility;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers.BoundaryConditions;

public class InitialStageLayer : SpatialScalarBoundaryConditionBase, IInitialConditionLayer
{
	public const string DefaultHDFGroupName = "/Boundary Conditions/Initial Conditions/Stage";

	public const string WaterSurfaceDataTypeName = "Water Surface Elevation";

	private const int DefaultIngestCellCount = 4194304;

	private static readonly ILogger _logger = LogHelper.GetLogger("InitialStageLayer");

	private Versioned<IResample<float>> _remoteSurface = new Versioned<IResample<float>>();

	private Result _sourceResult;

	private bool _ownsSourceResult;

	public override string LayerTypeName => "Initial Stage";

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

	public IResample<float> Surface
	{
		get
		{
			if (!IsEnabled)
			{
				return null;
			}
			if (SpatialDataType == SpatialDataType.Gridded && GridDataLocation == DataLocation.Remote)
			{
				if (!TryBuildRemoteSurface(out var surface))
				{
					return null;
				}
				return surface;
			}
			ITimeSeriesResampler<float> resampler = Resampler;
			if (resampler == null)
			{
				return null;
			}
			if (!InitialConditionSource.TryResolveProfileIndex(resampler.Times, SourceTimestep, out var profileIndex))
			{
				if (resampler.ProfileCount <= 0)
				{
					return null;
				}
				return resampler.AsSingleProfile(0);
			}
			return resampler.AsSingleProfile(profileIndex);
		}
	}

	public InitialStageLayer(BoundaryCondition bc)
		: base(bc, "/Boundary Conditions/Initial Conditions/Stage")
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

	public bool TryBuildRemoteSurface(out IResample<float> surface)
	{
		surface = _remoteSurface.Get(VersionXY, BuildRemoteSurface);
		return surface != null;
	}

	private IResample<float> BuildRemoteSurface()
	{
		DateTime sourceTime;
		return BuildRemoteSurface(out sourceTime);
	}

	private IResample<float> BuildRemoteSurface(out DateTime sourceTime)
	{
		sourceTime = default;
		if (!TryResolveConnectionFile(out var filename))
		{
			return null;
		}
		if (!Path.GetExtension(filename).RoughlyEquals(".h5"))
		{
			AddMessage(new Message("Initial stage connection {Name} is not a RAS result: {Filename}", ExternalGriddedConnection, filename));
			return null;
		}
		if (!TryGetSourceResult(filename, out var source))
		{
			return null;
		}
		if (source.AssociatedTerrain == null)
		{
			AddMessage(new Message("Initial stage source result {Filename} has no associated terrain.", filename));
			return null;
		}
		ResultMapResampler resultMapResampler = new ResultMapResampler(source, MapTypes.WaterSurfaceElevation);
		if (!InitialConditionSource.TryResolveProfileIndex(resultMapResampler.Times, SourceTimestep, out var profileIndex))
		{
			AddMessage(new Message("Initial stage source result {Filename} has no output timesteps.", filename));
			return null;
		}
		sourceTime = resultMapResampler.Times[profileIndex];
		_logger.LogDebug("Initial stage resolved to {Filename} timestep {Timestep}", filename, sourceTime);
		return resultMapResampler.AsSingleProfile(profileIndex);
	}

	private bool TryResolveConnectionFile(out string filename)
	{
		OperationResult operationResult = InitialConditionSource.TryResolveConnectionFile(BoundaryCondition?.Project, ExternalGriddedConnection, out filename);
		if (!operationResult)
		{
			AddMessages(operationResult);
			return false;
		}
		return true;
	}

	private bool TryGetSourceResult(string filename, out Result source)
	{
		if (_sourceResult != null)
		{
			source = _sourceResult;
			return true;
		}
		OperationResult operationResult = InitialConditionSource.TryOpenSourceResult(BoundaryCondition?.Project, filename, out source, out var owned);
		if (!operationResult)
		{
			AddMessages(operationResult);
			return false;
		}
		_sourceResult = source;
		_ownsSourceResult = owned;
		return true;
	}

	public void DisposeSource()
	{
		if (_ownsSourceResult)
		{
			_sourceResult?.Dispose();
		}
		_sourceResult = null;
		_ownsSourceResult = false;
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
		pr.ReportMessage("Ingesting Initial Stage from {Name}...", ExternalGriddedConnection);
		IResample<float> resample = BuildRemoteSurface(out var sourceTime);
		if (resample == null)
		{
			return OperationResult.Fail(new Message("Could not resolve initial stage source {Name}.", ExternalGriddedConnection));
		}
		RasterDefinition rasterDefinition = RasterDefinition.ComputeBoundingRaster(resample.ValidExtent, EffectiveIngestCellCount);
		float[] floatValues = new float[rasterDefinition.CellCount];
		resample.Resample(new ResampleArgs(rasterDefinition), floatValues);
		ITimeSeriesRaster<float> timed = new MultiBandRaster<float>(rasterDefinition, new float[1][] { floatValues }, -9999f).AsTimeSeriesRaster(new DateTime[1] { sourceTime });
		ImportRaster(timed, BoundaryCondition.Project?.Projection, "Water Surface Elevation", "", pr);
		NotifyRasterDataChange();
		DisposeSource();
		return true;
	}

	public override OperationResult InitializeComputeDriver(ComputeControl computeControl, BoundaryConditionCollection bcs)
	{
		return true;
	}
}
