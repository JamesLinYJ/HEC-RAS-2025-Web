using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Geospatial.IO;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Engine;
using Ras.Hydraulics;
using Ras.Layers.Terrains;
using Utility.Extensions;
using Utility.Files;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;
using Utility.Threading;
using Utility.Units;

namespace Ras.Layers;

public class Plan : CoreLayerBase, IResultSubLayer
{
	public static class MessageCodes
	{
		public static readonly MessageClass NotFound = new MessageClass("Plan_NotFound", MessageSeverity.Error, "Plan \"{PlanName}\" was not found.");

		public static readonly MessageClass NoProjectAssociation = new MessageClass("Plan_NoProject", MessageSeverity.Error, "Plan \"{PlanName}\" does not have an associated Project.");

		public static readonly MessageClass IsEmbeddedInResult = new MessageClass("Plan_IsEmbeddedInResult", MessageSeverity.Error, "Plan \"{PlanName}\" is embedded in a result file.");

		public static readonly MessageClass IsNotEmbeddedInResult = new MessageClass("Plan_IsNotEmbeddedInResult", MessageSeverity.Error, "Plan \"{PlanName}\" is not embedded in a result file.");

		public static readonly MessageClass NoGeometryAssociation = new MessageClass("Plan_NoGeomAssociation", MessageSeverity.Error, "Plan \"{PlanName}\" has no associated Geometry.");

		public static readonly MessageClass NoBoundaryConditionAssociation = new MessageClass("Plan_NoBcAssociation", MessageSeverity.Error, "Plan \"{PlanName}\" has no associated Boundary Condition.");

		public static readonly MessageClass NoBCAssociation = new MessageClass("Plan_NoBCAssociation", MessageSeverity.Error, "Plan \"{PlanName}\" has no Boundary Condition association.");

		public static readonly MessageClass BadComputeTimestep = new MessageClass("Plan_BadComputeTimestep", MessageSeverity.Error, "Plan \"{PlanName}\" has an invalid compute timestep.");

		public static readonly MessageClass BadComputeTimeWindow = new MessageClass("Plan_BadComputeTimeWindow", MessageSeverity.Error, "Plan \"{PlanName}\" has an invalid time window.");

		public static readonly MessageClass MappingIntervalMismatchingComputeInterval = new MessageClass("Plan_MappingIntervalMismatchingComputeInterval", MessageSeverity.Error, "Plan {PlanName} has an output interval ({OutputInterval}) that is not an even multiple of Compute timestep ({ComputeTimestep}).");

		public static readonly MessageClass OnlySupportsOneMesh = new MessageClass("Plan_OnlySupportsOneMesh", MessageSeverity.Error, "Plan \"{PlanName}\" can only compute against a geometry with a single mesh. {GeometryName} has {GeometryMeshCount}");

		public static Message GetNoProjectAssociation(Plan plan)
		{
			return NoProjectAssociation.Create(plan.Name);
		}

		public static Message GetNoProjectAssociation(string planName)
		{
			return NoProjectAssociation.Create(planName);
		}

		public static Message GetIsEmbeddedInResult(Plan plan)
		{
			return IsEmbeddedInResult.Create(plan.Name);
		}

		public static Message GetIsEmbeddedInResult(string planName)
		{
			return IsEmbeddedInResult.Create(planName);
		}

		public static Message GetIsNotEmbeddedInResult(string planName)
		{
			return IsNotEmbeddedInResult.Create(planName);
		}

		public static Message GetNoGeometryAssociation(string planName)
		{
			return NoGeometryAssociation.Create(planName);
		}

		public static Message GetNoBoundaryConditionAssociation(string planName)
		{
			return NoBoundaryConditionAssociation.Create(planName);
		}

		public static Message GetMappingIntervalMismatchingComputeInterval(string planName, TimeSpan outputInterval, TimeSpan computeInterval)
		{
			return MappingIntervalMismatchingComputeInterval.Create(planName, outputInterval, computeInterval);
		}
	}

	public const string Extension = ".h5";

	public const string ReadyToRunExtension = ".r2r.h5";

	public const string HDF5GroupName = "/Plan Data";

	public static readonly string FileVersion = Project.DevFileVersion;

	public static readonly TimeSpan DefaultComputeBaseTimestep = TimeSpan.FromSeconds(60L);

	public const double DefaultComputeDurationSeconds = 43200.0;

	public static readonly bool DefaultUseFBF = false;

	public static readonly double DefaultTimestepSafetyFactor = 0.8;

	public static readonly bool DefaultUseMinAdvectiveTimeStep = true;

	public static readonly double DefaultMinAdvectiveTimeStep = 1.0;

	public static readonly double DefaultMaxPercentWetAdvTimeStep = 25.0;

	public static readonly double DefaultTheta = 1.0;

	public const double DefaultMaxCourant = 3.0;

	public const double DefaultActiveThresholdPartGrid = 60.0;

	public const int DefaultBufferCountPartGrid = 3;

	public const double DefaultElevationThresholdPartGrid = 0.25;

	public const int DefaultMaxInnerIterations = 20;

	public const double DefaultWaterSurfaceTolerance = 0.01;

