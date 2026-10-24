using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Utility.Attributes;
using Utility.Matrices;
using Utility.Units;

namespace Ras.Engine;

public class SolverControl
{
	public enum EquationSet
	{
		[EnumDisplayName("Diffusion Wave Equation")]
		[Alias("Diffusion Wave Equation")]
		DWE,
		[EnumDisplayName("Shallow Water Equations")]
		[Alias("Shallow Water Equations")]
		SWE
	}

	public enum TimeStepping
	{
		Explicit,
		Implicit
	}

	public enum StepType
	{
		FWDBACK,
		FWDBACKFWD,
		FWDBACK_LTS,
		FWDBACKFWD_LTS,
		RK2FB
	}

	public enum CellVelocityMethod
	{
		Perot,
		WLS
	}

	public enum FaceTangentialVelocityMethod
	{
		CellBased,
		WLS,
		Momentum
	}

	public enum BedFrictionMethod
	{
		None,
		Constant,
		Variable
	}

	public enum GradientMethod
	{
		Distance,
		PerpDistance,
		LocalCoordinates
	}

	public enum AdvectionMethod
	{
		[EnumDisplayName("Explicit Upwind")]
		ExplicitUpwind,
		[EnumDisplayName("Semi-Implicit Upwind")]
		SemiImplicitUpwind,
		[EnumDisplayName("None")]
		None
	}

	public enum LtsLevelOptions
	{
		[EnumDisplayName("Disabled")]
		GlobalStepping,
		[EnumDisplayName("1 time level")]
		LtsLevels1,
		[EnumDisplayName("2 time levels")]
		LtsLevels2,
		[EnumDisplayName("3 time levels")]
		LtsLevels3,
		[EnumDisplayName("4 time levels")]
		LtsLevels4,
		[EnumDisplayName("5 time levels")]
		LtsLevels5
	}

	public enum LtsStepStrategy
	{
		CONSTANT,
		COURANT
	}

	public struct LoopControl
	{
		public bool IsInner;

		public int IterMax;

		public double tolMax;

		public double tolConv;

		public double tolRMS;
	}

	public enum ParallelMethod
	{
		[EnumDisplayName("Thread Pool")]
		PARALLEL_FOR,
		[EnumDisplayName("Dedicated Threads")]
		THREAD_FOR
	}

	public bool OutputCellDtMaxAdvection;

	public bool OutputCellDtMaxCelerity;

	public bool OutputCellDtMaxDiffusion;

	public GradientMethod gradientMethod = GradientMethod.LocalCoordinates;

	public FaceNormalGradientMethod faceNormalGradientMethod;

	public HydraulicMeshComputer.MultidimensionalSlopeLimiter multidimensionalSlopeLimiter = HydraulicMeshComputer.MultidimensionalSlopeLimiter.SmoothCubic;

	public FaceElevationMethod faceElevationMethod = FaceElevationMethod.Upwind;

	private EquationSet _equationSet = EquationSet.SWE;

	public TimeStepping timeStepping;

	public StepType stepType = StepType.FWDBACKFWD;

	public LtsLevelOptions LtsLevels = LtsLevelOptions.LtsLevels3;

	public LtsStepStrategy LtsStrategy = LtsStepStrategy.COURANT;

	public double dzMinAdvection = 0.01;

	public double checkMaxElevationChange = DefaultMaxElevationChange;

	public static double DefaultMaxElevationChange = 0.25;

	public bool celerityUsesFlow;

	public AdvectionMethod advectionMethod;

	public double AdvectionImplicitFactor;

	public double ImplicitFactor = 1.0;

	public double TimestepSafetyFactor = 0.8;

	public bool dtLimitDiffusion = true;

	public CellVelocityMethod cellVelocityMethod = CellVelocityMethod.WLS;

	public FaceTangentialVelocityMethod faceTangentialVelocityMethod;

	public bool streamwiseInertialTerm;

	public double ClampGradientThreshold = 1E-24;

	public double fCoriolis;

	public double dzMinFriction = 0.01;

	public BedFrictionMethod bedFrictionMethod = BedFrictionMethod.Variable;

	public double linFrictCoeff;

	public double nonlinFrictCoeff = 0.01;

	public double MaxCourant = 3.0;

	public LoopControl InnerControl = new LoopControl
	{
		IsInner = true,
		IterMax = 20,
		tolMax = 0.03048,
		tolConv = 0.003048,
		tolRMS = 3.048E-05
	};

	public LoopControl OuterControl = new LoopControl
	{
		IsInner = false,
		IterMax = 1,
		tolMax = 0.03048,
		tolConv = 0.003048,
		tolRMS = 3.048E-05
	};

	public bool UsePartialGrid = true;

	public double ActiveThresholdPartGrid = 60.0;

	public int BufferCountPartGrid = 2;

	public double ElevationThresholdPartGrid = 0.9144;

	public bool UseBlockSolver;

	public static bool? UseBlockSolverOverride = null;

	public int BlockSolverTinyMaxSize = 16;

	public int BlockSolverMediumMaxSize = 500;

	public bool BlockSolverParallel;

	public SolverKind MatrixSolverPrimary;

	public TurbulenceParams Turbulence = new TurbulenceParams();

	public SecondaryFlowControl SecondaryFlow = new SecondaryFlowControl();

	public bool checkSteps = true;

	public int maxLocalIterations = 3;

	public int numCores = 1;

	public ParallelMethod parallelMethod = ParallelMethod.THREAD_FOR;

	public double MinAdvDt = -1.0;

	public double MaxRatioWetAdvDt = 0.25;

