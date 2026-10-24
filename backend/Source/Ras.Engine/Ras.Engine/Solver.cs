#define TRACE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Ras.Engine.Engine;
using Ras.Engine.Structures;
using Ras.Engine.Structures.Gates;
using Utility;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Engine;

public abstract class Solver : ISolver
{
	public enum StepLimitation
	{
		LIM_NONE,
		LIM_CELERITY,
		LIM_USER,
		LIM_ADVECTION,
		LIM_DIFFUSION,
		LIM_INCREASE,
		LIM_RETRY_ERROR,
		LIM_RETRY_NEGATIVE_VOLUME,
		LIM_RETRY_ELEVATION_CHANGE,
		LIM_RETRY_NAN_STATE
	}

	public enum FaceFlag : byte
	{
		FORCE_FLOW = 1,
		COMPUTE_FLOW,
		HAS_FLOW
	}

	public enum Ticker
	{
		SOLVESTEP,
		FACEVELOCITY,
		CELLADVECTION,
		FRICTIONVELOCITY,
		STRUCTURES,
		UPDATEVOLUME,
		FACESTATE,
		VISCOSITY,
		CELLVELOCITY,
		LTS_CALC_LEVEL0,
		LTS_HALOS,
		LTS_INDEX_LISTS,
		MAXTIMESTEP,
		CHECKSTEP,
		COPYSTATE,
		SOLVEINNER,
		ASSEMBLY,
		PRESOLVEOUTER,
		PARTIALGRID,
		MATRIXPREPARE,
		PRESOLVEINNER,
		RESOLVEINNERFACEPROPS,
		PRESOLVEINNERFACEVARS,
		PRECOMPUTENEWTON,
		SOLVENEWTON,
		MATRIXSOLVE,
		POSTSOLVENEWTON,
		POSTSOLVEINNER,
		POSTSOLVEOUTER
	}

	public ThreadFor threadFor;

	protected static readonly ILogger _logger = LogHelper.GetLogger("Solver");

	protected readonly ComputeDriver _computeDriver;

	protected readonly Mesh _mesh;

	protected readonly MeshPropertyTables _propertyTables;

	protected readonly HydraulicMeshComputer _hmesh;

	protected MeshConnectivity _meshConnectivity;

	public StepLimitation stepLimitation;

	public int limitingCell;

	public int limitingFace;

	public int retryCount;

	public int[] stepLimitationCount;

	public int[] rejectCounts;

	protected MeshWaterState _waterState;

	protected double[] newCellVolume;

	[Expose(MeshElementType.Cell)]
	public double[] CellDtMax;

	public SolverConvergence Convergence;

	public BoundaryConditionCollection BoundaryConditions;

	public HydraulicStructureCollection HydraulicStructures;

	private bool _lastHydraulicStepSucceeded;

	private double _attemptDuration;

	public List<int> ILCellAll;

	public List<int> ILFaceAll;

	protected List<int> ILCellVolume;

	protected List<int> ILFaceAllVelocity;

	protected List<int> ILFaceVelocity;

	protected List<int> ILCellVelocity;

	protected List<int> ILFaceVelFrict;

	public List<MassConservationCheck> MassConservationChecks = new List<MassConservationCheck>();

	protected MeshWaterState _oldState;

	protected TurbulenceSolver _turbulence;

	protected SecondaryFlowSolver _secondaryFlow;

	protected StepChecker checker;

	public FaceFlag[] FaceFlags;

	public ComputeControl.CellFlag[] CellFlags;

	private bool[] _faceUsesNonOrthogonalCorrection;

	public long[] ticks;

	protected long _endMicros => _computeDriver.EndMicros;

	public double EndTime
	{
		get
		{
			return _computeDriver.EndTime;
		}
		set
		{
			_computeDriver.EndTime = value;
		}
	}

	public ComputeDriver ComputeDriver => _computeDriver;

	public Mesh Mesh => _mesh;

	public MeshPropertyTables PropertyTables => _propertyTables;

	public HydraulicMeshComputer HydraulicMeshComputer => _hmesh;

	public MeshConnectivity MeshConnectivity => _meshConnectivity;

	public double grav => computeControl.grav;

	public double WaterDensity => computeControl.WaterDensity;

	public double AirDensity => computeControl.AirDensity;

	public double nSItoUnits => computeControl.nSItoUnits;

	public long TMicros => _waterState.tMicros;

	public Action<MeshTimestep> TimestepDone { get; private set; }

	public Action<MeshTimestep> ComputeStepDone { get; set; }

	public double Time => ComputeDriver.MicrosToSeconds(TMicros);

	public double Timestep => ComputeDriver.MicrosToSeconds(_waterState.dtMicros);

	public long step => _computeDriver.StepCount;

	public MeshWaterState WaterState => _waterState;

	public SolverControl Control { get; }

	public ComputeControl computeControl => Control.computeControl;

	public List<BoundaryCondition> FlowBoundaryConditions => BoundaryConditions.FlowBoundaryConditions;

	public List<BoundaryCondition> StageBoundaryConditions => BoundaryConditions.StageBoundaryConditions;

	public List<BoundaryCondition> RatingCurveBoundaryConditions => BoundaryConditions.RatingCurveBoundaryConditions;

	public List<BoundaryCondition> NormalDepthBoundaryConditions => BoundaryConditions.NormalDepthBoundaryConditions;

	public List<BoundaryCondition> VariableFlowBoundaryConditions => BoundaryConditions.VariableFlowBoundaryConditions;

	public List<BoundaryCondition> VariableStageBoundaryConditions => BoundaryConditions.VariableStageBoundaryConditions;

	public List<BoundaryCondition> SpatiallyVariableStageVelocityBoundaryConditions => BoundaryConditions.SpatiallyVariableStageVelocityBoundaryConditions;

	public List<BoundaryCondition> FlowIntBoundaryConditions => BoundaryConditions.InternalFlowBoundaryConditions;

	public CellWaterSourceLedger CellWaterSources { get; private set; }

	protected virtual bool RefreshCellWaterSourcesAtStepStart => true;

	public MeshWaterState OldState => _oldState;

	public TurbulenceSolver Turbulence => _turbulence;

	public SecondaryFlowSolver SecondaryFlow => _secondaryFlow;

	public ProgressReporter Reporter
	{
		get
		{
			return _computeDriver.Reporter;
		}
		set
		{
			_computeDriver.Reporter = value;
		}
	}

	protected bool UsesPhysicalPerpendicularDistance
	{
		get
		{
			if (Control.faceNormalGradientMethod != FaceNormalGradientMethod.VirtualNode)
			{
				return Control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased;
			}
			return true;
		}
	}

	protected virtual double[] FaceInverseDistances
	{
		get
		{
			if (UsesPhysicalPerpendicularDistance)
			{
				return _hmesh.FaceInvPerpDistances;
			}
			return Control.gradientMethod switch
			{
				SolverControl.GradientMethod.PerpDistance => _hmesh.FaceInvPerpDistances, 
				SolverControl.GradientMethod.Distance => _hmesh.FaceRepresentativeInverseDistances, 
				_ => _hmesh.FaceInvPerpDistances, 
			};
		}
	}

	protected bool HasNonOrthogonalCorrection => _faceUsesNonOrthogonalCorrection != null;

	public Mesh GetMesh()
	{
		return _mesh;
	}

	public void AddTimestepDone(Action<MeshTimestep> callback)
	{
		TimestepDone = (Action<MeshTimestep>)Delegate.Combine(TimestepDone, callback);
	}

	public void RemoveTimestepDone(Action<MeshTimestep> callback)
	{
		TimestepDone = (Action<MeshTimestep>)Delegate.Remove(TimestepDone, callback);
	}

	public void AddComputeStepDone(Action<MeshTimestep> callback)
	{
		ComputeStepDone = (Action<MeshTimestep>)Delegate.Combine(ComputeStepDone, callback);
	}

	public void RemoveComputeStepDone(Action<MeshTimestep> callback)
	{
		ComputeStepDone = (Action<MeshTimestep>)Delegate.Remove(ComputeStepDone, callback);
	}

	public MeshWaterState MaybeGetMeshWaterState()
	{
		return _waterState;
	}

	public virtual ReadOnlySpan<double> GetFaceVelocity()
	{
		return _waterState.FaceVelocity;
	}

	public ReadOnlySpan<double> GetFaceFlow()
	{
		return _waterState.FaceFlow;
	}

	public ReadOnlySpan<double> GetFaceArea()
	{
		return _waterState.FaceArea;
	}

	public ReadOnlySpan<double> GetCellWaterSurface()
	{
		return _waterState.CellElevation;
	}

	public ReadOnlySpan<double> GetCellVolume()
	{
		return _waterState.CellVolume;
	}

	public Span<ComputeControl.CellFlag> GetCellFlags()
	{
		return CellFlags;
	}

	public BoundaryConditionCollection GetBoundaryConditions()
	{
		return BoundaryConditions;
	}