	public const double DefaultWaterSurfaceMaxTolerance = 0.15;

	public const double DefaultWaterSurfaceRMSTolerance = 0.002;

	public const int DefaultMaxOuterIterations = 0;

	public const double DefaultVolumeTolerance = 0.01;

	public const double DefaultVolumeMaxTolerance = 0.1;

	public const double DefaultVolumeRMSTolerance = 0.001;

	public static readonly TimeSpan DefaultBaseOutputInterval = TimeSpan.FromHours(1);

	public const bool DefaultSensitivityRun = false;

	public const SolverControl.LtsLevelOptions DefaultLtsLevels = SolverControl.LtsLevelOptions.LtsLevels3;

	public const SolverType DefaultSolverType = SolverType.CPU;

	public const bool DefaultUseGPUAdaptive = true;

	public const SolverControl.EquationSet DefaultEquationSet = SolverControl.EquationSet.SWE;

	public const bool DefaultUseAdvection = true;

	public const SolverControl.AdvectionMethod DefaultAdvectionMethod = SolverControl.AdvectionMethod.ExplicitUpwind;

	public const double DefaultAdvectionImplicitFactor = 0.0;

	public const double DefaultImplicitFactor = 1.0;

	public const double DefaultLinearFrictionCoefficient = 0.0;

	public const bool DefaultUseDiffusion = false;

	public const TurbulenceModel DefaultTurbulenceModel = TurbulenceModel.Smagorinsky;

	public const double DefaultBaseEddyViscosity = 0.0;

	public const double DefaultLongMixCoeff = 0.3;

	public const double DefaultTransMixCoeff = 0.1;

	public const double DefaultHorizMixCoeff = 0.05;

	public const double DefaultLatitude = 0.0;

	public const PlanTimeWindowMode DefaultTimeWindowMode = PlanTimeWindowMode.BoundaryCondition;

	public const bool DefaultExtraOutput = false;

	private double? _elevationThresholdPartGrid;

	private double? _waterSurfaceTolerance;

	private double? _waterSurfaceMaxTolerance;

	private double? _waterSurfaceRMSTolerance;

	private double? _volumeTolerance;

	private double? _volumeMaxTolerance;

	private double? _volumeRMSTolerance;

	public double CheckMaxElevationChange = -1.0;

	public static readonly DateTime DefaultStartTime = new DateTime(2000, 1, 1);

	public static readonly SolverControl.ParallelMethod DefaultParallelMethod = SolverControl.ParallelMethod.PARALLEL_FOR;

	public static readonly int DefaultCoreCount = 8;

	private readonly EnsureSingleton _solveSingle = new EnsureSingleton();

	private static readonly double[] NiceTimeValues = new double[23]
	{
		0.1, 0.2, 0.5, 1.0, 2.0, 5.0, 10.0, 15.0, 20.0, 30.0,
		60.0, 120.0, 300.0, 600.0, 900.0, 1200.0, 1800.0, 3600.0, 7200.0, 10800.0,
		21600.0, 43200.0, 86400.0
	};

	public bool IsResult => Result != null;

	public Result Result { get; }

	public override bool NameMustMatchFilename => !IsResult;

	public Geometry Geometry
	{
		get
		{
			if (IsResult)
			{
				return Result.Geometry;
			}
			if (Project == null)
			{
				return null;
			}
			PlanAssociation planAssociation = Project.GetPlanAssociation(this);
			if (planAssociation == null)
			{
				CoreLayerBase._logger.LogWarning("Plan Association null for Plan '{Name}', likely initialization error.", Name);
				return null;
			}
			return planAssociation.Geometry;
		}
	}

	public BoundaryCondition BoundaryCondition
	{
		get
		{
			if (IsResult)
			{
				return Result.BoundaryConditions;
			}
			if (Project == null)
			{
				return null;
			}
			PlanAssociation planAssociation = Project.GetPlanAssociation(this);
			if (planAssociation == null)
			{
				CoreLayerBase._logger.LogWarning("Plan Association null for Plan '{Name}', likely initialization error.", Name);
				return null;
			}
			return planAssociation.BoundaryCondition;
		}
	}

	public PlanAssociation PlanAssociation => AssertGetProject().GetPlanAssociation(this);

	[HashContributor]
	[Metadata("ShortID", Optional = true)]
	public string ShortID { get; set; }

	public ITimeSeriesResampler<float> AnalyticalWaterSurface { get; set; }

	public ITimeSeriesResampler<float> AnalyticalVelocityMagnitude { get; set; }

	public ITimeSeriesResampler<float> AnalyticalVelocityX { get; set; }

	public ITimeSeriesResampler<float> AnalyticalVelocityY { get; set; }

	public string AnalyticalGroupName { get; set; }

	public string ExportDirectory => Path.Combine(SourceDirectory, Name);

	public double ComputeBaseTimestepSeconds => ComputeTimestep.TotalSeconds;

	[HashContributor]
	[Metadata("Base Compute Timestep", SetValueToDefaultIfKeyMissing = false)]
	public TimeSpan ComputeTimestep { get; set; } = DefaultComputeBaseTimestep;

	[HashContributor]
	[Metadata("Compute Duration")]
	public double InternalComputeDurationSeconds { get; set; } = 43200.0;

