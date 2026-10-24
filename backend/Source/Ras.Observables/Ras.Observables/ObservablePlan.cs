using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Ras.Engine;
using Ras.Layers;
using Utility.DataStructures;
using Utility.Files;
using Utility.Logging;
using Utility.Units;
using Utility.Validation;
using Visual.Observables;

namespace Ras.Observables;

public class ObservablePlan : ObservableCoreLayer
{
	public delegate void ComputeStartingEvent(ObservablePlan plan, BatchJob job);

	public delegate void ComputeStartedEvent(ObservableResult res, BatchJob job);

	public delegate void ComputeEndedEvent(ObservablePlan plan);

	public delegate void AssociatedBoundaryConditionChangedEvent(ObservablePlan sender, ObservableBoundaryCondition oldBc, ObservableBoundaryCondition newBc);

	public readonly struct ReadyToRunResult
	{
		public OperationResult OperationResult { get; init; }

		public string FilePath { get; init; }

		public Result R2R { get; init; }

		public static ReadyToRunResult Fail(OperationResult result)
		{
			return new ReadyToRunResult
			{
				OperationResult = result
			};
		}

		public static ReadyToRunResult Fail(Message msg)
		{
			return new ReadyToRunResult
			{
				OperationResult = OperationResult.Fail(msg)
			};
		}

		public static ReadyToRunResult Fail(IList<Message> msgs)
		{
			return new ReadyToRunResult
			{
				OperationResult = OperationResult.Fail(msgs)
			};
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("ObservablePlan");

	private ObservableBoundaryCondition _associatedBoundaryCondition;
	[CustomValidation(typeof(ObservablePlan), "ValidatePlan")]
	private OperationResult _latestPreComputeResult;

	private ObservableResult _result;

	private BatchJob _activeComputeJob;

	private ObservableResult _activeComputeResult;
	private bool _isComputing;
	private AdvancedCollection<ObservableResult> _nameMatchedResults = new AdvancedCollection<ObservableResult>();

	private int _lastBackupHash = -1;

	public static Type Type => typeof(ObservablePlan);

	public new Plan Layer => base.Layer as Plan;

	public Plan Plan => Layer;

	[Utility.Validation.NotNull("Plan must have associated geometry.")]
	public ObservableGeometry AssociatedGeometry
	{
		get
		{
			if (IsResult)
			{
				return null;
			}
			PlanAssociation planAssociation = Plan.PlanAssociation;
			if (planAssociation == null)
			{
				return null;
			}
			Geometry geo = planAssociation.Geometry;
			if (geo == null)
			{
				return null;
			}
			return Project.Geometries.FirstOrDefault((ObservableGeometry og) => og.Geometry == geo);
		}
		set
		{
			if (IsResult)
			{
				return;
			}
			PlanAssociation planAssociation = Plan.PlanAssociation;
			if (planAssociation == null)
			{
				throw new Exception();
			}
			if (value != null)
			{
				SetProperty(planAssociation.Geometry, value?.Geometry ?? null, planAssociation, (PlanAssociation setPlanAssn, Geometry setToGeo) =>
				{
					setPlanAssn.Geometry = setToGeo;
				}, "AssociatedGeometry");
				ValidateProperty(value, "AssociatedGeometry");
			}
		}
	}

	[Utility.Validation.NotNull("Plan must have associated boundary condition.")]
	public ObservableBoundaryCondition AssociatedBoundaryCondition
	{
		get
		{
			if (IsResult)
			{
				return null;
			}
			if (_associatedBoundaryCondition == null)
			{
				PlanAssociation planAssociation = Plan.PlanAssociation;
				if (planAssociation == null)
				{
					return null;
				}
				Ras.Layers.BoundaryCondition bc = planAssociation.BoundaryCondition;
				if (bc == null)
				{
					return null;
				}
				_associatedBoundaryCondition = Project.BoundaryConditions.FirstOrDefault((ObservableBoundaryCondition og) => og.BoundaryCondition == bc);
				RegisterListeners(_associatedBoundaryCondition);
			}
			return _associatedBoundaryCondition;
		}
		set
		{
			if (IsResult)
			{
				return;
			}
			PlanAssociation planAssociation = Plan.PlanAssociation;
			if (planAssociation == null)
			{
				throw new Exception();
			}
			ObservableBoundaryCondition associatedBoundaryCondition = _associatedBoundaryCondition;
			if (associatedBoundaryCondition != value && value != null)
			{
				DeregisterListeners(_associatedBoundaryCondition);
				SetProperty(planAssociation.BoundaryCondition, value?.BoundaryCondition ?? null, planAssociation, (PlanAssociation setPlanAssn, Ras.Layers.BoundaryCondition setToBc) =>
				{
					setPlanAssn.BoundaryCondition = setToBc;
				}, "AssociatedBoundaryCondition");
				_associatedBoundaryCondition = value;
				RegisterListeners(_associatedBoundaryCondition);
				OnPropertyChanged("ComputeStartTime");
				OnPropertyChanged("ComputeEndTime");
				AssociatedBoundaryConditionChanged?.Invoke(this, associatedBoundaryCondition, value);
				ValidateProperty(value, "AssociatedBoundaryCondition");
			}
		}
	}

	public string ShortID
	{
		get
		{
			return Plan.ShortID;
		}
		set
		{
			SetProperty(Plan.ShortID, value, Plan, (Plan a, string b) =>
			{
				a.ShortID = b;
			}, "ShortID");
		}
	}

	public DateTime StartTime
	{
		get
		{
			return Plan.InternalStartTime;
		}
		set
		{
			DateTime internalStartTime = Plan.InternalStartTime;
			if (SetProperty(Plan.InternalStartTime, value, Plan, (Plan a, DateTime b) =>
			{
				a.InternalStartTime = b;
			}, "StartTime"))
			{
				OnPropertyChanged("ComputeStartTime");
				TimeSpan timeSpan = value - internalStartTime;
				Plan.InternalComputeDurationSeconds -= timeSpan.TotalSeconds;
				OnPropertyChanged("ComputeEndTime");
			}
		}
	}

	public DateTime EndTime
	{
		get
		{
			return Plan.EndTime;
		}
		set
		{
			double totalSeconds = (value - Plan.InternalStartTime).TotalSeconds;
			SetProperty(Plan.InternalComputeDurationSeconds, totalSeconds, Plan, (Plan a, double b) =>
			{
				a.InternalComputeDurationSeconds = b;
			}, "EndTime");
			OnPropertyChanged("ComputeEndTime");
		}
	}

	public DateTime ComputeStartTime
	{
		get
		{
			return Plan.StartTime;
		}
		set
		{
			if (TimeWindowMode != PlanTimeWindowMode.Plan)
			{
				OnPropertyChanged("ComputeStartTime");
			}
			else
			{
				StartTime = value;
			}
		}
	}

	public DateTime ComputeEndTime
	{
		get
		{
			return Plan.EndTime;
		}
		set
		{
			if (TimeWindowMode != PlanTimeWindowMode.Plan)
			{
				OnPropertyChanged("ComputeEndTime");
			}
			else
			{
				EndTime = value;
			}
		}
	}

	public bool SensitivityRun
	{
		get
		{
			return Plan.SensitivityRun;
		}
		set
		{
			SetProperty(Plan.SensitivityRun, value, Plan, (Plan a, bool b) =>
			{
				a.SensitivityRun = b;
			}, "SensitivityRun");
		}
	}

	[RecommendedBounds(0.1, 5.0, "Value outside recommended range", false)]
	public double MaxCourant
	{
		get
		{
			return Plan.MaxCourant;
		}
		set
		{
			SetProperty(Plan.MaxCourant, value, Plan, (Plan a, double b) =>
			{
				a.MaxCourant = b;
			}, "MaxCourant");
		}
	}

	public SolverControl.LtsLevelOptions LtsLevels
	{
		get
		{
			return Plan.LtsLevels;
		}
		set
		{
			SetProperty(Plan.LtsLevels, value, Plan, (Plan a, SolverControl.LtsLevelOptions b) =>
			{
				a.LtsLevels = b;
			}, "LtsLevels");
		}
	}

	public double TimestepSafetyFactor
	{
		get
		{
			return Plan.TimestepSafetyFactor;
		}
		set
		{
			SetProperty(Plan.TimestepSafetyFactor, value, Plan, (Plan a, double b) =>
			{
				a.TimestepSafetyFactor = b;
			}, "TimestepSafetyFactor");
		}
	}

	public bool CanUseMinimumAdvectiveTimeStep => IsExplicit & UseMinAdvectiveTimeStep;

	public bool UseMinAdvectiveTimeStep
	{
		get
		{
			return Plan.UseMinAdvectiveTimeStep;
		}
		set
		{
			SetProperty(Plan.UseMinAdvectiveTimeStep, value, Plan, (Plan a, bool b) =>
			{
				a.UseMinAdvectiveTimeStep = b;
			}, "UseMinAdvectiveTimeStep");
			OnPropertyChanged("CanUseMinimumAdvectiveTimeStep");
		}
	}

	public double MinAdvectiveTimeStep
	{
		get
		{
			return Plan.MinAdvectiveTimeStep;
		}
		set
		{
			SetProperty(Plan.MinAdvectiveTimeStep, value, Plan, (Plan a, double b) =>
			{
				a.MinAdvectiveTimeStep = b;
			}, "MinAdvectiveTimeStep");
		}
	}

	[AbsoluteBounds(0.0, 100.0, "Value must be in range 0 - 100")]
	public double MaxPercentWetAdvTimeStep
	{
		get
		{
			return Plan.MaxPercentWetAdvTimeStep;
		}
		set
		{
			SetProperty(Plan.MaxPercentWetAdvTimeStep, value, Plan, (Plan a, double b) =>
			{
				a.MaxPercentWetAdvTimeStep = b;
			}, "MaxPercentWetAdvTimeStep");
		}
	}

	[AbsoluteBounds(0.0, 1.0, "Value must be in range 0 - 1")]
	public double Theta
	{
		get
		{
			return Plan.Theta;
		}
		set
		{
			SetProperty(Plan.Theta, value, Plan, (Plan a, double b) =>
			{
				a.Theta = b;
			}, "Theta");
		}
	}

	public bool UseFBF
	{
		get
		{
			return Plan.UseFBF;
		}
		set
		{
			SetProperty(Plan.UseFBF, value, Plan, (Plan a, bool b) =>
			{
				a.UseFBF = b;
			}, "UseFBF");
		}
	}

	public bool UseGPU => Plan.UseGPU;

	public SolverType SolverType
	{
		get
		{
			return Plan.SolverType;
		}
		set
		{
			SetProperty(Plan.SolverType, value, Plan, (Plan a, SolverType b) =>
			{
				a.SolverType = b;
			}, "SolverType");
			if (Plan.SolverType == SolverType.GPU)
			{
				EquationSet = SolverControl.EquationSet.SWE;
				TimeStepping = SolverControl.TimeStepping.Explicit;
			}
			OnPropertyChanged("IsShallowWater");
			OnPropertyChanged("IsDiffusionWave");
			OnPropertyChanged("IsImplicit");
			OnPropertyChanged("IsExplicit");
			OnPropertyChanged("CanUseMinimumAdvectiveTimeStep");
		}
	}

	public SolverControl.EquationSet EquationSet
	{
		get
		{
			return Plan.EquationSet;
		}
		set
		{
			SetProperty(Plan.EquationSet, value, Plan, (Plan a, SolverControl.EquationSet b) =>
			{
				a.EquationSet = b;
			}, "EquationSet");
			if (value == SolverControl.EquationSet.DWE && SolverType == SolverType.GPU)
			{
				SolverType = SolverType.CPU;
			}
			OnPropertyChanged("IsShallowWater");
			OnPropertyChanged("IsDiffusionWave");
			OnPropertyChanged("IsImplicit");
			OnPropertyChanged("IsExplicit");
			OnPropertyChanged("CanUseAdvection");
			OnPropertyChanged("CanUseDiffusion");
			OnPropertyChanged("CanUseMixingCoefficients");
			OnPropertyChanged("CanUseMinimumAdvectiveTimeStep");
		}
	}

	public SolverControl.TimeStepping TimeStepping
	{
		get
		{
			return Plan.TimeStepping;
		}
		set
		{
			SetProperty(Plan.TimeStepping, value, Plan, (Plan a, SolverControl.TimeStepping b) =>
			{
				a.TimeStepping = b;
			}, "TimeStepping");
			OnPropertyChanged("IsShallowWater");
			OnPropertyChanged("IsDiffusionWave");
			OnPropertyChanged("IsImplicit");
			OnPropertyChanged("IsExplicit");
			OnPropertyChanged("CanUseAdvection");
			OnPropertyChanged("CanUseDiffusion");
			OnPropertyChanged("CanUseMixingCoefficients");
			OnPropertyChanged("CanUseMinimumAdvectiveTimeStep");
		}
	}

	public bool IsShallowWater => !IsDiffusionWave;

	public bool IsDiffusionWave => Plan.EquationSet == SolverControl.EquationSet.DWE;

	public bool IsImplicit
	{
		get
		{
			if (Plan.TimeStepping != SolverControl.TimeStepping.Implicit)
			{
				return Plan.EquationSet == SolverControl.EquationSet.DWE;
			}
			return true;
		}
	}

	public bool IsExplicit => Plan.TimeStepping == SolverControl.TimeStepping.Explicit;

	public bool IsPartialGrid => IsImplicit & Plan.UsePartialGrid;

	public bool IsOuterLoop
	{
		get
		{
			if (Plan.TimeStepping == SolverControl.TimeStepping.Implicit)
			{
				return Plan.MaxOuterIterations >= 1;
			}
			return false;
		}
	}

	public bool ImplicitAdaptiveTimestepping
	{
		get
		{
			if (IsImplicit)
			{
				return Plan.ImplicitAdaptiveTimestepping;
			}
			return false;
		}
		set
		{
			SetProperty(Plan.ImplicitAdaptiveTimestepping, value, Plan, (Plan a, bool b) =>
			{
				a.ImplicitAdaptiveTimestepping = b;
			}, "ImplicitAdaptiveTimestepping");
		}
	}

	public bool UsePartialGrid
	{
		get
		{
			return Plan.UsePartialGrid;
		}
		set
		{
			SetProperty(Plan.UsePartialGrid, value, Plan, (Plan a, bool b) =>
			{
				a.UsePartialGrid = b;
			}, "UsePartialGrid");
			OnPropertyChanged("IsPartialGrid");
		}
	}

	public double PartialGridTolerance
	{
		get
		{
			return Plan.ActiveThresholdPartGrid;
		}
		set
		{
			SetProperty(Plan.ActiveThresholdPartGrid, value, Plan, (Plan a, double b) =>
			{
				a.ActiveThresholdPartGrid = b;
			}, "PartialGridTolerance");
		}
	}

	public int PartialGridBufferCount
	{
		get
		{
			return Plan.BufferCountPartGrid;
		}
		set
		{
			SetProperty(Plan.BufferCountPartGrid, value, Plan, (Plan a, int b) =>
			{
				a.BufferCountPartGrid = b;
			}, "PartialGridBufferCount");
		}
	}

	public double PartialGridElevationTolerance
	{
		get
		{
			return Plan.ElevationThresholdPartGrid;
		}
		set
		{
			SetProperty(Plan.ElevationThresholdPartGrid, value, Plan, (Plan a, double b) =>
			{
				a.ElevationThresholdPartGrid = b;
			}, "PartialGridElevationTolerance");
		}
	}

	public int MaxInnerIterations
	{
		get
		{
			return Plan.MaxInnerIterations;
		}
		set
		{
			SetProperty(Plan.MaxInnerIterations, value, Plan, (Plan a, int b) =>
			{
				a.MaxInnerIterations = b;
			}, "MaxInnerIterations");
		}
	}

	public double WaterSurfaceTolerance
	{
		get
		{
			return Plan.WaterSurfaceTolerance;
		}
		set
		{
			SetProperty(Plan.WaterSurfaceTolerance, value, Plan, (Plan a, double b) =>
			{
				a.WaterSurfaceTolerance = b;
			}, "WaterSurfaceTolerance");
		}
	}

	public double WaterSurfaceMaxTolerance
	{
		get
		{
			return Plan.WaterSurfaceMaxTolerance;
		}
		set
		{
			SetProperty(Plan.WaterSurfaceMaxTolerance, value, Plan, (Plan a, double b) =>
			{
				a.WaterSurfaceMaxTolerance = b;
			}, "WaterSurfaceMaxTolerance");
		}
	}

	public double WaterSurfaceRMSTolerance
	{
		get
		{
			return Plan.WaterSurfaceRMSTolerance;
		}
		set
		{
			SetProperty(Plan.WaterSurfaceRMSTolerance, value, Plan, (Plan a, double b) =>
			{
				a.WaterSurfaceRMSTolerance = b;
			}, "WaterSurfaceRMSTolerance");
		}
	}

	public int MaxOuterIterations
	{
		get
		{
			return Plan.MaxOuterIterations;
		}
		set
		{
			SetProperty(Plan.MaxOuterIterations, value, Plan, (Plan a, int b) =>
			{
				a.MaxOuterIterations = b;
			}, "MaxOuterIterations");
		}
	}

	public double VolumeTolerance
	{
		get
		{
			return Plan.VolumeTolerance;
		}
		set
		{
			SetProperty(Plan.VolumeTolerance, value, Plan, (Plan a, double b) =>
			{
				a.VolumeTolerance = b;
			}, "VolumeTolerance");
		}
	}

	public double VolumeMaxTolerance
	{
		get
		{
			return Plan.VolumeMaxTolerance;
		}
		set
		{
			SetProperty(Plan.VolumeMaxTolerance, value, Plan, (Plan a, double b) =>
			{
				a.VolumeMaxTolerance = b;
			}, "VolumeMaxTolerance");
		}
	}

	public double VolumeRMSTolerance
	{
		get
		{
			return Plan.VolumeRMSTolerance;
		}
		set
		{
			SetProperty(Plan.VolumeRMSTolerance, value, Plan, (Plan a, double b) =>
			{
				a.VolumeRMSTolerance = b;
			}, "VolumeRMSTolerance");
		}
	}

	public bool CanUseAdvection => Plan.UseAdvection & (Plan.EquationSet == SolverControl.EquationSet.SWE);

	public bool CanUseAdvectionImplicitFactor => CanUseAdvection & IsImplicit;

	public bool CanUseDiffusion => Plan.UseDiffusion & (Plan.EquationSet == SolverControl.EquationSet.SWE);

	public bool UseAdvection
	{
		get
		{
			return Plan.UseAdvection;
		}
		set
		{
			SetProperty(Plan.UseAdvection, value, Plan, (Plan a, bool b) =>
			{
				a.UseAdvection = b;
			}, "UseAdvection");
			OnPropertyChanged("CanUseAdvection");
		}
	}

	public SolverControl.AdvectionMethod AdvectionMethod
	{
		get
		{
			return Plan.AdvectionMethod;
		}
		set
		{
			SetProperty(Plan.AdvectionMethod, value, Plan, (Plan a, SolverControl.AdvectionMethod b) =>
			{
				a.AdvectionMethod = b;
			}, "AdvectionMethod");
		}
	}

	[AbsoluteBounds(0.0, 1.0, "Value must be in range 0-1")]
	public double AdvectionImplicitFactor
	{
		get
		{
			return Plan.AdvectionImplicitFactor;
		}
		set
		{
			SetProperty(Plan.AdvectionImplicitFactor, value, Plan, (Plan a, double b) =>
			{
				a.AdvectionImplicitFactor = b;
			}, "AdvectionImplicitFactor");
		}
	}

	[AbsoluteBounds(0.5, 1.0, "Value must be in range 0.5-1")]
	public double ImplicitFactor
	{
		get
		{
			return Plan.ImplicitFactor;
		}
		set
		{
			SetProperty(Plan.ImplicitFactor, value, Plan, (Plan a, double b) =>
			{
				a.ImplicitFactor = b;
			}, "ImplicitFactor");
		}
	}

	public bool UseDiffusion
	{
		get
		{
			return Plan.UseDiffusion;
		}
		set
		{
			SetProperty(Plan.UseDiffusion, value, Plan, (Plan a, bool b) =>
			{
				a.UseDiffusion = b;
			}, "UseDiffusion");
			OnPropertyChanged("CanUseDiffusion");
			OnPropertyChanged("CanUseMixingCoefficients");
		}
	}

	public TurbulenceModel TurbulenceModel
	{
		get
		{
			return Plan.TurbulenceModel;
		}
		set
		{
			SetProperty(Plan.TurbulenceModel, value, Plan, (Plan a, TurbulenceModel b) =>
			{
				a.TurbulenceModel = b;
			}, "TurbulenceModel");
			OnPropertyChanged("CanUseMixingCoefficients");
		}
	}

	public bool CanUseMixingCoefficients => CanUseDiffusion & (Plan.TurbulenceModel == TurbulenceModel.Smagorinsky);

	[RecommendedBounds(0.0, 10.0, "Value outside recommended range", false)]
	public double BaseEddyViscosity
	{
		get
		{
			return Plan.BaseEddyViscosity;
		}
		set
		{
			SetProperty(Plan.BaseEddyViscosity, value, Plan, (Plan a, double b) =>
			{
				a.BaseEddyViscosity = b;
			}, "BaseEddyViscosity");
		}
	}

	[RecommendedBounds(0.0, 3.0, "Value outside recommended range", false)]
	public double LongMixCoeff
	{
		get
		{
			return Plan.LongMixCoeff;
		}
		set
		{
			SetProperty(Plan.LongMixCoeff, value, Plan, (Plan a, double b) =>
			{
				a.LongMixCoeff = b;
			}, "LongMixCoeff");
		}
	}

	[RecommendedBounds(0.0, 1.0, "TransMixCoeff outside recommended range", false)]
	[AbsoluteBounds(0.0, 100.0, "TransMixCoeff outside valid range")]
	public double TransMixCoeff
	{
		get
		{
			return Plan.TransMixCoeff;
		}
		set
		{
			SetProperty(Plan.TransMixCoeff, value, Plan, (Plan a, double b) =>
			{
				a.TransMixCoeff = b;
			}, "TransMixCoeff");
		}
	}

	[RecommendedBounds(0.0, 0.5, "Value outside recommended range", false)]
	public double HorizMixCoeff
	{
		get
		{
			return Plan.HorizMixCoeff;
		}
		set
		{
			SetProperty(Plan.HorizMixCoeff, value, Plan, (Plan a, double b) =>
			{
				a.HorizMixCoeff = b;
			}, "HorizMixCoeff");
		}
	}

	[RecommendedBounds(-90.0, 90.0, "Latitude outside recommended range", false)]
	public double Latitude
	{
		get
		{
			return Plan.Latitude;
		}
		set
		{
			SetProperty(Plan.Latitude, value, Plan, (Plan a, double b) =>
			{
				a.Latitude = b;
			}, "Latitude");
		}
	}

	public TimeSpan ComputeTimestep
	{
		get
		{
			return Plan.ComputeTimestep;
		}
		set
		{
			SetProperty(Plan.ComputeTimestep, value, Plan, (Plan a, TimeSpan b) =>
			{
				a.ComputeTimestep = b;
			}, "ComputeTimestep");
		}
	}

	public PlanTimeWindowMode TimeWindowMode
	{
		get
		{
			return Plan.TimeWindowMode;
		}
		set
		{
			SetProperty(Plan.TimeWindowMode, value, Plan, (Plan a, PlanTimeWindowMode b) =>
			{
				a.TimeWindowMode = b;
			}, "TimeWindowMode");
			OnPropertyChanged("ComputeStartTime");
			OnPropertyChanged("ComputeEndTime");
		}
	}

	public SolverControl.ParallelMethod ParallelMethod
	{
		get
		{
			return Plan.ParallelMethod;
		}
		set
		{
			SetProperty(Plan.ParallelMethod, value, Plan, (Plan a, SolverControl.ParallelMethod b) =>
			{
				a.ParallelMethod = b;
			}, "ParallelMethod");
		}
	}

	public int CoreCount
	{
		get
		{
			return Plan.CoreCount;
		}
		set
		{
			SetProperty(Plan.CoreCount, value, Plan, (Plan a, int b) =>
			{
				a.CoreCount = b;
			}, "CoreCount");
		}
	}

	public bool ExtraOutput
	{
		get
		{
			return Plan.ExtraOutput;
		}
		set
		{
			SetProperty(Plan.ExtraOutput, value, Plan, (Plan a, bool b) =>
			{
				a.ExtraOutput = b;
			}, "ExtraOutput");
		}
	}

	public string DevOptions
	{
		get
		{
			return Plan.DevOptions;
		}
		set
		{
			SetProperty(Plan.DevOptions, value, Plan, (Plan a, string b) =>
			{
				a.DevOptions = b;
			}, "DevOptions");
		}
	}

	public bool PropertyTablesPinned
	{
		get
		{
			Geometry geometry = AssociatedGeometry?.Geometry;
			if (geometry == null)
			{
				return false;
			}
			FlowAreaLayer flowAreaLayer = geometry.FlowAreaLayer;
			if (flowAreaLayer == null || flowAreaLayer.Count == 0)
			{
				return false;
			}
			return flowAreaLayer[0].PropertyTablesPinned;
		}
		set
		{
			Geometry geometry = AssociatedGeometry?.Geometry;
			if (geometry != null)
			{
				FlowAreaLayer flowAreaLayer = geometry.FlowAreaLayer;
				if (flowAreaLayer != null && flowAreaLayer.Count != 0 && flowAreaLayer[0].PropertyTablesPinned != value)
				{
					flowAreaLayer[0].PropertyTablesPinned = value;
					OnPropertyChanged("PropertyTablesPinned");
				}
			}
		}
	}

	public TimeSpan BaseOutputInterval
	{
		get
		{
			return Plan.BaseOutputInterval;
		}
		set
		{
			SetProperty(Plan.BaseOutputInterval, value, Plan, (Plan a, TimeSpan b) =>
			{
				a.BaseOutputInterval = b;
			}, "BaseOutputInterval");
		}
	}

	public bool IsResult => Plan.IsResult;

	public bool IsNotResult => !IsResult;

	public ObservableResult Result => _result;

	public BatchJob ActiveComputeJob
	{
		get
		{
			return _activeComputeJob;
		}
		private set
		{
			SetProperty(ref _activeComputeJob, value, "ActiveComputeJob");
		}
	}

	public ObservableResult ActiveComputeResult
	{
		get
		{
			return _activeComputeResult;
		}
		private set
		{
			SetProperty(ref _activeComputeResult, value, "ActiveComputeResult");
		}
	}

	public List<BatchJob> JobLog { get; } = new List<BatchJob>();

	public AdvancedCollection<string> BackupFiles { get; init; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	[CustomValidation(typeof(ObservablePlan), "ValidatePlan")]
	public OperationResult LatestPreComputeResult
	{
		get
		{
			return _latestPreComputeResult;
		}
		[MemberNotNull("_latestPreComputeResult")]
		[RequiresUnreferencedCode("The type of the current instance cannot be statically discovered.")]
		set
		{
			if (!EqualityComparer<OperationResult>.Default.Equals(_latestPreComputeResult, value))
			{
				OnPropertyChanging("LatestPreComputeResult");
				_latestPreComputeResult = value;
				ValidateProperty(value, "LatestPreComputeResult");
				OnPropertyChanged("LatestPreComputeResult");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsComputing
	{
		get
		{
			return _isComputing;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isComputing, value))
			{
				OnPropertyChanging("IsComputing");
				_isComputing = value;
				OnPropertyChanged("IsComputing");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<ObservableResult> NameMatchedResults
	{
		get
		{
			return _nameMatchedResults;
		}
		[MemberNotNull("_nameMatchedResults")]
		set
		{
			if (!EqualityComparer<AdvancedCollection<ObservableResult>>.Default.Equals(_nameMatchedResults, value))
			{
				OnPropertyChanging("NameMatchedResults");
				_nameMatchedResults = value;
				OnPropertyChanged("NameMatchedResults");
			}
		}
	}

	public event ComputeStartingEvent ComputeStarting;

	public event ComputeStartedEvent ComputeStarted;

	public event ComputeEndedEvent ComputeEnded;

	public event AssociatedBoundaryConditionChangedEvent AssociatedBoundaryConditionChanged;

	public void BeginCompute(BatchJob job)
	{
		ActiveComputeJob = job;
		IsComputing = true;
		ComputeStarting?.Invoke(this, job);
	}

	public void EndCompute(BatchJob job)
	{
		IsComputing = false;
		ComputeEnded?.Invoke(this);
		ActiveComputeJob = null;
		ActiveComputeResult = null;
		JobLog.Add(job);
	}

	public ObservablePlan(ObservableProject prj, Plan plan)
		: base(prj, plan)
	{
		PropertyChanged += ObservablePlan_PropertyChanged;
		_lastBackupHash = plan.GetHashVersionForBackup();
		AdvancedCollection<string> advancedCollection = new AdvancedCollection<string>();
		foreach (string backupFilesFromDisk in GetBackupFilesFromDisk())
		{
			advancedCollection.Add(backupFilesFromDisk);
		}
		BackupFiles = advancedCollection;
		if (plan.IsResult)
		{
			_result = new ObservableResult(Project, Plan.Result);
			OnPropertyChanged("Result");
		}
	}

	public static ValidationResult ValidatePlan(OperationResult res, ValidationContext ctx)
	{
		if (res.Result)
		{
			return LeveledValidationResult.Success;
		}
		if (res.Messages.Count == 0)
		{
			return new LeveledValidationResult("Could not validate plan - unknown error.");
		}
		CompactList<LeveledValidationResult> compactList = default;
		foreach (Message message in res.Messages)
		{
			if (message.MessageType.DefaultSeverity == MessageSeverity.Error)
			{
				compactList.Add(new LeveledValidationResult(message.EvaluatedMessage));
				break;
			}
		}
		if (compactList.Count == 0)
		{
			return LeveledValidationResult.Success;
		}
		return compactList[0];
	}

	private void ObservablePlan_PropertyChanged(object sender, PropertyChangedEventArgs e)
	{
		if (!e.PropertyName.Equals("LatestPreComputeResult"))
		{
			UpdateLatestPreComputeResult();
		}
	}

	private void RegisterListeners(ObservableBoundaryCondition bc)
	{
		if (bc != null)
		{
			bc.StartTimeChanged += Bc_StartTimeChanged;
			bc.EndTimeChanged += Bc_EndTimeChanged;
		}
	}

	private void DeregisterListeners(ObservableBoundaryCondition bc)
	{
		if (bc != null)
		{
			bc.StartTimeChanged -= Bc_StartTimeChanged;
			bc.EndTimeChanged -= Bc_EndTimeChanged;
		}
	}

	private void Bc_StartTimeChanged(ObservableBoundaryCondition bc, DateTime newTime)
	{
		OnPropertyChanged("ComputeStartTime");
	}

	private void Bc_EndTimeChanged(ObservableBoundaryCondition bc, DateTime newTime)
	{
		OnPropertyChanged("ComputeEndTime");
	}

	public override bool TryRenameByDeleteAndSaveAsH5(string inputName, out string errorText)
	{
		return TryRenameByDeleteAndSaveAsH5Internal(inputName, Plan.MakeFilename, out errorText);
	}

	public void UpdateLatestPreComputeResult()
	{
		try
		{
			LatestPreComputeResult = Plan.CanCompute();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error checking Plan CanCompute");
			LatestPreComputeResult = OperationResult.Fail("Error checking if plan can compute.", ex);
		}
	}

	public async Task<ReadyToRunResult> BuildReadyToRunAsync(BatchJob job, string outputFilename)
	{
		ObservableGeometry geo = AssociatedGeometry;
		if (geo == null)
		{
			return ReadyToRunResult.Fail(Plan.MessageCodes.GetNoGeometryAssociation(Plan.Name));
		}
		ObservableBoundaryCondition bc = AssociatedBoundaryCondition;
		if (bc == null)
		{
			return ReadyToRunResult.Fail(Plan.MessageCodes.GetNoBoundaryConditionAssociation(Plan.Name));
		}
		OperationResult operationResult = await geo.FlowAreaLayer.ComputePropertyTablesAsync(job);
		if (job.Reporter.CancelRequested)
		{
			return ReadyToRunResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Computing Property Tables"));
		}
		if (!operationResult.Result)
		{
			return ReadyToRunResult.Fail(operationResult.Messages);
		}
		geo.Geometry.Save();
		bc.BoundaryCondition.Save();
		OperationResult operationResult2 = Plan.TryPrepareCompute(outputFilename, out var readyToRun, job.Reporter);
		if (!operationResult2.Result)
		{
			return ReadyToRunResult.Fail(operationResult2.Messages);
		}
		if (readyToRun == null)
		{
			throw new Exception("Error preparing result.");
		}
		return new ReadyToRunResult
		{
			OperationResult = OperationResult.Success(),
			FilePath = outputFilename,
			R2R = readyToRun
		};
	}

	public async Task<OperationResult> ComputeAsync(BatchJob job, Action<ObservableResult> computeStarted = null)
	{
		if (job == null)
		{
			throw new ArgumentNullException("job");
		}
		if (ActiveComputeJob != null)
		{
			throw new Exception("Cannot compute against a plan that is already computing.");
		}
		OperationResult success = null;
		BeginCompute(job);
		try
		{
			string uniqueResultFilename = Project.Project.GetUniqueResultFilename(Plan.Name);
			ReadyToRunResult readyToRunResult = await BuildReadyToRunAsync(job, uniqueResultFilename);
			success = readyToRunResult.OperationResult;
			if (!success)
			{
				return success;
			}
			Result res = readyToRunResult.R2R;
			ObservableResult observableResult = (ActiveComputeResult = new ObservableResult(Project, res));
			observableResult.ActiveComputeJob = job;
			Project.AddResult(observableResult);
			computeStarted?.Invoke(observableResult);
			ComputeStarted?.Invoke(observableResult, job);
			success = await Task.Run(() =>
			{
				try
				{
					return res.Compute((Action act) =>
					{
						job.UIThreadSyncContext.Sync(act);
					}, job.Reporter);
				}
				catch (Exception ex)
				{
					return OperationResult.Fail("Compute", ex);
				}
			});
			return success;
		}
		finally
		{
			if (success != null && success.Messages.Count > 0)
			{
				for (int intervalIndex = 0; intervalIndex < success.Messages.Count; intervalIndex++)
				{
					job.Reporter.ReportMessage(success.Messages[intervalIndex]);
				}
			}
			EndCompute(job);
		}
	}

	public string CheckCreateBackup(string backupName = "")
	{
		if (Plan.IsInMemory)
		{
			return "";
		}
		if (Plan.IsResult)
		{
			return "";
		}
		int hashVersionForBackup = Plan.GetHashVersionForBackup();
		if (string.IsNullOrEmpty(backupName) && _lastBackupHash == hashVersionForBackup)
		{
			return "";
		}
		_lastBackupHash = hashVersionForBackup;
		string backupDirectory = BackupDirectory();
		if (!Directory.Exists(backupDirectory))
		{
			Directory.CreateDirectory(backupDirectory);
		}
		string baseFilename = ((!string.IsNullOrEmpty(backupName)) ? Path.Combine(backupDirectory, backupName + ".h5") : Path.Combine(backupDirectory, "Backup " + Time.CurrentDateFilenameSafe() + ".h5"));
		baseFilename = FileHelpers.UniqueFilename(baseFilename);
		Plan.SavePlanBackup(baseFilename);
		BackupFiles.Add(baseFilename);
		return baseFilename;
	}

	public string BackupDirectory()
	{
		return Path.Combine(Path.GetDirectoryName(Filename), Path.GetFileNameWithoutExtension(Filename) + " Backups");
	}

	public List<string> GetBackupFilesFromDisk()
	{
		if (IsInMemory)
		{
			return new List<string>();
		}
		string path = BackupDirectory();
		if (!Directory.Exists(path))
		{
			return new List<string>();
		}
		return Directory.GetFiles(path).ToList();
	}

	protected override void DeleteSelfInternal()
	{
		Project.RemovePlan(this);
		Layer.DisposeAndDeleteAllBackingFiles();
	}
}