	public HydraulicStructureCollection GetHydraulicStructures()
	{
		return HydraulicStructures;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected static void ComputeCellAccumulation(int cIdx, int[] offsets, ReadOnlySpan<int> neighborFaces, ReadOnlySpan<int> neighborCells, ReadOnlySpan<int> faceOrientations, ReadOnlySpan<double> faceVelocity, ReadOnlySpan<double> faceArea, ReadOnlySpan<Geospatial.Vectors.Vector> cellVelocity, Geospatial.Vectors.Vector[] faceNormals, Geospatial.Vectors.Vector[,] localFaceNormals, ComputeControl.CellFlag[] cellFlags, LocalCoordinates localCoordinates, bool[] cellActivity, int[] faceToPartialFace, Geospatial.Vectors.Vector[] boundaryVelocityMap, double sourceFlow, Geospatial.Vectors.Vector sourceMomentum, out Vector2 explicitVariable, out float implicitVariable)
	{
		double xComponent = 0.0;
		double yComponent = 0.0;
		double implicitVariable2 = 0.0;
		int offsetStart = offsets[cIdx];
		int offsetEnd = offsets[cIdx + 1];
		for (int i = offsetStart; i < offsetEnd; i++)
		{
			int neighborFaceIndex = neighborFaces[i];
			if ((faceToPartialFace != null && faceToPartialFace[neighborFaceIndex] < 0) || faceArea[neighborFaceIndex] <= 0.0)
			{
				continue;
			}
			int faceOrientation = faceOrientations[i];
			double faceOrientationFaceVelocityTerm = (double)faceOrientation * faceVelocity[neighborFaceIndex];
			if (faceOrientationFaceVelocityTerm >= 0.0)
			{
				continue;
			}
			int neighborCellIndex = neighborCells[i];
			int faceOrientationTerm = ((faceOrientation <= 0) ? 1 : 0);
			Geospatial.Vectors.Vector input;
			if (neighborCellIndex >= 0)
			{
				input = cellVelocity[neighborCellIndex];
				if (cellFlags[cIdx] != ComputeControl.CellFlag.None || cellFlags[neighborCellIndex] != ComputeControl.CellFlag.None)
				{
					input = localCoordinates.RotateThroughFace(input, neighborFaceIndex, 1 - faceOrientationTerm);
				}
			}
			else if (boundaryVelocityMap != null && !double.IsNaN(boundaryVelocityMap[neighborFaceIndex].X))
			{
				input = boundaryVelocityMap[neighborFaceIndex];
				if (cellFlags[cIdx] != ComputeControl.CellFlag.None)
				{
					input = RotateCartesianToCell(input, faceNormals[neighborFaceIndex], localFaceNormals[neighborFaceIndex, faceOrientationTerm]);
				}
			}
			else
			{
				Geospatial.Vectors.Vector vector = ((cellFlags[cIdx] == ComputeControl.CellFlag.None) ? faceNormals[neighborFaceIndex] : localFaceNormals[neighborFaceIndex, faceOrientationTerm]);
				float computed = (float)faceOrientationFaceVelocityTerm;
				input = new Geospatial.Vectors.Vector((float)vector.X * computed, (float)vector.Y * computed);
			}
			double faceAreaNeighborFaceIndexTerm = faceArea[neighborFaceIndex] * faceOrientationFaceVelocityTerm;
			xComponent += faceAreaNeighborFaceIndexTerm * input.X;
			yComponent += faceAreaNeighborFaceIndexTerm * input.Y;
			implicitVariable2 -= faceAreaNeighborFaceIndexTerm;
		}
		if (sourceFlow > 0.0)
		{
			xComponent -= sourceMomentum.X;
			yComponent -= sourceMomentum.Y;
			implicitVariable2 += sourceFlow;
		}
		explicitVariable = new Vector2((float)xComponent, (float)yComponent);
		implicitVariable = (float)implicitVariable2;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected static void ComputeFaceAdvectionFromCellAccumulations(int fIdx, int cellA, int cellB, double faceHbarInverse, double faceVelocity, double theta, ReadOnlySpan<Vector2> cellExplicitVariables, ReadOnlySpan<float> cellImplicitVariables, Geospatial.Vectors.Vector[] faceNormals, Geospatial.Vectors.Vector[,] localFaceNormals, ComputeControl.CellFlag[] cellFlags, FaceAB[] faceAlphaCellAInv, out double explicitAdvection, out double implicitAdvection)
	{
		if (cellB < 0)
		{
			explicitAdvection = 0.0;
			implicitAdvection = 0.0;
			return;
		}
		double projectCellVariable = ProjectCellVariable(cellExplicitVariables[cellA], fIdx, 0, faceNormals, localFaceNormals, cellFlags[cellA]);
		double projectCellVariable2 = ProjectCellVariable(cellExplicitVariables[cellB], fIdx, 1, faceNormals, localFaceNormals, cellFlags[cellB]);
		FaceAB faceAB = faceAlphaCellAInv[fIdx];
		double valueAFaceABTerm = faceAB.ValueA * (double)cellImplicitVariables[cellA] + faceAB.ValueB * (double)cellImplicitVariables[cellB];
		double valueAFaceABTerm2 = faceAB.ValueA * projectCellVariable + faceAB.ValueB * projectCellVariable2;
		explicitAdvection = (valueAFaceABTerm2 + (1.0 - theta) * faceVelocity * valueAFaceABTerm) * faceHbarInverse;
		implicitAdvection = theta * valueAFaceABTerm * faceHbarInverse;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double ProjectCellVariable(Vector2 variable, int fIdx, int side, Geospatial.Vectors.Vector[] faceNormals, Geospatial.Vectors.Vector[,] localFaceNormals, ComputeControl.CellFlag cellFlag)
	{
		Geospatial.Vectors.Vector vector = ((cellFlag == ComputeControl.CellFlag.None) ? faceNormals[fIdx] : localFaceNormals[fIdx, side]);
		return (double)variable.X * vector.X + (double)variable.Y * vector.Y;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Geospatial.Vectors.Vector RotateCartesianToCell(Geospatial.Vectors.Vector input, Geospatial.Vectors.Vector cartesianNormal, Geospatial.Vectors.Vector cellNormal)
	{
		double dotProduct = cartesianNormal.X * input.X + cartesianNormal.Y * input.Y;
		double cartesianNormalInputTerm = (0.0 - cartesianNormal.Y) * input.X + cartesianNormal.X * input.Y;
		return new Geospatial.Vectors.Vector(cellNormal.X * dotProduct - cellNormal.Y * cartesianNormalInputTerm, cellNormal.Y * dotProduct + cellNormal.X * cartesianNormalInputTerm);
	}

	protected void PrepareTurbulenceState()
	{
		if (!Control.Turbulence.IsActive)
		{
			return;
		}
		MeshWaterState waterState = _waterState;
		bool hasTurb = waterState.turb != null;
		if (hasTurb)
		{
			waterState.turb.PrepareForControl(_mesh, Control);
		}
		else
		{
			waterState.turb = new TurbulenceState(_mesh, Control);
		}
		_oldState.turb = waterState.turb;
		if (waterState.turb.IsTransported)
		{
			_turbulence = new TurbulenceSolver(_mesh, Control, waterState.turb);
			if (hasTurb)
			{
				_turbulence.PrepareFromState(this);
			}
			else
			{
				_turbulence.Prepare(this);
			}
		}
	}

	protected void ComputeTurbulentMixing(MeshWaterState ws, List<int> faceIndexes, List<int> cellIndexes, Vector2[] cellDiffusion, float[] cellFactors)
	{
		if (Control.Turbulence.IsActive && ws.turb != null)
		{
			if (ws.turb.cellVelGrad != null)
			{
				ws.turb.ComputeCellVelGradients(ws, cellIndexes, threadFor);
			}
			ws.turb.ComputeFaceTurbEddyVisALL(ws, faceIndexes, cellIndexes, threadFor);
			if (cellDiffusion != null)
			{
				ComputeCellTurbulentMixing(ws, cellIndexes, cellDiffusion, cellFactors);
			}
		}
	}

	protected void ComputeCellTurbulentMixing(MeshWaterState ws, List<int> cellIndexes, Vector2[] cellDiffusion, float[] cellFactors)
	{
		threadFor.Run(cellIndexes.Count, (int start, int stop) =>
		{
			ComputeCellTurbulentMixingStartStop(ws, cellIndexes, cellDiffusion, cellFactors, start, stop);
		});
	}

	protected internal void ComputeCellTurbulentMixingStartStop(MeshWaterState ws, List<int> cellIndexes, Vector2[] cellDiffusion, float[] cellFactors, int start, int stop)
	{
		ReadOnlySpan<double> faceDistancesInverse = FaceInverseDistances;
		ReadOnlySpan<Geospatial.Vectors.Vector> cellVelocity = ws.CellVelocity;
		ReadOnlySpan<double> faceArea = ws.FaceArea;
		ReadOnlySpan<float> faceEddyViscosity = ws.FaceNuH;
		Span<Vector2> cellDiffusionSpan = cellDiffusion;
		ReadOnlySpan<int> neighborCells = _meshConnectivity.CellNeighborCells;
		ReadOnlySpan<int> neighborFaces = _meshConnectivity.CellNeighborFaces;
		ReadOnlySpan<int> offsets = _meshConnectivity.CellNeighborOffsets;
		ReadOnlySpan<GradientUVXY> cellVelocityGradient = ws.turb?.cellVelGrad;
		ReadOnlySpan<int> faceCellA = _meshConnectivity.FaceCellA;
		FaceAB[] faceAlpha = _hmesh.FaceAlpha;
		Vector2[] faceNormalF = _hmesh.FaceNormalF;
		ReadOnlySpan<int> cellIndexesSpan = CollectionsMarshal.AsSpan(cellIndexes);
		for (int i = start; i <= stop; i++)
		{
			int cellIndex = cellIndexesSpan[i];
			float factor = ((cellFactors == null) ? 1f : cellFactors[cellIndex]);
			ComputeCellTurbulentMixingOne(cellIndex, offsets, neighborFaces, neighborCells, faceArea, faceEddyViscosity, cellVelocity, faceDistancesInverse, cellVelocityGradient, faceCellA, faceAlpha, faceNormalF, factor, out cellDiffusionSpan[cellIndex]);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ComputeCellTurbulentMixingOne(int cellIndex, ReadOnlySpan<int> offsets, ReadOnlySpan<int> neighborFaces, ReadOnlySpan<int> neighborCells, ReadOnlySpan<double> faceArea, ReadOnlySpan<float> faceEddyViscosity, ReadOnlySpan<Geospatial.Vectors.Vector> cellVelocity, ReadOnlySpan<double> faceDistancesInverse, ReadOnlySpan<GradientUVXY> cellVelocityGradient, ReadOnlySpan<int> faceCellA, FaceAB[] faceAlpha, Vector2[] faceNormals, float factor, out Vector2 cellDiffusion)
	{
		bool notIsEmpty = !cellVelocityGradient.IsEmpty;
		int offsetStart = offsets[cellIndex];
		int offsetEnd = offsets[cellIndex + 1];
		double xComponent = 0.0;
		double yComponent = 0.0;
		Geospatial.Vectors.Vector vector = cellVelocity[cellIndex];
		for (int i = offsetStart; i < offsetEnd; i++)
		{
			int neighborCellIndex = neighborCells[i];
			if (neighborCellIndex < 0)
			{
				continue;
			}
			int neighborFaceIndex = neighborFaces[i];
			double faceEddyViscosity2 = faceEddyViscosity[neighborFaceIndex];
			if (faceEddyViscosity2 == 0.0)
			{
				continue;
			}
			double faceArea2 = faceArea[neighborFaceIndex];
			if (!(faceArea2 <= 0.0))
			{
				Geospatial.Vectors.Vector vector2 = cellVelocity[neighborCellIndex];
				double faceEddyViscosity2FaceArea2Term = faceEddyViscosity2 * faceArea2 * faceDistancesInverse[neighborFaceIndex];
				xComponent += faceEddyViscosity2FaceArea2Term * (vector2.X - vector.X);
				yComponent += faceEddyViscosity2FaceArea2Term * (vector2.Y - vector.Y);
				if (notIsEmpty)
				{
					bool sign = faceCellA[neighborFaceIndex] == cellIndex;
					GradientUVXY gradientUVXY = cellVelocityGradient[sign ? cellIndex : neighborCellIndex];
					GradientUVXY gradientUVXY2 = cellVelocityGradient[sign ? neighborCellIndex : cellIndex];
					FaceAB faceAB = faceAlpha[neighborFaceIndex];
					double valueAFaceABTerm = faceAB.ValueA * gradientUVXY.DU_DX + faceAB.ValueB * gradientUVXY2.DU_DX;
					double valueAFaceABTerm2 = faceAB.ValueA * gradientUVXY.DU_DY + faceAB.ValueB * gradientUVXY2.DU_DY;
					double valueAFaceABTerm3 = faceAB.ValueA * gradientUVXY.DV_DX + faceAB.ValueB * gradientUVXY2.DV_DX;
					double valueAFaceABTerm4 = faceAB.ValueA * gradientUVXY.DV_DY + faceAB.ValueB * gradientUVXY2.DV_DY;
					Vector2 vector3 = faceNormals[neighborFaceIndex];
					float signTerm = (sign ? 1f : (-1f));
					double vector3Term = signTerm * vector3.X;
					double vector3Term2 = signTerm * vector3.Y;
					double faceEddyViscosity2TimesFaceArea2 = faceEddyViscosity2 * faceArea2;
					xComponent += faceEddyViscosity2TimesFaceArea2 * (valueAFaceABTerm * vector3Term + valueAFaceABTerm3 * vector3Term2);
					yComponent += faceEddyViscosity2TimesFaceArea2 * (valueAFaceABTerm2 * vector3Term + valueAFaceABTerm4 * vector3Term2);
				}
			}
		}
		xComponent *= (double)factor;
		yComponent *= (double)factor;
		cellDiffusion = new Vector2((float)xComponent, (float)yComponent);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected internal static float ComputeFaceTurbulentMixing(int cellA, int cellB, float faceDepthInverse, Vector2 faceNormal, FaceAB faceAlphaCellAreaInverse, ReadOnlySpan<Vector2> cellDiffusion)
	{
		if (cellB < 0)
		{
			return 0f;
		}
		Vector2 cellDiffusion2 = cellDiffusion[cellA];
		Vector2 cellDiffusion3 = cellDiffusion[cellB];
		return ((float)faceAlphaCellAreaInverse.ValueA * Vector2.Dot(cellDiffusion2, faceNormal) + (float)faceAlphaCellAreaInverse.ValueB * Vector2.Dot(cellDiffusion3, faceNormal)) * faceDepthInverse;
	}

	public Solver(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState, SolverControl control, SolverConvergence convergence)
		: this(mesh, props, initialState, control, convergence, null)
	{
	}

	public Solver(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState, SolverControl control, SolverConvergence convergence, ComputeDriver driver)
	{
		_mesh = mesh;
		_hmesh = mesh.MetadataCache;
		_propertyTables = props;
		CellFlags = new ComputeControl.CellFlag[_mesh.CellCount];
		Control = control;
		if (driver != null)
		{
			_computeDriver = driver;
		}
		else
		{
			_computeDriver = ComputeDriver.SingleSolverFactory(this, props, control.computeControl);
		}
		if (_computeDriver.IdentityMapping)
		{
			BoundaryConditions = _computeDriver.BoundaryConditions;
			HydraulicStructures = _computeDriver.HydraulicStructures;
		}
		else
		{
			BoundaryConditions = new BoundaryConditionCollection(_mesh);
			HydraulicStructures = new HydraulicStructureCollection();
		}
		checker = new StepChecker(Control);
		_waterState = initialState.Clone();
		Control.AllocateExtraStateFields(_mesh, _waterState);
		_waterState.SetPropertyTables(_propertyTables);
		_waterState.dtMicros = 0L;
		_waterState.tMicros = 0L;
		ClearTickers();
	}

	public void ClearTickers()
	{
		ticks = new long[Enum.GetNames(typeof(Ticker)).Length];
	}

	public bool SetInitialStageFromBC()
	{
		if (StageBoundaryConditions.Count() == 0)
		{
			return false;
		}
		double valueAtTime = StageBoundaryConditions[0].ValueAtTime(0.0);
		_waterState.SetConstantDepth(valueAtTime, valueAtTime);
		return true;
	}

	public void ReportProgressToLog()
	{
		Reporter = new ProgressReporter(new LoggerProgressTarget(_logger));
	}

	public void SetPrincipalState(double[] cellWaterSurface, double[] cellVolume, double[] faceVelocity)
	{
		MeshWaterState waterState = _waterState;
		waterState.InvalidateCellWaterSurfaceGradients();
		cellWaterSurface.CopyTo(waterState.CellElevation, 0);
		cellVolume.CopyTo(waterState.CellVolume, 0);
		faceVelocity.CopyTo(waterState.FaceVelocity, 0);
	}

	public virtual void Prepare(ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		pr.ReportProgress(0);
		_meshConnectivity = new MeshConnectivity(_mesh);
		switch (Control.parallelMethod)
		{
		case SolverControl.ParallelMethod.THREAD_FOR:
			threadFor = new ThreadFor(Control.numCores);
			break;
		case SolverControl.ParallelMethod.PARALLEL_FOR:
			threadFor = new ThreadFor(Control.numCores, useSmartFor: true);
			break;
		}
		retryCount = 0;
		stepLimitationCount = new int[1 + Enum.GetValues(typeof(StepLimitation)).Cast<int>().Max()];
		rejectCounts = new int[1 + Enum.GetValues(typeof(StepChecker.RejectReason)).Cast<int>().Max()];
		if (checker != null)
		{
			checker.threadFor = threadFor;
		}
		_ = _hmesh.FaceSimpleLengths;
		newCellVolume = new double[_mesh.CellCount];
		PreparePropertyTables();
		_waterState.tMicros = 0L;
		int ordinal = 0;
		int ordinal2 = 0;
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			if ((CellFlags[i] & ComputeControl.CellFlag.StreamwiseCoordinates) != 0)
			{
				if (_mesh.Cells[i].Faces.Span.Length != 4)
				{
					ordinal2++;
					CellFlags[i] &= ~ComputeControl.CellFlag.StreamwiseCoordinates;
				}
				else
				{
					ordinal++;
				}
			}
		}
		_logger.LogDebug("{streamwiseCount} cells with streamwise coordinates enabled. Disabled streamwise for {streamwiseOverrideCount} non-quads.", ordinal, ordinal2);
		PrepareFaceFlags();
		PrepareIndexLists();
		_waterState.UpdateCells(null, ILCellAll, threadFor);
		SetBoundaryFaceWaterSurfaces(_waterState, Time, 0.0);
		if (Control.faceElevationMethod == FaceElevationMethod.Frozen)
		{
			_waterState.UpdateFacesProperties(ILFaceAll);
		}
		else
		{
			UpdateFaceState(_waterState, ILFaceAll);
		}
		_waterState.UpdateFaceFlowFromVelocityArea();
		PrepareFlowBoundaryConditions(pr);
		OverwriteBoundaryFaceFlows(_waterState, Time, 0.0);
		BoundaryConditions.Prepare(computeControl.LToSI);
		HydraulicStructures.Prepare(computeControl, Control, _mesh, _propertyTables, pr);
		if (pr.CancelRequested)
		{
			return;
		}
		PrepareNonOrthogonalCorrection();
		CellWaterSources = new CellWaterSourceLedger(this);
		CellWaterSources.RefreshCurrentRates();
		_waterState.InitializeSources(HydraulicStructures.SourceSinks.Count);
		foreach (MassConservationCheck massConservationCheck in MassConservationChecks)
		{
			massConservationCheck.Initialize(_waterState);
		}
		_oldState = _waterState.Clone();
		pr.ReportProgress(100);
	}

	private void PrepareNonOrthogonalCorrection()
	{
		_faceUsesNonOrthogonalCorrection = null;
		switch (Control.faceNormalGradientMethod)
		{
		case FaceNormalGradientMethod.TwoPoint:
			return;
		case FaceNormalGradientMethod.OverRelaxation:
			if (Control.gradientMethod != SolverControl.GradientMethod.PerpDistance && Control.gradientMethod != SolverControl.GradientMethod.LocalCoordinates)
			{
				Reporter.ReportWarning($"{"OverRelaxation"} correction requires {"PerpDistance"} or {"LocalCoordinates"} gradients. Disabling the correction for {Control.gradientMethod} gradients.");
				Control.faceNormalGradientMethod = FaceNormalGradientMethod.TwoPoint;
				return;
			}
			break;
		}
		int faceCount = _mesh.FaceCount;
		_faceUsesNonOrthogonalCorrection = new bool[faceCount];
		for (int i = 0; i < faceCount; i++)
		{
			bool faceUsesNonOrthogonalCorrection = _meshConnectivity.FaceCellB[i] >= 0 && FaceFlags[i] == FaceFlag.COMPUTE_FLOW;
			_faceUsesNonOrthogonalCorrection[i] = faceUsesNonOrthogonalCorrection;
		}
		MarkStageBoundaryFaces(StageBoundaryConditions);
		MarkStageBoundaryFaces(VariableStageBoundaryConditions);
		MarkStageBoundaryFaces(SpatiallyVariableStageVelocityBoundaryConditions);
		foreach (HydraulicStructure allHydraulicStructure in HydraulicStructures.AllHydraulicStructures)
		{
			int[] faces = allHydraulicStructure.Faces;
			foreach (int faceIndex in faces)
			{
				_faceUsesNonOrthogonalCorrection[faceIndex] = false;
			}
		}
		void MarkStageBoundaryFaces(IEnumerable<BoundaryCondition> conditions)
		{
			foreach (BoundaryCondition condition in conditions)
			{
				int[] faces2 = condition.Faces;
				foreach (int faces22 in faces2)
				{
					_faceUsesNonOrthogonalCorrection[faces22] = true;
				}
			}
		}
	}

	protected bool FaceUsesNonOrthogonalCorrection(int fIdx)
	{
		if (_faceUsesNonOrthogonalCorrection != null)
		{
			return _faceUsesNonOrthogonalCorrection[fIdx];
		}
		return false;
	}

	protected void UpdateFaceGradientData(MeshWaterState state)
	{
		if (Control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased)
		{
			state.UpdateNodeFaceWaterSurfaceValues(threadFor);
		}
		else
		{
			state.UpdateCellWaterSurfaceGradients(threadFor, Control.multidimensionalSlopeLimiter);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected double ComputeFaceWaterSurfaceCorrection(int fIdx, MeshWaterState state)
	{
		if (!FaceUsesNonOrthogonalCorrection(fIdx))
		{
			return 0.0;
		}
		FaceNormalGradientMethod faceNormalGradientMethod = Control.faceNormalGradientMethod;
		Geospatial.Vectors.Vector[] cellGradients = ((faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased) ? null : state.CellWaterSurfaceGradients);
		double[] nodeFaceValues = ((faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased) ? state.NodeFaceWaterSurfaceValues : null);
		HydraulicMeshComputer.NodeFaceInterpolationWorkspace nodeInterpolation = ((faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased) ? state.NodeFaceWaterSurfaceInterpolation : null);
		if (_meshConnectivity.FaceCellB[fIdx] < 0)
		{
			return state.WaterSurfaceBoundaryReconstruction?.FaceNormalGradientCorrection(fIdx, faceNormalGradientMethod, cellGradients, nodeFaceValues, nodeInterpolation, FaceInverseDistances[fIdx]) ?? 0.0;
		}
		return _hmesh.FaceComputes.FaceNormalGradientCorrection(faceNormalGradientMethod, fIdx, cellGradients, nodeFaceValues, nodeInterpolation, FaceInverseDistances[fIdx]);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected double ComputeFaceWaterSurfaceTangentialGradient(int fIdx, MeshWaterState state)
	{
		FaceNormalGradientMethod faceNormalGradientMethod = Control.faceNormalGradientMethod;
		Geospatial.Vectors.Vector[] cellGradients = ((faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased) ? null : state.CellWaterSurfaceGradients);
		double[] nodeFaceValues = ((faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased) ? state.NodeFaceWaterSurfaceValues : null);
		HydraulicMeshComputer.NodeFaceInterpolationWorkspace nodeInterpolation = ((faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased) ? state.NodeFaceWaterSurfaceInterpolation : null);
		return _hmesh.FaceComputes.FaceTangentialGradient(faceNormalGradientMethod, fIdx, cellGradients, nodeFaceValues, nodeInterpolation);
	}

	protected bool CompactFaceValuesAreValid(int fIdx, MeshWaterState state)
	{
		if (Control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased)
		{
			return _hmesh.FaceComputes.CompactFaceValuesAreValid(fIdx, state.NodeFaceWaterSurfaceValues, state.NodeFaceWaterSurfaceInterpolation);
		}
		return true;
	}

	protected virtual void PrepareFlowBoundaryConditions(ProgressReporter pr)
	{
		int denominator = FlowBoundaryConditions.Count + RatingCurveBoundaryConditions.Count;
		int ordinal = 0;
		foreach (BoundaryCondition flowBoundaryCondition in FlowBoundaryConditions)
		{
			PrepareFlowLikeBoundaryCondition(pr, flowBoundaryCondition);
			pr.ReportProgressFraction(++ordinal, denominator);
		}
		foreach (BoundaryCondition ratingCurveBoundaryCondition in RatingCurveBoundaryConditions)
		{
			PrepareFlowLikeBoundaryCondition(pr, ratingCurveBoundaryCondition);
			pr.ReportProgressFraction(++ordinal, denominator);
		}
	}

	private void PrepareFlowLikeBoundaryCondition(ProgressReporter pr, BoundaryCondition bc)
	{
		if (bc.FaceApportionment == null)
		{
			bc.FaceApportionment = CreateDefaultFaceApportionment(bc);
		}
		bc.FaceApportionment.Prepare(pr, bc, _mesh, _waterState, GetFaceConveyanceFunc());
	}

	private IFaceApportionment CreateDefaultFaceApportionment(BoundaryCondition bc)
	{
		if (bc.UseLateralDistribution)
		{
			return new LDMFaceFractionsApportionment(_propertyTables, computeControl.Units, 0.01 / Control.LToSI, 0.01, Control.Turbulence);
		}
		return new FaceFractionsApportionment(_propertyTables, 0.01 / Control.LToSI, 0.01);
	}

	protected abstract Func<MeshWaterState, int, double> GetFaceConveyanceFunc();

	public virtual void PreparePropertyTables()
	{
		_propertyTables.UpdateFaceMeanElevation();
		TurbulenceParams turbulence = Control.Turbulence;
		bool longMixCoeffTurbulenceTerm = turbulence.LongMixCoeff > 0.0 || turbulence.TransMixCoeff > 0.0 || turbulence.Model != TurbulenceModel.Smagorinsky;
		if ((Control.equationSet == SolverControl.EquationSet.SWE && turbulence.IsActive) & longMixCoeffTurbulenceTerm)
		{
			_propertyTables.ComputeFaceShearFluxVarTable(_mesh, grav, nSItoUnits);
		}
	}

	public void PrepareFaceFlags()
	{
		List<int> intList = new List<int>();
		List<int> intListList = new List<int>();
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			Face face = _mesh.Faces[i];
			bool condition = false;
			bool condition2 = false;
			bool cellBIsNegative = face.cellB < 0;
			foreach (BoundaryCondition flowBoundaryCondition in FlowBoundaryConditions)
			{
				condition |= flowBoundaryCondition.Faces.Contains(i);
			}
			foreach (BoundaryCondition stageBoundaryCondition in StageBoundaryConditions)
			{
				condition2 |= stageBoundaryCondition.Faces.Contains(i);
			}
			foreach (BoundaryCondition variableFlowBoundaryCondition in VariableFlowBoundaryConditions)
			{
				condition |= variableFlowBoundaryCondition.Faces.Contains(i);
			}
			foreach (BoundaryCondition ratingCurveBoundaryCondition in RatingCurveBoundaryConditions)
			{
				condition |= ratingCurveBoundaryCondition.Faces.Contains(i);
			}
			foreach (BoundaryCondition variableStageBoundaryCondition in VariableStageBoundaryConditions)
			{
				condition2 |= variableStageBoundaryCondition.Faces.Contains(i);
			}
			foreach (BoundaryCondition spatiallyVariableStageVelocityBoundaryCondition in SpatiallyVariableStageVelocityBoundaryConditions)
			{
				condition2 |= spatiallyVariableStageVelocityBoundaryCondition.Faces.Contains(i);
			}
			foreach (BoundaryCondition normalDepthBoundaryCondition in NormalDepthBoundaryConditions)
			{
				condition |= normalDepthBoundaryCondition.Faces.Contains(i);
			}
			foreach (HydraulicStructure allHydraulicStructure in HydraulicStructures.AllHydraulicStructures)
			{
				if (allHydraulicStructure.TreatFlowAsInternalBoundary)
				{
					condition |= allHydraulicStructure.Faces.Contains(i);
				}
			}
			foreach (Gate gate in HydraulicStructures.Gates)
			{
				condition |= gate.Faces.Contains(i);
			}
			if (!cellBIsNegative | condition2 | condition)
			{
				intListList.Add(i);
				if (!condition)
				{
					intList.Add(i);
				}
			}
		}
		FaceFlags = new FaceFlag[_mesh.FaceCount];
		foreach (int except in intListList.Except(intList))
		{
			FaceFlags[except] |= FaceFlag.FORCE_FLOW;
		}
		foreach (int ordinal in intList)
		{
			FaceFlags[ordinal] |= FaceFlag.COMPUTE_FLOW;
		}
	}

	public void PrepareIndexLists()
	{
		ILCellAll = new List<int>();
		ILFaceAll = new List<int>();
		ILFaceAllVelocity = new List<int>();
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			ILCellAll.Add(i);
		}
		for (int j = 0; j < _mesh.FaceCount; j++)
		{
			ILFaceAll.Add(j);
			if ((FaceFlags[j] & FaceFlag.HAS_FLOW) != 0)
			{
				ILFaceAllVelocity.Add(j);
			}
		}
		ILCellVolume = ILCellAll;
		ILFaceVelocity = ILFaceAllVelocity;
		ILFaceVelFrict = ILFaceAll;
		ILCellVelocity = ILCellAll;
	}

	public OperationResult Solve(double dt, double duration)
	{
		Control.dtMicros = ComputeDriver.SecondsToMicros(dt);
		return Solve(duration);
	}

	public OperationResult Solve(double duration)
	{
		return _computeDriver.Solve(duration);
	}

	public OperationResult Solve()
	{
		Prepare(Reporter);
		if (Reporter.CancelRequested)
		{
			return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Compute"));
		}
		OperationResult result = SolveSteps();
		Cleanup();
		return result;
	}

	public OperationResult SolveSteps(double duration)
	{
		return _computeDriver.SolveSteps(duration);
	}

	public OperationResult SolveSteps()
	{
		return _computeDriver.SolveSteps();
	}

	public virtual void Cleanup()
	{
		_turbulence?.Cleanup();
		_secondaryFlow?.Cleanup();
		threadFor?.Dispose();
		threadFor = new ThreadFor(1);
		checker.threadFor = threadFor;
		Trace.WriteLine("Solver Timers (ms):");
		Ticker[] values = Enum.GetValues<Ticker>();
		foreach (Ticker ticker in values)
		{
			int ticker2 = (int)ticker;
			if (ticker2 < ticks.Length)
			{
				long ticksTicker2Term = ticks[ticker2] / 10000;
				if (ticksTicker2Term > 0)
				{
					Trace.WriteLine($"{ticker}   " + ticksTicker2Term);
				}
				continue;
			}
			break;
		}
	}

	public virtual void CheckpointState()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		_oldState.UpdateFrom(_waterState, threadFor);
		ticks[14] += stopwatch.ElapsedTicks;
	}

	public virtual void RevertState()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		_logger.LogTrace("Revert state {Reason}", checker.reason);
		rejectCounts[(int)checker.reason]++;
		_waterState.UpdateFrom(_oldState, threadFor);
		CellWaterSources?.RevertStep();
		RollbackConservation();
		ticks[14] += stopwatch.ElapsedTicks;
	}

	public void CommitState()
	{
		CommitConservation();
		if (_lastHydraulicStepSucceeded)
		{
			CellWaterSources?.CommitStep(_attemptDuration);
			AdvanceTurbulence();
			AdvanceSecondaryFlow();
		}
	}

	private void AdvanceSecondaryFlow()
	{
		if (_secondaryFlow == null)
		{
			return;
		}
		double microsToSeconds = ComputeDriver.MicrosToSeconds(_waterState.dtMicros);
		if (!(microsToSeconds <= 0.0))
		{
			double absTime = ComputeDriver.MicrosToSeconds(_waterState.tMicros - _waterState.dtMicros);
			if (_secondaryFlow.SolveStep(absTime, microsToSeconds, out var failureCell) != ITransport.Status.OK)
			{
				_logger.LogWarning("Secondary-flow transport failed at cell {Cell}; dispersion is stale", failureCell);
			}
		}
	}

	private void AdvanceTurbulence()
	{
		if (_turbulence == null)
		{
			return;
		}
		double microsToSeconds = ComputeDriver.MicrosToSeconds(_waterState.dtMicros);
		if (!(microsToSeconds <= 0.0))
		{
			double absTime = ComputeDriver.MicrosToSeconds(_waterState.tMicros - _waterState.dtMicros);
			if (_turbulence.SolveStep(absTime, microsToSeconds, out var failureCell) != ITransport.Status.OK)
			{
				_logger.LogWarning("Turbulence step failed at cell {Cell}; eddy viscosity is stale", failureCell);
			}
		}
	}

	public void ReportState(OperationResult opResult, bool userTimestep)
	{
		ComputeStepDone?.Invoke(new MeshTimestep(_waterState, this));
		if (userTimestep || !opResult.Result)
		{
			TimestepDone?.Invoke(new MeshTimestep(_waterState, this));
		}
	}

	public void DumpToJson(string filename = "C:\\Temp\\cmd.json")
	{
		SlidingMesh mesh = new SlidingMesh(_mesh);
		using FileStream utf8Json = File.Create(filename);
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		};
		double[] doubleValues = new double[_mesh.CellCount];
		double[] doubleValuesArray = new double[_mesh.CellCount];
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			doubleValues[i] = _waterState.CellVelocity[i].X;
			doubleValuesArray[i] = _waterState.CellVelocity[i].Y;
		}
		List<object> localValue = new List<object>
		{
			new List<object> { "SET", "g_raw", mesh },
			new List<object> { "SET", "cellElevation_raw", _waterState.CellElevation },
			new List<object> { "SET", "cellDtMax_raw", CellDtMax },
			new List<object> { "SET", "cellU_raw", doubleValues },
			new List<object> { "SET", "cellV_raw", doubleValuesArray },
			new List<object> { "SET", "faceVelocity_raw", _waterState.FaceVelocity },
			new List<object> { "PYTHON", "\r\ng=json_to_mesh(g_raw)\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\ng.plot_edges(ax = ax, color = 'k', subedges='smoothStringXY', lw=0.5, alpha=0.5)\r\ndtmax = np.array(cellDtMax_raw)\r\n#g.plot_cells(ax = ax, values=dtmax, subedges='smoothStringXY', cmap='turbo')\r\n" }
		};
		JsonSerializer.Serialize(utf8Json, localValue, options);
	}

	public double MaximumTimestep(out StepLimitation stepLimitation, out int limitingCell, out int limitingFace)
	{
		double result = ComputeMaxTimestep(_waterState);
		stepLimitation = this.stepLimitation;
		limitingCell = this.limitingCell;
		limitingFace = this.limitingFace;
		return result;
	}

	public abstract double ComputeMaxTimestep(MeshWaterState ws);

	public void ApplyBoundaryConditions(double absTime, double dt)
	{
		SetBoundaryFaceWaterSurfaces(_waterState, absTime, dt);
		OverwriteBoundaryFaceFlows(_waterState, absTime, dt);
		double absTime2 = ((Control.timeStepping == SolverControl.TimeStepping.Explicit) ? absTime : (absTime + dt));
		BoundaryConditions.UpdateStepAverage(absTime2, FaceInverseDistances, WaterState.CellVelocity);
		SetCellInternalFlows(_waterState, absTime, dt);
		CellWaterSources?.MarkCurrentRatesDirty();
	}

	public void SetCellInternalFlows(MeshWaterState ws, double absTime, double dt)
	{
		if (!BoundaryConditions.HasInternalFlows)
		{
			return;
		}
		Span<int> cellIndicesInternalFlows = BoundaryConditions.CellIndicesInternalFlows.AsSpan();
		Span<double> cellInternalFlows = BoundaryConditions.CellInternalFlows.AsSpan();
		Span<double> cellInternalFlowLimited = BoundaryConditions.CellInternalFlowLimited.AsSpan();
		int length = cellIndicesInternalFlows.Length;
		for (int i = 0; i < length; i++)
		{
			int index = cellIndicesInternalFlows[i];
			cellInternalFlows[index] = 0.0;
		}
		foreach (BoundaryCondition flowIntBoundaryCondition in FlowIntBoundaryConditions)
		{
			double[] cellValuesAtTime = flowIntBoundaryCondition.CellValuesAtTime(absTime);
			int[] cells = flowIntBoundaryCondition.Cells;
			for (int j = 0; j < cells.Length; j++)
			{
				BoundaryConditions.CellInternalFlows[cells[j]] += cellValuesAtTime[j];
			}
		}
		Cell[] cells2 = _mesh.Cells;
		Face[] faces = _mesh.Faces;
		for (int k = 0; k < length; k++)
		{
			int cellIndicesInternalFlow = cellIndicesInternalFlows[k];
			cellInternalFlowLimited[cellIndicesInternalFlow] = cellInternalFlows[cellIndicesInternalFlow];
			if (cellInternalFlows[cellIndicesInternalFlow] >= 0.0)
			{
				continue;
			}
			double volumeIncrement = ws.CellVolume[cellIndicesInternalFlow] / dt;
			double computed = 0.0;
			double computed2 = 0.0;
			Span<int> facesSpan = cells2[cellIndicesInternalFlow].Faces.Span;
			for (int l = 0; l < facesSpan.Length; l++)
			{
				int faceIndex = facesSpan[l];
				double cellIndicesInternalFlowCellBTerm = (double)((cellIndicesInternalFlow != faces[faceIndex].cellB) ? 1 : (-1)) * ws.FaceFlow[faceIndex];
				if (cellIndicesInternalFlowCellBTerm > 0.0)
				{
					computed += cellIndicesInternalFlowCellBTerm;
				}
				else
				{
					computed2 += cellIndicesInternalFlowCellBTerm;
				}
			}
			double computed3 = ((!(0.0 - computed2 < computed)) ? 0.5 : 2.0);
			double cellInternalFlowLimited2 = 0.0 - (volumeIncrement + computed3 * (computed2 - computed));
			cellInternalFlowLimited2 = Math.Min(cellInternalFlowLimited2, 0.0);
			if (cellInternalFlowLimited[cellIndicesInternalFlow] < cellInternalFlowLimited2)
			{
				cellInternalFlowLimited[cellIndicesInternalFlow] = cellInternalFlowLimited2;
			}
		}
	}

	public void ApplyHydraulicStructures(double absTime, double dt)
	{
		HydraulicStructures.Apply(absTime, dt, _waterState, _mesh, _propertyTables, _hmesh);
		OverwriteHydStructFaceFlows(_waterState);
		CellWaterSources?.MarkCurrentRatesDirty();
	}

	public ISolver.Status SolveStep(long tMicros, long dtMicros)
	{
		int rejectCell;
		return SolveStep(tMicros, dtMicros, 0L, out rejectCell);
	}

	public ISolver.Status SolveStep(long tMicros, long dtMicros, long dtMicrosPrevious, out int rejectCell)
	{
		rejectCell = -1;
		double absTime = ComputeDriver.MicrosToSeconds(tMicros);
		double attemptDuration = ComputeDriver.MicrosToSeconds(dtMicros);
		double dtPrevious = ComputeDriver.MicrosToSeconds(dtMicrosPrevious);
		_attemptDuration = attemptDuration;
		_lastHydraulicStepSucceeded = false;
		CellWaterSources?.BeginStep();
		BoundaryConditions.UpdateStepIntegrated(absTime, attemptDuration, _waterState.CellHydraulicDepth, _waterState.CellArea);
		CellWaterSources?.MarkCurrentRatesDirty();
		if (RefreshCellWaterSourcesAtStepStart)
		{
			CellWaterSources?.RefreshCurrentRates();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		ISolver.Status status = SolveStepInternal(absTime, attemptDuration, dtPrevious);
		ticks[0] += stopwatch.ElapsedTicks;
		_waterState.tMicros += dtMicros;
		_waterState.dtMicros = dtMicros;
		if (status == ISolver.Status.OK && Control.checkSteps)
		{
			stopwatch.Restart();
			if (!checker.CheckStep(_oldState, _waterState))
			{
				rejectCell = checker.rejectCell;
				switch (checker.reason)
				{
				case StepChecker.RejectReason.VOLUME_NEGATIVE:
					status = ISolver.Status.VOLUME_NEGATIVE;
					break;
				case StepChecker.RejectReason.ELEVATION_CHANGE:
					status = ISolver.Status.ELEVATION_CHANGE;
					break;
				case StepChecker.RejectReason.ELEVATION_NAN:
				case StepChecker.RejectReason.VELOCITY_NAN:
					status = ISolver.Status.NAN_STATE;
					break;
				case StepChecker.RejectReason.ERROR:
					status = ISolver.Status.ERROR;
					break;
				}
			}
			ticks[13] += stopwatch.ElapsedTicks;
		}
		_lastHydraulicStepSucceeded = status == ISolver.Status.OK;
		return status;
	}

	protected abstract ISolver.Status SolveStepInternal(double absTime, double dt, double dtPrevious);

	private void SetBoundaryFaceWaterSurfaces(MeshWaterState ws, double absTime, double dt)
	{
		double absTime2 = ((Control.timeStepping == SolverControl.TimeStepping.Explicit) ? absTime : (absTime + dt));
		ws.InitializePinnedFaceStages();
		HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace scalarBoundaryReconstructionWorkspace = ws.ResetWaterSurfaceBoundaryReconstruction();
		foreach (BoundaryCondition normalDepthBoundaryCondition in NormalDepthBoundaryConditions)
		{
			double outwardGradient = 0.0 - normalDepthBoundaryCondition.ValueAtTime(absTime2);
			int[] faces = normalDepthBoundaryCondition.Faces;
			foreach (int face in faces)
			{
				scalarBoundaryReconstructionWorkspace.SetNormalGradient(face, outwardGradient);
			}
		}
		foreach (BoundaryCondition stageBoundaryCondition in StageBoundaryConditions)
		{
			double pinnedFaceStage = stageBoundaryCondition.ValueAtTime(absTime2);
			for (int j = 0; j < stageBoundaryCondition.Faces.Length; j++)
			{
				ws.PinnedFaceStage[stageBoundaryCondition.Faces[j]] = pinnedFaceStage;
				scalarBoundaryReconstructionWorkspace.SetValue(stageBoundaryCondition.Faces[j], pinnedFaceStage);
			}
		}
		foreach (BoundaryCondition variableStageBoundaryCondition in VariableStageBoundaryConditions)
		{
			double[] faceValuesAtTime = variableStageBoundaryCondition.FaceValuesAtTime(absTime2);
			for (int k = 0; k < variableStageBoundaryCondition.Faces.Length; k++)
			{
				ws.PinnedFaceStage[variableStageBoundaryCondition.Faces[k]] = faceValuesAtTime[k];
				scalarBoundaryReconstructionWorkspace.SetValue(variableStageBoundaryCondition.Faces[k], faceValuesAtTime[k]);
			}
		}
		foreach (BoundaryCondition spatiallyVariableStageVelocityBoundaryCondition in SpatiallyVariableStageVelocityBoundaryConditions)
		{
			double[,] faceMultipleValuesAtTime = spatiallyVariableStageVelocityBoundaryCondition.FaceMultipleValuesAtTime(absTime2);
			for (int l = 0; l < spatiallyVariableStageVelocityBoundaryCondition.Faces.Length; l++)
			{
				int faceIndex = spatiallyVariableStageVelocityBoundaryCondition.Faces[l];
				ws.PinnedFaceStage[faceIndex] = faceMultipleValuesAtTime[l, 0];
				scalarBoundaryReconstructionWorkspace.SetValue(faceIndex, faceMultipleValuesAtTime[l, 0]);
			}
		}
	}

	protected void OverwriteBoundaryFaceFlows(MeshWaterState ws, double absTime, double dt)
	{
		ApplyFlowBoundaryConditions(ws, absTime, dt);
		ApplyRatingCurveBoundaryConditions(ws);
		double curTime = ((Control.timeStepping == SolverControl.TimeStepping.Explicit) ? absTime : (absTime + dt));
		ApplyNormalDepthBoundaryConditions(ws, curTime);
		ApplyVariableFlowBoundaryConditions(ws, absTime);
	}

	private void ApplyFlowBoundaryConditions(MeshWaterState ws, double absTime, double dt)
	{
		foreach (BoundaryCondition flowBoundaryCondition in FlowBoundaryConditions)
		{
			double averageValueOverIntervalTerm;
			if (computeControl.usePeriodAverageFlows)
			{
				averageValueOverIntervalTerm = flowBoundaryCondition.AverageValueOverInterval(absTime, dt);
			}
			else
			{
				double absTime2 = ((Control.timeStepping == SolverControl.TimeStepping.Explicit) ? absTime : (absTime + dt));
				averageValueOverIntervalTerm = flowBoundaryCondition.ValueAtTime(absTime2);
			}
			if (!double.IsFinite(averageValueOverIntervalTerm))
			{
				throw new Exception("Flow BC Error: BC Line " + flowBoundaryCondition.Name + " may not cover the time-window.");
			}
			if (averageValueOverIntervalTerm == 0.0)
			{
				for (int i = 0; i < flowBoundaryCondition.Faces.Length; i++)
				{
					int faceIndex = flowBoundaryCondition.Faces[i];
					ws.FaceFlow[faceIndex] = 0.0;
					ws.FaceVelocity[faceIndex] = 0.0;
				}
				continue;
			}
			if (flowBoundaryCondition.EGSlope < 1E-10)
			{
				throw new Exception($"Flow BC: EG slope {flowBoundaryCondition.EGSlope} is less than threshold 1e-10");
			}
			ApplyFlowBoundaryConditionEGSlope(flowBoundaryCondition, averageValueOverIntervalTerm, ws);
		}
	}

	private static string DescribeTimestepSpan(BoundaryCondition bc)
	{
		if (bc.Timesteps == null || bc.Timesteps.Length == 0)
		{
			return "(none)";
		}
		return $"{bc.Timesteps[0]}s to {bc.Timesteps[^1]}s";
	}

	private void ApplyVariableFlowBoundaryConditions(MeshWaterState ws, double absTime)
	{
		foreach (BoundaryCondition variableFlowBoundaryCondition in VariableFlowBoundaryConditions)
		{
			double[] faceValuesAtTime = variableFlowBoundaryCondition.FaceValuesAtTime(absTime);
			for (int i = 0; i < variableFlowBoundaryCondition.Faces.Length; i++)
			{
				int faceIndex = variableFlowBoundaryCondition.Faces[i];
				if (!double.IsFinite(faceValuesAtTime[i]))
				{
					throw new Exception("Variable Flow BC Error: Doesn't cover time-window.");
				}
				double faceArea = ws.FaceArea[faceIndex];
				if (faceArea > 0.0)
				{
					ws.FaceFlow[faceIndex] = faceValuesAtTime[i];
					ws.FaceVelocity[faceIndex] = faceValuesAtTime[i] / faceArea;
				}
			}
		}
	}

	private void ApplyRatingCurveBoundaryConditions(MeshWaterState ws)
	{
		foreach (BoundaryCondition ratingCurveBoundaryCondition in RatingCurveBoundaryConditions)
		{
			if (ratingCurveBoundaryCondition.FaceApportionment == null)
			{
				throw new SolverException("Rating Curve BC Error: FaceApportionment is not set.");
			}
			double boundaryAverageStage = GetBoundaryAverageStage(ws, ratingCurveBoundaryCondition);
			double flowFromStage = ratingCurveBoundaryCondition.GetFlowFromStage(boundaryAverageStage);
			if (!double.IsFinite(flowFromStage))
			{
				throw new SolverException("Rating Curve BC Error: stage-to-flow mapping returned non-finite flow.");
			}
			double qtot = 0.0 - flowFromStage;
			ratingCurveBoundaryCondition.FaceFractions = ratingCurveBoundaryCondition.FaceApportionment.GetFaceFractionsByStage(_mesh, ws, boundaryAverageStage, ratingCurveBoundaryCondition.FaceFractions);
			ApplyFlowBoundaryConditionWithFractions(ratingCurveBoundaryCondition, qtot, ws, setVelocity: true);
		}
	}

	private static double GetBoundaryAverageStage(MeshWaterState ws, BoundaryCondition bc)
	{
		int faceLength = bc.Faces.Length;
		if (faceLength == 0)
		{
			throw new SolverException("Rating Curve BC Error: No faces are associated with the boundary condition.");
		}
		double faceElevationSum = 0.0;
		double faceElevationSum2 = 0.0;
		int ordinal = 0;
		for (int i = 0; i < faceLength; i++)
		{
			int faceIndex = bc.Faces[i];
			double faceElevation = ws.FaceElevation[faceIndex];
			if (!double.IsFinite(faceElevation))
			{
				throw new SolverException("Rating Curve BC Error: Boundary face stage is not finite.");
			}
			faceElevationSum += faceElevation;
			if (ws.FaceArea[faceIndex] > 0.0)
			{
				faceElevationSum2 += faceElevation;
				ordinal++;
			}
		}
		if (ordinal > 0)
		{
			return faceElevationSum2 / (double)ordinal;
		}
		return faceElevationSum / (double)faceLength;
	}

	protected static void ApplyFlowBoundaryConditionWithFractions(BoundaryCondition bc, double Qtot, MeshWaterState ws, bool setVelocity)
	{
		if (bc.FaceFractions == null || bc.FaceFractions.Length != bc.Faces.Length)
		{
			throw new SolverException("Flow BC Error: Face fractions are missing or have incorrect length.");
		}
		if (Qtot == 0.0)
		{
			for (int i = 0; i < bc.Faces.Length; i++)
			{
				int faceIndex = bc.Faces[i];
				ws.FaceFlow[faceIndex] = 0.0;
				ws.FaceVelocity[faceIndex] = 0.0;
			}
			return;
		}
		for (int j = 0; j < bc.Faces.Length; j++)
		{
			int faceIndex2 = bc.Faces[j];
			double faceFraction = bc.FaceFractions[j];
			ws.FaceFlow[faceIndex2] = (0.0 - Qtot) * faceFraction;
			if (!setVelocity)
			{
				ws.FaceVelocity[faceIndex2] = 0.0;
				continue;
			}
			double faceArea = ws.FaceArea[faceIndex2];
			ws.FaceVelocity[faceIndex2] = ((faceArea > 0.0) ? ((float)(ws.FaceFlow[faceIndex2] / faceArea)) : 0f);
		}
	}

	protected abstract void ApplyFlowBoundaryConditionEGSlope(BoundaryCondition bc, double Qtot, MeshWaterState ws);

	protected void ApplyFlowBoundaryConditionByConveyance(BoundaryCondition bc, double Qtot, MeshWaterState ws, Func<MeshWaterState, int, double> FaceConveyanceFunc, double minConveyance, bool setVelocity, bool outflowUseVelocity = true)
	{
		if (Qtot == 0.0)
		{
			for (int i = 0; i < bc.Faces.Length; i++)
			{
				int faceIndex = bc.Faces[i];
				ws.FaceFlow[faceIndex] = 0.0;
				ws.FaceVelocity[faceIndex] = 0.0;
				bc.FaceFractions[i] = 1.0;
			}
			return;
		}
		if (Qtot > 0.0)
		{
			bc.FaceFractions = bc.FaceApportionment.GetFaceFractions(_mesh, ws, minConveyance, bc.FaceFractions);
			for (int j = 0; j < bc.Faces.Length; j++)
			{
				int faceIndex2 = bc.Faces[j];
				ws.FaceFlow[faceIndex2] = (0.0 - Qtot) * bc.FaceFractions[j];
				if (bc.FaceFractions[j] != 0.0)
				{
					if (setVelocity)
					{
						ws.FaceVelocity[faceIndex2] = ws.FaceFlow[faceIndex2] / Math.Max(ws.FaceArea[faceIndex2], 0.0001);
					}
					else
					{
						ws.FaceVelocity[faceIndex2] = 0.0;
					}
				}
				else
				{
					ws.FaceFlow[faceIndex2] = 0.0;
					ws.FaceVelocity[faceIndex2] = 0.0;
				}
			}
			return;
		}
		double computed = 0.0;
		double[] doubleValues = null;
		double faceConveyanceFuncSum = 0.0;
		if (outflowUseVelocity)
		{
			doubleValues = new double[bc.Faces.Length];
			for (int k = 0; k < bc.Faces.Length; k++)
			{
				int faceIndex3 = bc.Faces[k];
				double faceFractions = FaceConveyanceFunc(ws, faceIndex3);
				faceConveyanceFuncSum += faceFractions;
				bc.FaceFractions[k] = faceFractions;
				if (faceFractions == 0.0)
				{
					doubleValues[k] = 0.0;
					continue;
				}
				int cellA = Mesh.Faces[faceIndex3].cellA;
				Vector2 vector = _hmesh.FaceNormalF[faceIndex3];
				Geospatial.Vectors.Vector vector2 = ws.CellVelocity[cellA];
				doubleValues[k] = Math.Max(0.0, ws.FaceArea[faceIndex3] * ((double)vector.X * vector2.X + (double)vector.Y * vector2.Y));
				computed += doubleValues[k];
			}
			double qtotTerm = (0.0 - computed) / Qtot;
			if (faceConveyanceFuncSum <= 0.0)
			{
				for (int l = 0; l < bc.Faces.Length; l++)
				{
					int faceIndex4 = bc.Faces[l];
					ws.FaceFlow[faceIndex4] = 0.0;
					ws.FaceVelocity[faceIndex4] = 0.0;
					bc.FaceFractions[l] = 0.0;
				}
				return;
			}
			for (int m = 0; m < bc.Faces.Length; m++)
			{
				int faceIndex5 = bc.Faces[m];
				double computed2 = 0.1;
				if (qtotTerm <= computed2)
				{
					ws.FaceFlow[faceIndex5] = doubleValues[m] / computed2 + (0.0 - Qtot - computed / computed2) * bc.FaceFractions[m] / faceConveyanceFuncSum;
				}
				else
				{
					ws.FaceFlow[faceIndex5] = doubleValues[m] / qtotTerm;
				}
				bc.FaceFractions[m] = (0.0 - ws.FaceFlow[faceIndex5]) / Qtot;
				if (bc.FaceFractions[m] != 0.0)
				{
					if (setVelocity)
					{
						ws.FaceVelocity[faceIndex5] = ws.FaceFlow[faceIndex5] / Math.Max(ws.FaceArea[faceIndex5], 0.0001);
					}
					else
					{
						ws.FaceVelocity[faceIndex5] = 0.0;
					}
				}
				else
				{
					ws.FaceFlow[faceIndex5] = 0.0;
					ws.FaceVelocity[faceIndex5] = 0.0;
				}
			}
			return;
		}
		faceConveyanceFuncSum = 0.0;
		for (int n = 0; n < bc.Faces.Length; n++)
		{
			int faceIndex6 = bc.Faces[n];
			double faceFractions2 = FaceConveyanceFunc(ws, faceIndex6);
			faceConveyanceFuncSum += faceFractions2;
			bc.FaceFractions[n] = faceFractions2;
		}
		if (faceConveyanceFuncSum <= 0.0)
		{
			for (int intervalIndex = 0; intervalIndex < bc.Faces.Length; intervalIndex++)
			{
				int faceIndex7 = bc.Faces[intervalIndex];
				ws.FaceFlow[faceIndex7] = 0.0;
				ws.FaceVelocity[faceIndex7] = 0.0;
				bc.FaceFractions[intervalIndex] = 0.0;
			}
			return;
		}
		for (int intervalIndex2 = 0; intervalIndex2 < bc.Faces.Length; intervalIndex2++)
		{
			int faceIndex8 = bc.Faces[intervalIndex2];
			bc.FaceFractions[intervalIndex2] /= faceConveyanceFuncSum;
			ws.FaceFlow[faceIndex8] = (0.0 - Qtot) * bc.FaceFractions[intervalIndex2];
			if (bc.FaceFractions[intervalIndex2] != 0.0)
			{
				if (setVelocity)
				{
					ws.FaceVelocity[faceIndex8] = ws.FaceFlow[faceIndex8] / Math.Max(ws.FaceArea[faceIndex8], 0.0001);
				}
				else
				{
					ws.FaceVelocity[faceIndex8] = 0.0;
				}
			}
			else
			{
				ws.FaceFlow[faceIndex8] = 0.0;
				ws.FaceVelocity[faceIndex8] = 0.0;
			}
		}
	}

	protected void ApplyNormalDepthBoundaryConditions(MeshWaterState ws, double curTime)
	{
		double nSItoUnits2 = nSItoUnits;
		List<BoundaryCondition> normalDepthBoundaryConditions = NormalDepthBoundaryConditions;
		for (int i = 0; i < normalDepthBoundaryConditions.Count; i++)
		{
			BoundaryCondition boundaryCondition = normalDepthBoundaryConditions[i];
			double valueAtTime = boundaryCondition.ValueAtTime(curTime);
			if (!double.IsFinite(valueAtTime))
			{
				int face = (((boundaryCondition.Faces?.Length ?? 0) > 0) ? boundaryCondition.Faces[0] : (-1));
				throw new SolverException($"Normal depth boundary condition #{i + 1} of {normalDepthBoundaryConditions.Count} has a bad friction slope starting at time {curTime} s. Check your model along the BC line near Face[{face}]");
			}
			double valueAtTimeTerm = (double)Math.Sign(valueAtTime) * Math.Sqrt(Math.Abs(valueAtTime));
			for (int j = 0; j < boundaryCondition.Faces.Length; j++)
			{
				int fIdx = boundaryCondition.Faces[j];
				if (ws.FaceArea[fIdx] == 0.0)
				{
					ws.FaceVelocity[fIdx] = 0.0;
					ws.FaceFlow[fIdx] = 0.0;
				}
				else
				{
					double faceConveyanceByArea = _propertyTables.ComputeFaceConveyanceByArea(in fIdx, ws.FacePriorIndexes, in ws.FaceElevation[fIdx], nSItoUnits2);
					ws.FaceVelocity[fIdx] = valueAtTimeTerm * faceConveyanceByArea;
					ws.FaceFlow[fIdx] = ws.FaceArea[fIdx] * ws.FaceVelocity[fIdx];
				}
			}
		}
	}

	private void OverwriteHydStructFaceFlows(MeshWaterState ws)
	{
		foreach (HydraulicStructure allHydraulicStructure in HydraulicStructures.AllHydraulicStructures)
		{
			if (allHydraulicStructure.TreatFlowAsInternalBoundary)
			{
				allHydraulicStructure.OverwriteFaceFlows(ws);
			}
		}
	}

	public virtual void UpdateFaceState(MeshWaterState ws, List<int> faceIndexes)
	{
		if (Control.faceElevationMethod != FaceElevationMethod.Frozen)
		{
			threadFor.Run(faceIndexes.Count, (int start, int stop) =>
			{
				ws.UpdateFacesMANY(Control.faceElevationMethod, faceIndexes, start, stop);
			});
		}
	}

	public void UpdateCellVolume(MeshWaterState ws, double[] newCellVolume, double dt, List<int> cellIndexes)
	{
		ws.InvalidateCellWaterSurfaceGradients();
		threadFor.Run(cellIndexes.Count, (int start, int stop) =>
		{
			Solver solver = this;
			MeshWaterState meshWaterState = ws;
			double[] newCellVolumeArray = newCellVolume;
			List<int> cellIndexesList = cellIndexes;
			double dt2 = dt;
			for (int i = start; i <= stop; i++)
			{
				int cIdx = cellIndexesList[i];
				solver.UpdateCellVolumeONE(meshWaterState, newCellVolumeArray, dt2, cIdx);
			}
			double[] cellElevation = meshWaterState.CellElevation;
			double[] cellArea = meshWaterState.CellArea;
			double[] cellVolume = meshWaterState.CellVolume;
			float[] cellHydraulicDepth = meshWaterState.CellHydraulicDepth;
			int[] cellPriorIndexes = meshWaterState.CellPriorIndexes;
			for (int j = start; j <= stop; j++)
			{
				int cIdx2 = cellIndexesList[j];
				PropertyTables.UpdateCellsONE(in cIdx2, cellElevation, cellArea, cellVolume, cellHydraulicDepth, cellPriorIndexes, in newCellVolumeArray, ref meshWaterState.ErrorCellVolumeNegative);
			}
		});
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected void UpdateCellVolumeONE(MeshWaterState ws, double[] newCellVolume, double dt, int cIdx)
	{
		Cell cell = _mesh.Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		double computed = 0.0;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = _mesh.Faces[faceIndex];
			double sign = 1.0;
			if (cIdx == face.cellA)
			{
				sign = -1.0;
			}
			computed += sign * ws.FaceFlow[faceIndex];
		}
		CellWaterSourceLedger cellWaterSources = CellWaterSources;
		if (cellWaterSources != null && cellWaterSources.HasCellSources)
		{
			computed += cellWaterSources.CurrentNetFlowArray[cIdx];
			if (cellWaterSources.IsTransportTrackingEnabled)
			{
				cellWaterSources.AccumulateCell(cIdx, dt);
			}
		}
		newCellVolume[cIdx] = ws.CellVolume[cIdx] + dt * computed;
		if (!double.IsFinite(newCellVolume[cIdx]))
		{
			throw new SolverException("Non-finite volume");
		}
		if (newCellVolume[cIdx] < 0.0 && !Control.checkSteps)
		{
			throw new SolverException("Negative volume");
		}
	}

	public double ComputeFaceNormalGradient(MeshWaterState ws, int fIdx, double[] faceDistInv)
	{
		Face face = _mesh.Faces[fIdx];
		int cellA = face.cellA;
		int cellB = face.cellB;
		if (cellB >= 0)
		{
			return (ws.CellElevation[cellB] - ws.CellElevation[cellA]) * faceDistInv[fIdx];
		}
		HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace waterSurfaceBoundaryReconstruction = ws.WaterSurfaceBoundaryReconstruction;
		if (waterSurfaceBoundaryReconstruction != null && waterSurfaceBoundaryReconstruction.TryGetCondition(fIdx, out var type, out var localValue))
		{
			if (type == ScalarBoundaryConditionType.NormalGradient)
			{
				return waterSurfaceBoundaryReconstruction.FaceNormalGradient(fIdx, Control.faceNormalGradientMethod, ws.CellElevation, null, null, null, faceDistInv[fIdx]);
			}
			return (localValue - ws.CellElevation[cellA]) * faceDistInv[fIdx];
		}
		double[] pinnedFaceStage = ws.PinnedFaceStage;
		if (pinnedFaceStage != null && pinnedFaceStage[fIdx] != -9999.0)
		{
			double unity = 1.0;
			return (pinnedFaceStage[fIdx] - ws.CellElevation[cellA]) * unity * faceDistInv[fIdx];
		}
		return 0.0;
	}

	public string DebugFaceInfo(int fIdx, MeshWaterState ws, string delimiter = null)
	{
		delimiter = delimiter ?? Environment.NewLine;
		Geospatial.Vectors.Vector vector = _hmesh.FaceNormal[fIdx];
		return "FaceNormal: <" + vector.DeltaX.ToString("F3") + ", " + vector.DeltaY.ToString("F3") + ">" + delimiter + "Y-velocity: " + vector.DeltaY * ws.FaceVelocity[fIdx];
	}

	public string DebugCellInfo(int cIdx, MeshWaterState ws, string delimiter = null)
	{
		return "";
	}

	public int TrackConservation(List<int> cells = null)
	{
		if (cells == null)
		{
			cells = new List<int>();
			for (int i = 0; i < _mesh.CellCount; i++)
			{
				cells.Add(i);
			}
		}
		MassConservationChecks.Add(new MassConservationCheck(this, cells));
		return MassConservationChecks.Count - 1;
	}

	protected void IncrementConservation(double dt)
	{
		foreach (MassConservationCheck massConservationCheck in MassConservationChecks)
		{
			massConservationCheck.Increment(_waterState, dt);
		}
	}

	protected void CommitConservation()
	{
		foreach (MassConservationCheck massConservationCheck in MassConservationChecks)
		{
			massConservationCheck.Commit(_waterState);
		}
	}

	protected void CommitConservationSerial()
	{
		foreach (MassConservationCheck massConservationCheck in MassConservationChecks)
		{
			massConservationCheck.CommitSerial(_waterState);
		}
	}

	protected void RollbackConservation()
	{
		foreach (MassConservationCheck massConservationCheck in MassConservationChecks)
		{
			massConservationCheck.Rollback();
		}
	}
}