	public double ComputeDurationSeconds
	{
		get
		{
			if (TimeWindowMode == PlanTimeWindowMode.Plan)
			{
				return InternalComputeDurationSeconds;
			}
			return (EndTime - StartTime).TotalSeconds;
		}
	}

	public TimeSpan ComputeDurationTimeSpan => TimeSpan.FromSeconds(ComputeDurationSeconds);

	[HashContributor]
	[Metadata("Base Output Interval", SetValueToDefaultIfKeyMissing = false)]
	public TimeSpan BaseOutputInterval { get; set; } = DefaultBaseOutputInterval;

	[HashContributor]
	[Metadata("Sensitivity Run")]
	public bool SensitivityRun { get; set; }

	[HashContributor]
	[Metadata("LTS Levels", DefaultValue = SolverControl.LtsLevelOptions.LtsLevels3, SetValueToDefaultIfKeyMissing = true)]
	public SolverControl.LtsLevelOptions LtsLevels { get; set; } = SolverControl.LtsLevelOptions.LtsLevels3;

	[HashContributor]
	[Metadata("Time Step Safety Factor", LegacyKeyNames = new string[] { "Maximum CFL" }, SetValueToDefaultIfKeyMissing = false)]
	public double TimestepSafetyFactor { get; set; } = DefaultTimestepSafetyFactor;

	[HashContributor]
	[Metadata("Use Min Advective Time Step", SetValueToDefaultIfKeyMissing = false)]
	public bool UseMinAdvectiveTimeStep { get; set; } = DefaultUseMinAdvectiveTimeStep;

	[HashContributor]
	[Metadata("Minimum Advective Time Step", SetValueToDefaultIfKeyMissing = false)]
	public double MinAdvectiveTimeStep { get; set; } = DefaultMinAdvectiveTimeStep;

	[HashContributor]
	[Metadata("Maximum Wet Ratio for Minimum Advective Time Step", SetValueToDefaultIfKeyMissing = false)]
	public double MaxPercentWetAdvTimeStep { get; set; } = DefaultMaxPercentWetAdvTimeStep;

	[HashContributor]
	[Metadata("Theta", SetValueToDefaultIfKeyMissing = false)]
	public double Theta { get; set; } = DefaultTheta;

	[HashContributor]
	[Metadata("Use FBF")]
	public bool UseFBF { get; set; } = DefaultUseFBF;

	public bool UseGPU => SolverType == SolverType.GPU;

	[HashContributor]
	[Metadata("Solver", SetValueToDefaultIfKeyMissing = false)]
	public SolverType SolverType { get; set; }

	[HashContributor]
	[Metadata("Use GPU Adaptive Timestep", DefaultValue = true, SetValueToDefaultIfKeyMissing = true)]
	public bool UseGPUAdaptive { get; set; } = true;

	[HashContributor]
	[Metadata("Equation Set", DefaultValue = SolverControl.EquationSet.SWE, SetValueToDefaultIfKeyMissing = true)]
	public SolverControl.EquationSet EquationSet { get; set; } = SolverControl.EquationSet.SWE;

	[HashContributor]
	[Metadata("Time-stepping", DefaultValue = SolverControl.TimeStepping.Explicit, SetValueToDefaultIfKeyMissing = true)]
	public SolverControl.TimeStepping TimeStepping { get; set; }

	[HashContributor]
	[Metadata("Implicit Adaptive Timestepping", DefaultValue = true, SetValueToDefaultIfKeyMissing = true)]
	public bool ImplicitAdaptiveTimestepping { get; set; } = true;

	[HashContributor]
	[Metadata("Maximum Courant", DefaultValue = 3.0, SetValueToDefaultIfKeyMissing = true)]
	public double MaxCourant { get; set; } = 3.0;

	[HashContributor]
	[Metadata("Use Partial Grid", DefaultValue = true, SetValueToDefaultIfKeyMissing = true)]
	public bool UsePartialGrid { get; set; } = true;

	[HashContributor]
	[Metadata("Partial Grid Tolerance", DefaultValue = 60.0, SetValueToDefaultIfKeyMissing = true)]
	public double ActiveThresholdPartGrid { get; set; } = 60.0;

	[HashContributor]
	[Metadata("Partial Grid Buffer Count", DefaultValue = 3, SetValueToDefaultIfKeyMissing = true)]
	public int BufferCountPartGrid { get; set; } = 3;

	[HashContributor]
	[Metadata("Partial Grid Elevation Tolerance", DefaultValue = 0.25, SetValueToDefaultIfKeyMissing = true)]
	public double ElevationThresholdPartGrid
	{
		get
		{
			double? elevationThresholdPartGrid = _elevationThresholdPartGrid;
			if (!elevationThresholdPartGrid.HasValue)
			{
				if (!isMetric)
				{
					return 0.25;
				}
				return 0.0762;
			}
			return elevationThresholdPartGrid.GetValueOrDefault();
		}
		set
		{
			_elevationThresholdPartGrid = value;
		}
	}

	[HashContributor]
	[Metadata("Maximum Inner Iterations", DefaultValue = 20, SetValueToDefaultIfKeyMissing = true)]
	public int MaxInnerIterations { get; set; } = 20;