	public EquationSet equationSet
	{
		get
		{
			return _equationSet;
		}
		set
		{
			_equationSet = value;
			if (value == EquationSet.DWE)
			{
				advectionMethod = AdvectionMethod.None;
				dtLimitDiffusion = false;
				timeStepping = TimeStepping.Implicit;
			}
		}
	}

	public bool variableDt
	{
		get
		{
			return computeControl.variableDt;
		}
		set
		{
			computeControl.variableDt = value;
		}
	}

	public long dtMicros
	{
		get
		{
			return computeControl.DtMicros;
		}
		set
		{
			computeControl.DtMicros = value;
		}
	}

	public long dtMinMicros
	{
		get
		{
			return computeControl.dtMinMicros;
		}
		set
		{
			computeControl.dtMinMicros = value;
		}
	}

	public int maxIterations
	{
		get
		{
			return computeControl.maxIterations;
		}
		set
		{
			computeControl.maxIterations = value;
		}
	}

	public static double GravSI => 9.80665025024414;

	public ComputeControl computeControl { get; protected set; }

	public double LToSI => computeControl.LToSI;

	public LinearSystemDumpSettings SaveLinearSystem { get; } = new LinearSystemDumpSettings();

	public SolverControl(ComputeControl computeControl)
	{
		this.computeControl = computeControl;
		SetDefaults();
	}

	public SolverControl(UnitSystem units, EquationSet es = EquationSet.SWE, TimeStepping ts = TimeStepping.Explicit)
	{
		computeControl = new ComputeControl(units);
		equationSet = es;
		timeStepping = ts;
		SetDefaults();
	}

	public SolverControl()
	{
		computeControl = new ComputeControl();
		SetDefaults();
	}

	protected void SetDefaults()
	{
		if (timeStepping == TimeStepping.Implicit)
		{
			faceElevationMethod = FaceElevationMethod.BenAverage;
			faceTangentialVelocityMethod = FaceTangentialVelocityMethod.WLS;
		}
		dzMinAdvection /= LToSI;
		checkMaxElevationChange /= LToSI;
		ElevationThresholdPartGrid /= LToSI;
		InnerControl.tolMax /= LToSI;
		InnerControl.tolConv /= LToSI;
		InnerControl.tolRMS /= LToSI;
		OuterControl.tolMax /= LToSI;
		OuterControl.tolConv /= LToSI;
		OuterControl.tolRMS /= LToSI;
	}

	public void SetAdvectionMethod(string advMethod)
	{
		if (advMethod.Equals("ExplicitUpwind"))
		{
			advectionMethod = AdvectionMethod.ExplicitUpwind;
		}
		else if (advMethod.Equals("SemiImplicitUpwind"))
		{
			advectionMethod = AdvectionMethod.SemiImplicitUpwind;
		}
		else if (advMethod.Equals("None"))
		{
			advectionMethod = AdvectionMethod.None;
		}
	}

	public void SetAdvectionMethodNone()
	{
		advectionMethod = AdvectionMethod.None;
	}

	public void SetCoriolisWidthLatitude(double latDeg)
	{
		fCoriolis = 0.0001454441043328608 * Math.Sin(latDeg * Math.PI / 180.0);
	}

	public void AddGhosts(List<int> cellIndexes, Mesh mesh)
	{
	}

	public ISolver SolverFactory(ComputeDriver driver, Mesh mesh, MeshPropertyTables props, MeshWaterState initialState = null, bool useGpu = false)
	{
		bool useGpu2 = useGpu;
		if ((equationSet == EquationSet.SWE) | useGpu2)
		{
			bool notUseGpu = timeStepping == TimeStepping.Implicit && !useGpu;
			if (initialState == null)
			{
				initialState = (useGpu ? MeshWaterState.Factory<SWEWaterStateConfigGPU>(mesh, props) : ((!notUseGpu) ? MeshWaterState.Factory<SWEWaterStateConfig>(mesh, props) : MeshWaterState.Factory<SWEWaterStateConfigImp>(mesh, props)));
			}
			else if (useGpu)
			{
				initialState.EnsureFields<SWEWaterStateConfigGPU>();
			}
			else if (notUseGpu)
			{
				initialState.EnsureFields<SWEWaterStateConfigImp>();
			}
			else
			{
				initialState.EnsureFields<SWEWaterStateConfig>();
			}
			if (notUseGpu)
			{
				return new SolverImpSWE(mesh, props, initialState, this, null, driver);
			}
			return new SolverExpSWE(mesh, props, initialState, this, driver);
		}
		if (equationSet == EquationSet.DWE)
		{
			if (initialState == null)
			{
				initialState = MeshWaterState.Factory<DWEWaterStateConfig>(mesh, props);
			}
			else
			{
				initialState.EnsureFields<DWEWaterStateConfig>();
			}
			return new SolverDWE(mesh, props, initialState, this);
		}
		throw new SolverException("Unrecognized solver type in Factory");
	}

	public void AllocateExtraStateFields(Mesh mesh, MeshWaterState waterState)
	{
		if (equationSet == EquationSet.SWE && timeStepping == TimeStepping.Explicit)
		{
			if (OutputCellDtMaxAdvection)
			{
				waterState.CellDtMaxAdvection = new float[mesh.CellCount];
			}
			if (OutputCellDtMaxCelerity)
			{
				waterState.CellDtMaxCelerity = new float[mesh.CellCount];
			}
			if (OutputCellDtMaxDiffusion)
			{
				waterState.CellDtMaxDiffusion = new float[mesh.CellCount];
			}
		}
	}
}