	[HashContributor]
	[Metadata("Water Surface Tolerance", DefaultValue = 0.01, SetValueToDefaultIfKeyMissing = true)]
	public double WaterSurfaceTolerance
	{
		get
		{
			double? waterSurfaceTolerance = _waterSurfaceTolerance;
			if (!waterSurfaceTolerance.HasValue)
			{
				if (!isMetric)
				{
					return 0.01;
				}
				return 0.0030480000000000004;
			}
			return waterSurfaceTolerance.GetValueOrDefault();
		}
		set
		{
			_waterSurfaceTolerance = value;
		}
	}

	private bool isMetric
	{
		get
		{
			if (Project != null)
			{
				return Project.Units != UnitSystem.USCustomary;
			}
			return false;
		}
	}

	[HashContributor]
	[Metadata("Water Surface Max Tolerance", DefaultValue = 0.15, SetValueToDefaultIfKeyMissing = true)]
	public double WaterSurfaceMaxTolerance
	{
		get
		{
			double? waterSurfaceMaxTolerance = _waterSurfaceMaxTolerance;
			if (!waterSurfaceMaxTolerance.HasValue)
			{
				if (!isMetric)
				{
					return 0.15;
				}
				return 0.045720000000000004;
			}
			return waterSurfaceMaxTolerance.GetValueOrDefault();
		}
		set
		{
			_waterSurfaceMaxTolerance = value;
		}
	}

	[HashContributor]
	[Metadata("Water Surface RMS Tolerance", DefaultValue = 0.002, SetValueToDefaultIfKeyMissing = true)]
	public double WaterSurfaceRMSTolerance
	{
		get
		{
			double? waterSurfaceRMSTolerance = _waterSurfaceRMSTolerance;
			if (!waterSurfaceRMSTolerance.HasValue)
			{
				if (!isMetric)
				{
					return 0.002;
				}
				return 0.0006096000000000001;
			}
			return waterSurfaceRMSTolerance.GetValueOrDefault();
		}
		set
		{
			_waterSurfaceRMSTolerance = value;
		}
	}

	[HashContributor]
	[Metadata("Maximum Outer Iterations", DefaultValue = 0, SetValueToDefaultIfKeyMissing = true)]
	public int MaxOuterIterations { get; set; }

	[HashContributor]
	[Metadata("Volume Tolerance", DefaultValue = 0.01, SetValueToDefaultIfKeyMissing = true)]
	public double VolumeTolerance
	{
		get
		{
			double? volumeTolerance = _volumeTolerance;
			if (!volumeTolerance.HasValue)
			{
				if (!isMetric)
				{
					return 0.01;
				}
				return 0.0030480000000000004;
			}
			return volumeTolerance.GetValueOrDefault();
		}
		set
		{
			_volumeTolerance = value;
		}
	}

	[HashContributor]
	[Metadata("Volume Max Tolerance", DefaultValue = 0.1, SetValueToDefaultIfKeyMissing = true)]
	public double VolumeMaxTolerance
	{
		get
		{
			double? volumeMaxTolerance = _volumeMaxTolerance;
			if (!volumeMaxTolerance.HasValue)
			{
				if (!isMetric)
				{
					return 0.1;
				}
				return 0.030480000000000004;
			}
			return volumeMaxTolerance.GetValueOrDefault();
		}
		set
		{
			_volumeMaxTolerance = value;
		}
	}

	[HashContributor]
	[Metadata("Volume RMS Tolerance", DefaultValue = 0.001, SetValueToDefaultIfKeyMissing = true)]
	public double VolumeRMSTolerance
	{
		get
		{
			double? volumeRMSTolerance = _volumeRMSTolerance;
			if (!volumeRMSTolerance.HasValue)
			{
				if (!isMetric)
				{
					return 0.001;
				}
				return 0.00030480000000000004;
			}
			return volumeRMSTolerance.GetValueOrDefault();
		}
		set
		{
			_volumeRMSTolerance = value;
		}
	}

	[HashContributor]
	[Metadata("Use Advection", DefaultValue = true, SetValueToDefaultIfKeyMissing = true)]
	public bool UseAdvection { get; set; } = true;

	[HashContributor]
	[Metadata("Advection Scheme", SetValueToDefaultIfKeyMissing = false)]
	public SolverControl.AdvectionMethod AdvectionMethod { get; set; }

	[HashContributor]
	[Metadata("Advection Implicit Factor", SetValueToDefaultIfKeyMissing = false)]
	public double AdvectionImplicitFactor { get; set; }

	[HashContributor]
	[Metadata("Implicit Factor", SetValueToDefaultIfKeyMissing = false)]
	public double ImplicitFactor { get; set; } = 1.0;

	[HashContributor]
	[Metadata("Linear Friction Coefficient", DefaultValue = 0.0, SetValueToDefaultIfKeyMissing = true)]
	public double LinearFrictionCoefficient { get; set; }

	[HashContributor]
	[Metadata("Use Diffusion", SetValueToDefaultIfKeyMissing = false)]
	public bool UseDiffusion { get; set; }

	[HashContributor]
	[Metadata("Turbulence Model", DefaultValue = TurbulenceModel.Smagorinsky, SetValueToDefaultIfKeyMissing = true)]
	public TurbulenceModel TurbulenceModel { get; set; }

	[HashContributor]
	[Metadata("Base Eddy Viscosity", SetValueToDefaultIfKeyMissing = false)]
	public double BaseEddyViscosity { get; set; }

	[HashContributor]
	[Metadata("Longitudinal Mixing Coefficient", SetValueToDefaultIfKeyMissing = false)]
	public double LongMixCoeff { get; set; } = 0.3;

	[HashContributor]
	[Metadata("Transverse Mixing Coefficient", SetValueToDefaultIfKeyMissing = false)]
	public double TransMixCoeff { get; set; } = 0.1;

	[HashContributor]
	[Metadata("Smagorinsky Mixing Coefficient", SetValueToDefaultIfKeyMissing = false)]
	public double HorizMixCoeff { get; set; } = 0.05;

	[HashContributor]
	[Metadata("Latitude for Coriolis")]
	public double Latitude { get; set; }

	[HashContributor]
	[Metadata("Time Window Mode")]
	public PlanTimeWindowMode TimeWindowMode { get; set; }

	public int ExpectedProfileCount => (int)Math.Ceiling(ComputeDurationSeconds / BaseOutputInterval.TotalSeconds) + 1;

	public FaceElevationMethod FaceElevationMethod { get; set; } = FaceElevationMethod.Upwind;

	[HashContributor]
	[Metadata("Compute Start Time")]
	public DateTime InternalStartTime { get; set; } = DefaultStartTime;

	public DateTime StartTime
	{
		get
		{
			if (TimeWindowMode == PlanTimeWindowMode.Plan)
			{
				return InternalStartTime;
			}
			if (BoundaryCondition == null)
			{
				return InternalStartTime;
			}
			return BoundaryCondition.StartTime;
		}
	}

	public DateTime EndTime
	{
		get
		{
			BoundaryCondition boundaryCondition = BoundaryCondition;
			if (TimeWindowMode == PlanTimeWindowMode.Plan || boundaryCondition == null)
			{
				return InternalStartTime.AddSeconds(InternalComputeDurationSeconds);
			}
			return boundaryCondition.EndTime;
		}
	}

	[HashContributor]
	[Metadata("Parallel Method", SetValueToDefaultIfKeyMissing = false)]
	public SolverControl.ParallelMethod ParallelMethod { get; set; } = DefaultParallelMethod;

	[HashContributor]
	[Metadata("Compute Core Count", SetValueToDefaultIfKeyMissing = false)]
	public int CoreCount { get; set; } = DefaultCoreCount;

	[HashContributor]
	[Metadata("Extra output fields")]
	public bool ExtraOutput { get; set; }

	[Metadata("Dev Options", Optional = true, SetValueToDefaultIfKeyMissing = false)]
	public string DevOptions { get; set; } = string.Empty;

	public bool IsSolving => _solveSingle.IsRunning;

	protected override string GetHDF5Group()
	{
		return "/Plan Data";
	}

	public override string GetCurrentFileFormatVersion()
	{
		return FileVersion;
	}

	public static string MakeFilename(string projectDir, string planName)
	{
		return Project.MakeFilename<Plan>(projectDir, planName, ".h5");
	}

	private Plan(InstantiationMode mode)
		: base(mode)
	{
	}

	public Plan(string filename, Project project = null)
		: base(filename, project)
	{
	}

	internal Plan(Result res)
		: this(InstantiationMode.ReadFromDisk)
	{
		Result = res;
		Filename = res.Filename;
		_h5Wrapper = res.H5Wrapper;
		InitializeData();
	}

	public static Plan CreateInMemory(string name = "")
	{
		return new Plan(InstantiationMode.InMemory)
		{
			_name = name,
			ShortID = name
		};
	}

	public static Plan CreateNew(string filename)
	{
		if (File.Exists(filename))
		{
			throw new Exception(filename.InDoubleQuotes() + " already exists.");
		}
		string shortID = ParseNameFromFile(filename);
		Plan plan = new Plan(InstantiationMode.CreateNewFile);
		plan.Filename = filename;
		plan._name = shortID;
		plan.ShortID = shortID;
		plan.Save();
		return plan;
	}

	private static string ParseNameFromFile(string fn)
	{
		return NameHelpers.ParseNameFromFile(fn, ".h5", ".h5");
	}

	protected override bool NeedsSaveInternal()
	{
		return false;
	}

	protected override void SaveInternal(H5Writer hw)
	{
	}

	public double[] OutputTimestepsInSimulationTime()
	{
		int expectedProfileCount = ExpectedProfileCount;
		double[] doubleValues = new double[expectedProfileCount];
		double totalSeconds = BaseOutputInterval.TotalSeconds;
		for (int i = 0; i < expectedProfileCount; i++)
		{
			doubleValues[i] = (double)i * totalSeconds;
		}
		return doubleValues;
	}

	public DateTime[] OutputTimestepsInRealTime()
	{
		int expectedProfileCount = ExpectedProfileCount;
		DateTime[] dateTimes = new DateTime[expectedProfileCount];
		DateTime startTime = StartTime;
		double totalSeconds = BaseOutputInterval.TotalSeconds;
		for (int i = 0; i < expectedProfileCount; i++)
		{
			dateTimes[i] = startTime.AddSeconds((double)i * totalSeconds);
		}
		return dateTimes;
	}

	public string DefaultReadyToRunFilename(bool fullPath = true)
	{
		if (IsInMemory)
		{
			throw new Exception("In-Memory plans cannot have a default ready-to-run file.");
		}
		string textValue = Name + ".r2r.h5";
		if (fullPath)
		{
			return Path.Combine(Path.Combine(Path.GetDirectoryName(Filename), Name), textValue);
		}
		return textValue;
	}

	public string DefaultResultFilename(bool fullPath = true)
	{
		if (IsInMemory)
		{
			throw new Exception("In-Memory plans cannot have a default ready-to-run file.");
		}
		string textValue = Name + " (Result).h5";
		if (fullPath)
		{
			return Path.Combine(Project.ResultsDirectory, textValue);
		}
		return textValue;
	}

	public OperationResult CanCompute()
	{
		PlanAssociation planAssociation = Project.GetPlanAssociation(this);
		if (planAssociation == null)
		{
			return OperationResult.Fail(MessageCodes.IsEmbeddedInResult.Create(Name));
		}
		OperationResult operationResult = new OperationResult(success: true);
		Geometry geometry = planAssociation.Geometry;
		if (geometry == null)
		{
			operationResult.AddFailure(MessageCodes.NoGeometryAssociation.Create(Name));
		}
		if (geometry != null)
		{
			OperationResult operationResult2 = geometry.CanCompute();
			if (!operationResult2)
			{
				operationResult.AddFailure(operationResult2);
			}
			if (geometry.CulvertBarrelLayer.Count > 0 && UseGPU)
			{
				operationResult.AddFailure(new Message("GPU solver does not currently support culvert hydraulics. Associated Geometry has {Count} culvert barrels.", geometry.CulvertBarrelLayer.Count));
			}
		}
		BoundaryCondition boundaryCondition = planAssociation.BoundaryCondition;
		if (boundaryCondition == null)
		{
			operationResult.AddFailure(MessageCodes.NoBCAssociation.Create(Name));
		}
		if (ComputeBaseTimestepSeconds <= 0.0)
		{
			operationResult.AddFailure(MessageCodes.BadComputeTimestep.Create(Name));
		}
		if (ComputeDurationSeconds <= 0.0)
		{
			operationResult.AddFailure(MessageCodes.BadComputeTimeWindow.Create(Name));
		}
		if (!TryGetOutputFrequency(out var _))
		{
			operationResult.AddFailure(MessageCodes.GetMappingIntervalMismatchingComputeInterval(Name, BaseOutputInterval, ComputeTimestep));
		}
		if (boundaryCondition != null)
		{
			OperationResult operationResult3 = boundaryCondition.ValidateTimeWindow(StartTime, ComputeDurationTimeSpan);
			if (!operationResult3.Result)
			{
				operationResult.Result = false;
				operationResult3.CopyMessagesTo(operationResult);
			}
		}
		if (geometry != null && geometry.FlowAreaLayer.Count != 1)
		{
			operationResult.AddFailure(MessageCodes.OnlySupportsOneMesh.Create(Name, geometry.Name, geometry.FlowAreaLayer.Count));
		}
		return operationResult;
	}

	public OperationResult TryPrepareCompute(string filename, out Result readyToRun, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (string.IsNullOrEmpty(filename) && !IsInMemory)
		{
			throw new ArgumentException("Filename cannot be empty.");
		}
		OperationResult operationResult = CanCompute();
		if (!operationResult)
		{
			reporter.ReportMessages(operationResult);
			readyToRun = null;
			return operationResult;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		using (_solveSingle.Start())
		{
			PlanAssociation planAssociation = Project.GetPlanAssociation(this);
			Geometry geometry = planAssociation.Geometry;
			Terrain associatedTerrain = geometry.AssociatedTerrain;
			BoundaryCondition boundaryCondition = planAssociation.BoundaryCondition;
			OperationResult operationResult2 = geometry.PrepareForCompute(reporter.SubTask("Preparing Geometry", 0f, 0.5f));
			if (!operationResult2)
			{
				reporter.ReportMessages(operationResult2);
				readyToRun = null;
				return operationResult2;
			}
			OperationResult operationResult3 = boundaryCondition.PrepareForCompute(reporter.SubTask("Boundary Conditions", 0.5f, 0.3f));
			if (!operationResult3)
			{
				reporter.ReportMessages(operationResult3);
				readyToRun = null;
				return operationResult3;
			}
			FlowArea flowArea = geometry.FlowAreaLayer[0];
			Mesh mesh = flowArea.Mesh;
			MeshPropertyTables propertyTables = flowArea.PropertyTables;
			MeshWaterState meshWaterState = new MeshWaterState(mesh, propertyTables);
			boundaryCondition.ApplyInitialWaterState(mesh, associatedTerrain, meshWaterState, reporter.SubTask("Initial Conditions", 0.8f, 0.1f));
			readyToRun = Result.PrepareResult(filename, this, geometry, boundaryCondition, meshWaterState, reporter.SubTask("Preparing Result", 0.9f, 0.1f));
			stopwatch.Stop();
			reporter.ReportTaskCompleted(stopwatch.Elapsed);
			return true;
		}
	}

	public string GetAvailableResultFilename()
	{
		return Project.GetUniqueResultFilename(Name);
	}

	public void SetOutputFrequencyByCount(int n)
	{
		double computeDurationSecondsTerm = ComputeDurationSeconds / ComputeBaseTimestepSeconds + 1.0;
		SetOutputFrequency((int)Math.Ceiling(computeDurationSecondsTerm / (double)n));
	}

	public void SetOutputFrequency(int n)
	{
		BaseOutputInterval = ComputeTimestep * n;
	}

	public bool TryGetOutputFrequency(out int freq)
	{
		long ticks = BaseOutputInterval.Ticks;
		long ticks2 = ComputeTimestep.Ticks;
		if (ticks2 == 0L)
		{
			freq = -1;
			return false;
		}
		freq = (int)(ticks / ticks2);
		return ticks % ticks2 == 0;
	}

	public void SetOutputFrequencyByTimespan(TimeSpan ts)
	{
		SetOutputFrequencyByCount((int)Math.Ceiling(ts.TotalSeconds / ComputeDurationSeconds));
	}

	public async Task<(string file, OperationResult result)> DebugExport(ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (IsResult)
		{
			return (file: string.Empty, result: OperationResult.Fail(MessageCodes.GetIsEmbeddedInResult(this)));
		}
		if (Project == null)
		{
			return (file: string.Empty, result: OperationResult.Fail(MessageCodes.GetNoProjectAssociation(this)));
		}
		PlanAssociation planAssociation = PlanAssociation;
		if (planAssociation == null)
		{
			return (file: string.Empty, result: OperationResult.Fail(Project.MessageCodes.GetNoPlanAssociation(Project, this)));
		}
		Geometry geometry = Geometry;
		BoundaryCondition boundaryCondition = planAssociation.BoundaryCondition;
		Project project = Project;
		Terrain associatedTerrain = geometry.AssociatedTerrain;
		NValueLayer associatedNValueLayer = geometry.AssociatedNValueLayer;
		Extent targetExtent = geometry.Extent.RatioAroundCenter(1.1);
		string path = Project.EnsureDefaultExportDirectory();
		string path2 = Path.Combine(Path.GetTempPath(), string.Concat(str2: Time.CurrentDateFormattedForFileSystem(), str0: project.Name, str1: "_"));
		if (!Directory.Exists(path2))
		{
			Directory.CreateDirectory(path2);
		}
		string filename = Path.Combine(Project.GetDefaultPlanDirectory(path2), Name + ".h5");
		SaveAs(filename);
		Plan p = new Plan(filename);
		string filename2 = Path.Combine(Project.GetDefaultGeometryDirectory(path2), geometry.Name + ".h5");
		geometry.SaveAs(filename2);
		Geometry geometry2 = new Geometry(filename2);
		string filename3 = Path.Combine(Project.GetDefaultBoundaryConditionDirectory(path2), boundaryCondition.Name + ".h5");
		boundaryCondition.SaveAs(filename3);
		BoundaryCondition boundaryCondition2 = new BoundaryCondition(filename3);
		string defaultTerrainsDirectory = Project.GetDefaultTerrainsDirectory(path2);
		string filename4 = Path.Combine(defaultTerrainsDirectory, Path.GetFileName(associatedTerrain.Filename));
		associatedTerrain.SaveAs(filename4);
		List<string> stringList = new List<string>();
		foreach (RasterTerrainInput allRasterInput in associatedTerrain.AllRasterInputs())
		{
			string destFilename = Path.Combine(defaultTerrainsDirectory, Path.GetFileName(allRasterInput.Filename));
			IRasterPyramid<float> resampler = allRasterInput.GetResampler();
			if (targetExtent.Overlaps(resampler.ValidExtent))
			{
				TiffExportEngine.ExportWithOverviews(destFilename, resampler, new RasterDefinition(targetExtent, resampler.RasterDefinition.CellSize), project.Projection);
			}
			else
			{
				stringList.Add(Path.GetFileName(allRasterInput.Filename));
			}
		}
		Terrain terrain = new Terrain(filename4);
		string filename5 = Path.Combine(Project.GetDefaultNValueLayersDirectory(path2), Path.GetFileName(associatedNValueLayer.Filename));
		associatedNValueLayer.SaveAs(filename5);
		NValueLayer nValueLayer = new NValueLayer(filename5);
		Project project2 = Project.CreateNew(Path.Combine(path2, project.Name + ".ras"), project.ProjectionDefinition);
		project2.AddGeometry(geometry2);
		project2.AddBoundaryCondition(boundaryCondition2);
		project2.AddPlan(p);
		project2.AddTerrain(terrain);
		project2.AddNValueLayer(nValueLayer);
		GeometryAssociation geometryAssociation = project2.GetGeometryAssociation(geometry2);
		geometryAssociation.Terrain = terrain;
		geometryAssociation.NValueLayer = nValueLayer;
		PlanAssociation planAssociation2 = project2.GetPlanAssociation(p);
		planAssociation2.Geometry = geometry2;
		planAssociation2.BoundaryCondition = boundaryCondition2;
		project2.Save();
		string zipFile = Path.Combine(path, Path.GetFileName(path2) + ".zip");
		ZipFile.CreateFromDirectory(path2, zipFile);
		try
		{
			Directory.Delete(path2);
		}
		catch (Exception exception)
		{
			CoreLayerBase._logger.LogWarning(exception, "Could not delete temp export directory.");
		}
		await Task.CompletedTask;
		FileHelpers.LaunchExplorerAndSelect(zipFile);
		return (file: zipFile, result: true);
	}

	public int GetHashVersionForBackup()
	{
		return GetDataHash();
	}

	public void SavePlanBackup(string backupFile)
	{
		if (File.Exists(backupFile))
		{
			throw new ArgumentException("Backup file exists");
		}
		using H5Writer h5Writer = new H5Writer(backupFile);
		string hDF5Group = GetHDF5Group();
		GeospatialH5Metadata.Write(this, h5Writer, hDF5Group);
		Project?.SaveProjectMetadataToH5Header(h5Writer);
	}

	public void SetApproximateRunSettings()
	{
		Mesh mesh = AssertGetPlanAssocation().Geometry.Mesh;
		double sqrtCellAverageArea = Math.Sqrt(mesh.GetMeshStatistics().CellAverageArea);
		double computed = 0.1;
		if (Project.Units != UnitSystem.USCustomary)
		{
			computed *= 0.3048;
		}
		double targetSeconds = sqrtCellAverageArea / computed;
		double roundToNiceTimeValue = RoundToNiceTimeValue(ComputeDurationSeconds / 100.0);
		BaseOutputInterval = TimeSpan.FromSeconds(roundToNiceTimeValue);
		double nearestNiceDivisor = NearestNiceDivisor(roundToNiceTimeValue, targetSeconds);
		ComputeTimestep = TimeSpan.FromSeconds(nearestNiceDivisor);
		CoreCount = (int)Math.Ceiling((float)mesh.CellCount / 6000f);
	}

	private static double RoundToNiceTimeValue(double seconds)
	{
		double niceTimeValue = NiceTimeValues[0];
		double absoluteSecondsAboveNiceTimeValue = Math.Abs(seconds - niceTimeValue);
		for (int i = 1; i < NiceTimeValues.Length; i++)
		{
			double secondsNiceTimeValuesTerm = Math.Abs(seconds - NiceTimeValues[i]);
			if (secondsNiceTimeValuesTerm < absoluteSecondsAboveNiceTimeValue)
			{
				niceTimeValue = NiceTimeValues[i];
				absoluteSecondsAboveNiceTimeValue = secondsNiceTimeValuesTerm;
			}
		}
		return niceTimeValue;
	}

	private static double NearestNiceDivisor(double intervalSeconds, double targetSeconds)
	{
		double unity = -1.0;
		double maxValue = double.MaxValue;
		double[] niceTimeValues = NiceTimeValues;
		foreach (double niceTimeValue in niceTimeValues)
		{
			if (niceTimeValue > intervalSeconds)
			{
				break;
			}
			double intervalSecondsOverNiceTimeValue = intervalSeconds / niceTimeValue;
			double computed = Math.Round(intervalSecondsOverNiceTimeValue);
			if (!(computed < 1.0) && !(Math.Abs(intervalSecondsOverNiceTimeValue - computed) > 1E-09))
			{
				double maxValue2 = Math.Abs(niceTimeValue - targetSeconds);
				if (maxValue2 < maxValue)
				{
					unity = niceTimeValue;
					maxValue = maxValue2;
				}
			}
		}
		if (unity < 0.0)
		{
			for (int lengthOffset = NiceTimeValues.Length - 1; lengthOffset >= 0; lengthOffset--)
			{
				if (NiceTimeValues[lengthOffset] <= intervalSeconds)
				{
					unity = NiceTimeValues[lengthOffset];
					break;
				}
			}
			if (unity < 0.0)
			{
				unity = NiceTimeValues[0];
			}
		}
		return unity;
	}

	private Project AssertGetProject()
	{
		if (Project != null)
		{
			return Project;
		}
		throw new Exception("Project has not been set.");
	}

	public PlanAssociation AssertGetPlanAssocation()
	{
		return AssertGetProject().GetPlanAssociation(this) ?? throw new Exception("No plan association found on this project.");
	}

	public static bool IsValid(string fn)
	{
		try
		{
			if (!File.Exists(fn))
			{
				return false;
			}
			string extension = Path.GetExtension(fn);
			if (extension.RoughlyEquals(".h5") || extension.RoughlyEquals(".hdf"))
			{
				using (H5Reader h5r = new H5Reader(fn))
				{
					return IsValid(h5r);
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public static bool IsValid(H5Reader h5r)
	{
		try
		{
			if (h5r.ExistsAsGroup("/Plan Data"))
			{
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public override string ToString()
	{
		return ShortID;
	}
}
