using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Ras.Engine.Structures;
using Ras.Engine.Structures.Weirs;
using Utility;
using Utility.Progress;

namespace Ras.Engine;

public class SolverExpSWE : Solver, ISolver
{
	public interface IIndexListDefinitions
	{
		static abstract void CalculateSlots(int m, int m1, int lMaxLts, out int volSlot, out int velSlot);

		static abstract bool CellVolume(int m, int m1, int cellLevel2);

		static abstract bool CellVolumeM0(int cellLevel2, int lMaxLts);

		static abstract bool FaceVelocity(int m, int m1, int faceLevel1);

		static abstract bool CellVelocity(int m, int m1, int cellLevel4);

		static abstract bool FaceVelFrict(int m, int m1, int faceLevel3);
	}

	[StructLayout(LayoutKind.Sequential, Size = 1)]
	public struct FBFIndexListDefinitions : IIndexListDefinitions
	{
		public static void CalculateSlots(int m, int m1, int lMaxLts, out int volSlot, out int velSlot)
		{
			if (m == 0)
			{
				volSlot = lMaxLts;
			}
			else
			{
				volSlot = BitOperations.TrailingZeroCount(m);
			}
			velSlot = volSlot;
		}

		public static bool CellVolume(int m, int m1, int cellLevel2)
		{
			return m % (1 << cellLevel2 - 1) == 0;
		}

		public static bool CellVolumeM0(int cellLevel2, int lMaxLts)
		{
			return cellLevel2 <= lMaxLts;
		}

		public static bool FaceVelocity(int m, int m1, int faceLevel1)
		{
			return m % (1 << faceLevel1) == 1 << faceLevel1 - 1;
		}

		public static bool CellVelocity(int m, int m1, int cellLevel4)
		{
			return m % (1 << cellLevel4) == 1 << cellLevel4 - 1;
		}

		public static bool FaceVelFrict(int m, int m1, int faceLevel3)
		{
			return m % (1 << faceLevel3) == 1 << faceLevel3 - 1;
		}
	}

	[StructLayout(LayoutKind.Sequential, Size = 1)]
	public struct FBIndexListDefinitions : IIndexListDefinitions
	{
		public static void CalculateSlots(int m, int m1, int lMaxLts, out int volSlot, out int velSlot)
		{
			if (m == 0)
			{
				volSlot = lMaxLts;
			}
			else
			{
				volSlot = BitOperations.TrailingZeroCount(m);
			}
			int lMaxLtsTerm = 1 << lMaxLts;
			if (m1 == lMaxLtsTerm)
			{
				velSlot = lMaxLts;
			}
			else
			{
				velSlot = BitOperations.TrailingZeroCount(m1);
			}
		}

		public static bool CellVolume(int m, int m1, int cellLevel2)
		{
			return m % (1 << cellLevel2) == 0;
		}

		public static bool CellVolumeM0(int cellLevel2, int lMaxLts)
		{
			return cellLevel2 <= lMaxLts;
		}

		public static bool FaceVelocity(int m, int m1, int faceLevel1)
		{
			return m1 % (1 << faceLevel1) == 0;
		}

		public static bool CellVelocity(int m, int m1, int cellLevel4)
		{
			return m1 % (1 << cellLevel4) == 0;
		}

		public static bool FaceVelFrict(int m, int m1, int faceLevel3)
		{
			return m1 % (1 << faceLevel3) == 0;
		}
	}

	public const bool OffsetStageBC = false;

	private const double DtMaxInactive = 17280000.0;

	private const double DtMaxActive = 8640000.0;

	protected double[] newFaceVelocity;

	private Geospatial.Vectors.Vector[] _bcVelocityMap;

	public Vector2[] CellAdvVarExp;

	public float[] CellAdvVarImp;

	public Vector2[] CellDiffVarExp;

	public float[] CellFlowFactor;

	public float[] CellEddyVisFactor;

	private LocalCoordinates _localCoordinates;

	private const double ALPHA = 2.0 / 3.0;

	private readonly object _computeMaxTimeStepLock = new object();

	protected byte lMaxLts = 2;

	public bool SkipInactiveCells = true;

	private int mMaxLts;

	public int mIncrLts = 1;

	public long local_cell_steps;

	private bool[] volSlotIsStale;

	private bool[] velSlotIsStale;

	private List<List<int>> ILsCellVolumeLts;

	private List<List<int>> ILsFaceVelocityLts;

	private List<List<int>> ILsCellVelocityLts;

	private List<List<int>> ILsFaceVelFrictLts;

	private const bool LTS_COURANT_SERIAL = false;

	private List<int>[] partILCellVolume;

	private List<int>[] partILCellVelocity;

	private List<int>[] partILFaceVelocity;

	private List<int>[] partILFaceVelFrict;

	public LocalCoordinates LocalCoordinates => _localCoordinates;

	protected override double[] FaceInverseDistances
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
				SolverControl.GradientMethod.LocalCoordinates => _localCoordinates.FaceInvDistances, 
				SolverControl.GradientMethod.Distance => _hmesh.FaceRepresentativeInverseDistances, 
				_ => _hmesh.FaceRepresentativeInverseDistances, 
			};
		}
	}

	public bool localStepping
	{
		get
		{
			if (Control.stepType != SolverControl.StepType.FWDBACKFWD_LTS)
			{
				return Control.stepType == SolverControl.StepType.FWDBACK_LTS;
			}
			return true;
		}
	}

	public byte LtsLevels => lMaxLts;

	public byte lInactiveLts => (byte)(lMaxLts + (SkipInactiveCells ? 2 : 0));

	public int MaxLocalRetries => Control.maxLocalIterations;

	public int mLts { get; private set; }

	public SolverControl.LtsStepStrategy ltsStrategy => Control.LtsStrategy;

	public SolverExpSWE(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState)
		: this(mesh, props, initialState, new SolverControl(), null)
	{
	}

	public SolverExpSWE(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState, SolverControl control, ComputeDriver driver)
		: base(mesh, props, initialState, control, null, driver)
	{
	}

	public override void Prepare(ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		base.Prepare(pr);
		if (SpatiallyVariableStageVelocityBoundaryConditions.Count > 0)
		{
			_bcVelocityMap = new Geospatial.Vectors.Vector[_mesh.FaceCount];
			Array.Fill(_bcVelocityMap, new Geospatial.Vectors.Vector(double.NaN, double.NaN));
		}
		_localCoordinates = new LocalCoordinates(_mesh, CellFlags);
		_hmesh.SetFaceTangential(null);
		if (Control.Turbulence.IsActive)
		{
			ComputeFaceVelComp(_waterState, ILFaceVelFrict);
		}
		PrepareTurbulence();
		ComputeViscosity(_waterState, ILFaceAllVelocity, ILCellVelocity);
		if (Control.SecondaryFlow.Enabled)
		{
			_waterState.secondaryFlow = new SecondaryFlowState(_mesh);
			_oldState.secondaryFlow = _waterState.secondaryFlow;
			_secondaryFlow = new SecondaryFlowSolver(_mesh, Control, _waterState.secondaryFlow);
			_secondaryFlow.Prepare(this);
		}
		switch (Control.stepType)
		{
		case SolverControl.StepType.FWDBACK:
			PrepareForwardBack();
			break;
		case SolverControl.StepType.FWDBACKFWD:
			PrepareForwardBack();
			break;
		case SolverControl.StepType.FWDBACKFWD_LTS:
			PrepareForwardBackForwardLts();
			break;
		case SolverControl.StepType.FWDBACK_LTS:
			PrepareForwardBackLts();
			break;
		default:
			throw new SolverException("Unhandled step type during preparation");
		}
		if (Control.MinAdvDt > 0.0)
		{
			Reporter.ReportMessage("Minimum Advective Time Step: " + Control.MinAdvDt + " s");
		}
		else
		{
			Reporter.ReportMessage("Minimum Advective Time Step: None");
		}
	}

	protected override Func<MeshWaterState, int, double> GetFaceConveyanceFunc()
	{
		return FaceConveyanceFrictCoeff;
	}

	public void PrepareForwardBack()
	{
		newFaceVelocity = new double[_mesh.FaceCount];
		CellAdvVarExp = new Vector2[_mesh.CellCount];
		CellAdvVarImp = new float[_mesh.CellCount];
		CellFlowFactor = new float[_mesh.CellCount];
		if (Control.Turbulence.IsActive)
		{
			CellDiffVarExp = new Vector2[_mesh.CellCount];
		}
		if (Control.Turbulence.IsActive && Control.dtLimitDiffusion)
		{
			CellEddyVisFactor = new float[_mesh.CellCount];
		}
	}

	public override void PreparePropertyTables()
	{
		base.PreparePropertyTables();
		_propertyTables.ComputeFaceHydraulicRadiusTables();
		switch (Control.bedFrictionMethod)
		{
		case SolverControl.BedFrictionMethod.None:
			_propertyTables.SetConstBedFrictCoeff(0.0);
			break;
		case SolverControl.BedFrictionMethod.Constant:
			_propertyTables.SetConstBedFrictCoeff(Control.nonlinFrictCoeff);
			break;
		case SolverControl.BedFrictionMethod.Variable:
			_propertyTables.ComputeFaceBedFrictVariableTable(_mesh, grav, nSItoUnits, Control.dzMinFriction);
			break;
		}
	}

	public void PrepareTurbulence()
	{
		PrepareTurbulenceState();
	}

	private void UpdateBCVelocityMap(double absTime)
	{
		Array.Fill(_bcVelocityMap, new Geospatial.Vectors.Vector(double.NaN, double.NaN));
		foreach (BoundaryCondition spatiallyVariableStageVelocityBoundaryCondition in SpatiallyVariableStageVelocityBoundaryConditions)
		{
			double[,] faceMultipleValuesAtTime = spatiallyVariableStageVelocityBoundaryCondition.FaceMultipleValuesAtTime(absTime);
			for (int i = 0; i < spatiallyVariableStageVelocityBoundaryCondition.Faces.Length; i++)
			{
				int faceIndex = spatiallyVariableStageVelocityBoundaryCondition.Faces[i];
				_bcVelocityMap[faceIndex] = new Geospatial.Vectors.Vector(faceMultipleValuesAtTime[i, 1], faceMultipleValuesAtTime[i, 2]);
			}
		}
	}

	public override void CheckpointState()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		_oldState.UpdateFrom<SWEWaterStateConfig>(_waterState, threadFor);
		ticks[14] += stopwatch.ElapsedTicks;
	}

	public override void RevertState()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Solver._logger.LogTrace("Revert state {Reason}", checker.reason);
		rejectCounts[(int)checker.reason]++;
		_waterState.UpdateFrom<SWEWaterStateConfig>(_oldState, threadFor);
		CellWaterSources?.RevertStep();
		RollbackConservation();
		ticks[14] += stopwatch.ElapsedTicks;
	}

	public override double ComputeMaxTimestep(MeshWaterState ws)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		CellWaterSources?.RefreshCurrentRates();
		bool isActive = Control.Turbulence.IsActive && Control.dtLimitDiffusion;
		double[] faceDistInv = FaceInverseDistances;
		StepLimitation reduceLimType = StepLimitation.LIM_NONE;
		int reduceLimCell = -1;
		double dtMax = 8640000.0;
		if (CellDtMax == null)
		{
			CellDtMax = new double[_mesh.CellCount];
		}
		if (Control.celerityUsesFlow)
		{
			MeshWaterState meshWaterState = ws;
			if (meshWaterState.CellDtMaxCelerity == null)
			{
				meshWaterState.CellDtMaxCelerity = new float[_mesh.CellCount];
			}
		}
		threadFor.Run(_mesh.CellCount, (int start, int end) =>
		{
			ComputeMaxTimestepCelerityMANY(ws, faceDistInv, out var taskLimType, out var taskDtMax, out var taskLimCell, CellDtMax, start, end);
			if (taskLimCell >= 0 && taskDtMax < dtMax)
			{
				lock (_computeMaxTimeStepLock)
				{
					if (taskDtMax < dtMax)
					{
						dtMax = taskDtMax;
						reduceLimType = taskLimType;
						reduceLimCell = taskLimCell;
					}
				}
			}
		});
		double dtMaxTarget = Control.MinAdvDt / Control.TimestepSafetyFactor;
		dtMaxTarget = Math.Min(dtMaxTarget, dtMax);
		threadFor.Run(_mesh.CellCount, (int start, int end) =>
		{
			ComputeMaxTimestepAdvMANY(ws, dtMaxTarget, out var taskLimType, out var taskDtMax, out var taskLimCell, CellDtMax, start, end);
			if (taskLimCell >= 0 && taskDtMax < dtMax)
			{
				lock (_computeMaxTimeStepLock)
				{
					if (taskDtMax < dtMax)
					{
						dtMax = taskDtMax;
						reduceLimType = taskLimType;
						reduceLimCell = taskLimCell;
					}
				}
			}
		});
		if (isActive)
		{
			threadFor.Run(_mesh.CellCount, (int start, int end) =>
			{
				ComputeMaxTimestepDiffMANY(ws, faceDistInv, dtMaxTarget, out var taskLimType, out var taskDtMax, out var taskLimCell, CellDtMax, start, end);
				if (taskLimCell >= 0 && taskDtMax < dtMax)
				{
					lock (_computeMaxTimeStepLock)
					{
						if (taskDtMax < dtMax)
						{
							dtMax = taskDtMax;
							reduceLimType = taskLimType;
							reduceLimCell = taskLimCell;
						}
					}
				}
			});
		}
		if (CellDtMax != null)
		{
			foreach (BoundaryCondition stageBoundaryCondition in StageBoundaryConditions)
			{
				WetBCFaces(stageBoundaryCondition.Faces);
			}
			foreach (BoundaryCondition flowBoundaryCondition in FlowBoundaryConditions)
			{
				WetBCFaces(flowBoundaryCondition.Faces);
			}
			foreach (BoundaryCondition normalDepthBoundaryCondition in NormalDepthBoundaryConditions)
			{
				WetBCFaces(normalDepthBoundaryCondition.Faces);
			}
			foreach (BoundaryCondition variableStageBoundaryCondition in VariableStageBoundaryConditions)
			{
				WetBCFaces(variableStageBoundaryCondition.Faces);
			}
			foreach (BoundaryCondition variableFlowBoundaryCondition in VariableFlowBoundaryConditions)
			{
				WetBCFaces(variableFlowBoundaryCondition.Faces);
			}
			foreach (BoundaryCondition spatiallyVariableStageVelocityBoundaryCondition in SpatiallyVariableStageVelocityBoundaryConditions)
			{
				WetBCFaces(spatiallyVariableStageVelocityBoundaryCondition.Faces);
			}
		}
		stepLimitation = reduceLimType;
		limitingCell = reduceLimCell;
		limitingFace = -1;
		if (localStepping)
		{
			dtMax *= 1 << lMaxLts - 1;
		}
		ticks[12] += stopwatch.ElapsedTicks;
		dtMax *= Control.TimestepSafetyFactor;
		return dtMax;
		void WetBCFaces(int[] faces)
		{
			foreach (int faceIndex in faces)
			{
				int faceCellA = _meshConnectivity.FaceCellA[faceIndex];
				if (faceCellA >= 0)
				{
					CellDtMax[faceCellA] = Math.Min(8640000.0, CellDtMax[faceCellA]);
				}
				faceCellA = _meshConnectivity.FaceCellB[faceIndex];
				if (faceCellA >= 0)
				{
					CellDtMax[faceCellA] = Math.Min(8640000.0, CellDtMax[faceCellA]);
				}
			}
		}
	}

	public void ComputeMaxTimestepCelerityMANY(MeshWaterState ws, double[] faceDistInv, out StepLimitation taskLimType, out double taskDtMax, out int taskLimCell, double[] cellDtMax, int start, int end)
	{
		taskLimType = StepLimitation.LIM_NONE;
		taskLimCell = -1;
		taskDtMax = 17280000.0;
		for (int i = start; i <= end; i++)
		{
			double maxTimestepCelerityCell = ComputeMaxTimestepCelerityCell(ws, faceDistInv, i);
			float[] cellDtMaxCelerity = ws.CellDtMaxCelerity;
			if (cellDtMaxCelerity != null)
			{
				cellDtMaxCelerity[i] = (float)maxTimestepCelerityCell;
			}
			cellDtMax[i] = maxTimestepCelerityCell;
			if (maxTimestepCelerityCell < taskDtMax)
			{
				taskDtMax = maxTimestepCelerityCell;
				taskLimType = StepLimitation.LIM_CELERITY;
				taskLimCell = i;
			}
		}
	}

	public double ComputeMaxTimestepCelerityCell(MeshWaterState ws, double[] faceDistInv, int cIdx)
	{
		ReadOnlySpan<int> cellFaces = _meshConnectivity.GetCellFaces(cIdx);
		ReadOnlySpan<int> cellNeighbors = _meshConnectivity.GetCellNeighbors(cIdx);
		double result = 17280000.0;
		double computed = 0.0;
		for (int i = 0; i < cellFaces.Length; i++)
		{
			if (cellNeighbors[i] >= 0)
			{
				int cellFaceIndex = cellFaces[i];
				double faceAreaWsTerm = ws.FaceArea[cellFaceIndex] * faceDistInv[cellFaceIndex];
				computed += faceAreaWsTerm;
			}
		}
		if (computed > 0.0 && ws.CellArea[cIdx] > 0.0)
		{
			result = Math.Sqrt(2.0 * ws.CellArea[cIdx] / (computed * grav));
		}
		return result;
	}

	public double ComputeMaxTimestepCelerityCellNew(MeshWaterState ws, double[] faceDist, int cIdx)
	{
		ReadOnlySpan<int> cellFaces = _meshConnectivity.GetCellFaces(cIdx);
		ReadOnlySpan<int> cellNeighbors = _meshConnectivity.GetCellNeighbors(cIdx);
		double maxTimestepCelerityCellNew = 17280000.0;
		for (int i = 0; i < cellFaces.Length; i++)
		{
			if (cellNeighbors[i] >= 0)
			{
				int cellFaceIndex = cellFaces[i];
				double faceHydraulicDepth = ws.FaceHydraulicDepth[cellFaceIndex];
				faceHydraulicDepth = Math.Max(faceHydraulicDepth, Control.dzMinFriction);
				double faceDistOverSqrtGravTimesFaceHydraulicDepth = faceDist[cellFaceIndex] / Math.Sqrt(grav * faceHydraulicDepth);
				maxTimestepCelerityCellNew = Math.Min(maxTimestepCelerityCellNew, faceDistOverSqrtGravTimesFaceHydraulicDepth);
			}
		}
		return maxTimestepCelerityCellNew;
	}

	public void ComputeMaxTimestepDiffMANY(MeshWaterState ws, double[] faceDistInv, double dtMaxTarget, out StepLimitation taskLimType, out double taskDtMax, out int taskLimCell, double[] cellDtMax, int start, int end)
	{
		taskLimType = StepLimitation.LIM_NONE;
		taskLimCell = -1;
		taskDtMax = 17280000.0;
		for (int i = start; i <= end; i++)
		{
			double maxTimestepDiffusionONE = ComputeMaxTimestepDiffusionONE(ws, faceDistInv, dtMaxTarget, i);
			float[] cellDtMaxDiffusion = ws.CellDtMaxDiffusion;
			if (cellDtMaxDiffusion != null)
			{
				cellDtMaxDiffusion[i] = (float)maxTimestepDiffusionONE;
			}
			if (maxTimestepDiffusionONE < taskDtMax)
			{
				taskDtMax = maxTimestepDiffusionONE;
				taskLimType = StepLimitation.LIM_DIFFUSION;
				taskLimCell = i;
			}
			if (taskDtMax <= 0.0)
			{
				throw new SolverException($"Max time step < 0 taskDtMax:{taskDtMax} cell:{taskLimCell} step:{step} start:{start}");
			}
		}
	}

	public double ComputeMaxTimestepDiffusionONE(MeshWaterState ws, double[] faceDistInv, double dtMaxTarget, int cIdx)
	{
		double maxTimestepDiffusionCell = ComputeMaxTimestepDiffusionCell(ws, faceDistInv, cIdx);
		if (maxTimestepDiffusionCell < dtMaxTarget)
		{
			CellEddyVisFactor[cIdx] = (float)(maxTimestepDiffusionCell / dtMaxTarget);
			maxTimestepDiffusionCell = dtMaxTarget;
		}
		else
		{
			CellEddyVisFactor[cIdx] = 1f;
		}
		return maxTimestepDiffusionCell;
	}

	public double ComputeMaxTimestepDiffusionCell(MeshWaterState ws, double[] faceDistInv, int cIdx)
	{
		double computed = 0.0;
		double result = 8640000.0;
		ReadOnlySpan<int> cellFaces = _meshConnectivity.GetCellFaces(cIdx);
		ReadOnlySpan<int> cellNeighbors = _meshConnectivity.GetCellNeighbors(cIdx);
		for (int i = 0; i < cellFaces.Length; i++)
		{
			if (cellNeighbors[i] >= 0)
			{
				int cellFaceIndex = cellFaces[i];
				double faceAreaWsTerm = ws.FaceArea[cellFaceIndex] * faceDistInv[cellFaceIndex];
				computed += (double)ws.FaceNuH[cellFaceIndex] * faceAreaWsTerm;
			}
		}
		if (ws.turb?.cellVelGrad != null)
		{
			computed *= 2.0;
		}
		if (computed > 0.0)
		{
			double cellVolumeWsTerm = Math.Max(ws.CellVolume[cIdx], Control.dzMinAdvection * ws.CellArea[cIdx]);
			result = 2.0 * cellVolumeWsTerm / computed;
		}
		return result;
	}

	public void ComputeMaxTimestepAdvMANY(MeshWaterState ws, double dtMaxTarget, out StepLimitation taskLimType, out double taskDtMax, out int taskLimCell, double[] cellDtMax, int start, int end)
	{
		taskLimType = StepLimitation.LIM_NONE;
		taskLimCell = -1;
		taskDtMax = 17280000.0;
		double maxRatioWetAdvDt = Control.MaxRatioWetAdvDt;
		for (int i = start; i <= end; i++)
		{
			ComputeMaxTimestepAdvONE(ws, dtMaxTarget, maxRatioWetAdvDt, out var dtCellAdv, i);
			double cellDtMaxCelerityDtCellAdvTerm;
			if (Control.celerityUsesFlow)
			{
				double cellDtMaxCelerity = ws.CellDtMaxCelerity[i];
				cellDtMaxCelerityDtCellAdvTerm = 1.0 / (1.0 / cellDtMaxCelerity + 1.0 / dtCellAdv);
			}
			else
			{
				cellDtMaxCelerityDtCellAdvTerm = dtCellAdv;
			}
			if (cellDtMaxCelerityDtCellAdvTerm < cellDtMax[i])
			{
				cellDtMax[i] = cellDtMaxCelerityDtCellAdvTerm;
			}
			if (cellDtMaxCelerityDtCellAdvTerm < taskDtMax)
			{
				taskDtMax = cellDtMaxCelerityDtCellAdvTerm;
				taskLimCell = i;
				taskLimType = StepLimitation.LIM_ADVECTION;
			}
		}
	}

	public void ComputeMaxTimestepAdvONE(MeshWaterState ws, double dtMaxTarget, double minRatioWetAdvDt, out double dtCellAdv, int cIdx)
	{
		dtCellAdv = ComputeMaxTimestepAdvectionCell(ws, cIdx);
		CellFlowFactor[cIdx] = 1f;
		if (dtMaxTarget > 0.0 && dtCellAdv < dtMaxTarget && ws.CellArea[cIdx] < minRatioWetAdvDt * _hmesh.CellAreas[cIdx])
		{
			CellFlowFactor[cIdx] = (float)(dtCellAdv / dtMaxTarget);
			dtCellAdv = dtMaxTarget;
		}
		float[] cellDtMaxAdvection = ws.CellDtMaxAdvection;
		if (cellDtMaxAdvection != null)
		{
			cellDtMaxAdvection[cIdx] = (float)dtCellAdv;
		}
	}

	public double ComputeMaxTimestepAdvectionCell(MeshWaterState ws, int cIdx)
	{
		double result = 17280000.0;
		ReadOnlySpan<int> cellFaces = _meshConnectivity.GetCellFaces(cIdx);
		ReadOnlySpan<int> cellFaceOrientations = _meshConnectivity.GetCellFaceOrientations(cIdx);
		double computed = 0.0;
		for (int i = 0; i < cellFaces.Length; i++)
		{
			int cellFaceIndex = cellFaces[i];
			double cellFaceOrientationsFaceFlowTerm = (double)cellFaceOrientations[i] * ws.FaceFlow[cellFaceIndex];
			if (cellFaceOrientationsFaceFlowTerm > 0.0)
			{
				computed += cellFaceOrientationsFaceFlowTerm;
			}
		}
		CellWaterSourceLedger cellWaterSources = CellWaterSources;
		double explicitTimestepFlow = ((cellWaterSources != null && cellWaterSources.HasCellSources) ? CellWaterSources.GetExplicitTimestepFlow(cIdx) : 0.0);
		computed -= explicitTimestepFlow;
		if (explicitTimestepFlow > 0.0)
		{
			result = 8640000.0;
		}
		if (computed > 0.0)
		{
			result = ws.CellVolume[cIdx] / computed;
		}
		return result;
	}

	protected override ISolver.Status SolveStepInternal(double absTime, double dt, double dtPrevious)
	{
		if (SpatiallyVariableStageVelocityBoundaryConditions.Count > 0)
		{
			UpdateBCVelocityMap(absTime);
		}
		switch (Control.stepType)
		{
		case SolverControl.StepType.FWDBACK:
			return SolveStepForwardBack(absTime, dt);
		case SolverControl.StepType.FWDBACKFWD:
			return SolveStepForwardBackForward(absTime, dt);
		case SolverControl.StepType.FWDBACK_LTS:
		case SolverControl.StepType.FWDBACKFWD_LTS:
			return SolveStepLts(absTime, dt, dtPrevious);
		default:
			throw new SolverException("Bad step type");
		}
	}

	public ISolver.Status SolveStepForwardBack(double absTime, double dt)
	{
		MeshWaterState ws = _waterState;
		ws.ErrorCellVolumeNegative = false;
		Stopwatch stopwatch = new Stopwatch();
		stopwatch.Restart();
		UpdateCellVolume(ws, newCellVolume, dt, ILCellVolume);
		ticks[5] += stopwatch.ElapsedTicks;
		if (ws.ErrorCellVolumeNegative)
		{
			return ISolver.Status.VOLUME_NEGATIVE;
		}
		IncrementConservation(dt);
		stopwatch.Restart();
		ComputeCellVelocity(ws, ws.CellVelocity, ILCellVelocity);
		ticks[8] += stopwatch.ElapsedTicks;
		stopwatch.Restart();
		UpdateFaceState(ws, ILFaceVelocity);
		ticks[6] += stopwatch.ElapsedTicks;
		if (Control.advectionMethod != SolverControl.AdvectionMethod.None)
		{
			stopwatch.Restart();
			ComputeCellAdvVar(ws, ILCellVelocity);
			ticks[2] += stopwatch.ElapsedTicks;
		}
		stopwatch.Restart();
		ComputeFaceVelComp(ws, ILFaceVelFrict);
		ticks[3] += stopwatch.ElapsedTicks;
		ComputeViscosity(ws, ILFaceVelFrict, ILCellVelocity);
		stopwatch.Restart();
		ComputeFaceVelocity(ws, newFaceVelocity, absTime, dt, ILFaceVelocity);
		ticks[1] += stopwatch.ElapsedTicks;
		ComputeWeirFaceVelocity(ws, newFaceVelocity, dt, ILFaceVelocity);
		stopwatch.Restart();
		threadFor.Run(ILFaceVelocity.Count, (int start, int stop) =>
		{
			UpdateFaceVelocityAndStateMANY(ws, newFaceVelocity, ILFaceVelocity, start, stop);
		});
		ticks[6] += stopwatch.ElapsedTicks;
		return ISolver.Status.OK;
	}

	private double FaceConveyanceFrictCoeff(MeshWaterState ws, int fIdx)
	{
		double computed = 0.005;
		double x = Math.Max(1E-10, ws.FaceHydraulicRadius[fIdx]);
		double gravFaceNonlinFrictCoeffTerm = Math.Max(grav * computed * computed * Math.Pow(x, -1.3333333333333333), ws.FaceNonlinFrictCoeff[fIdx]);
		return ws.FaceArea[fIdx] * Math.Sqrt(grav / gravFaceNonlinFrictCoeffTerm);
	}

	protected override void ApplyFlowBoundaryConditionEGSlope(BoundaryCondition bc, double Qtot, MeshWaterState ws)
	{
		double minConveyance = Math.Abs(Qtot) / Math.Sqrt(bc.EGSlope);
		ApplyFlowBoundaryConditionByConveyance(bc, Qtot, ws, FaceConveyanceFrictCoeff, minConveyance, setVelocity: true);
	}

	public void ComputeCellVelocity(MeshWaterState ws, Geospatial.Vectors.Vector[] cellVel, List<int> cellIndexes)
	{
		switch (Control.cellVelocityMethod)
		{
		case SolverControl.CellVelocityMethod.Perot:
			threadFor.Run(cellIndexes.Count, (int start, int stop) =>
			{
				_hmesh.CellComputes.PerotVelocity(ws.FaceVelocity, cellVel, cellIndexes, start, stop);
			});
			break;
		case SolverControl.CellVelocityMethod.WLS:
			threadFor.Run(cellIndexes.Count, (int start, int stop) =>
			{
				_hmesh.CellComputes.VelocityWLS(ws.FaceArea, ws.FaceVelocity, cellVel, _localCoordinates.FaceNormal, cellIndexes, start, stop);
			});
			if (HydraulicStructures.HasWeirs)
			{
				OverrideStructFaceCellVelocity(ws, cellVel, cellIndexes);
			}
			break;
		}
	}

	public void OverrideStructFaceCellVelocity(MeshWaterState ws, Geospatial.Vectors.Vector[] cellVelocity, List<int> cellIndexes)
	{
		MeshConnectivity meshConnectivity = _meshConnectivity;
		foreach (Weir weir2 in HydraulicStructures.Weirs)
		{
			Weir weir = weir2 as Weir;
			for (int i = 0; i < weir2.Faces.Length; i++)
			{
				int faceIndex = weir2.Faces[i];
				int faceCellA = meshConnectivity.FaceCellA[faceIndex];
				int faceCellB = meshConnectivity.FaceCellB[faceIndex];
				double cellElevation = ws.CellElevation[faceCellA];
				double cellElevation2 = ws.CellElevation[faceCellB];
				double weirCrest = weir.WeirCrest[i];
				double weirApproachVel = weir.GetWeirApproachVel(ws, _mesh, _hmesh, faceCellA, faceIndex);
				double weirApproachVel2 = weir.GetWeirApproachVel(ws, _mesh, _hmesh, faceCellB, faceIndex);
				double cellElevationWeirApproachVelTerm = cellElevation + weirApproachVel * weirApproachVel / (2.0 * grav);
				double cellElevation2WeirApproachVel2Term = cellElevation2 + weirApproachVel2 * weirApproachVel2 / (2.0 * grav);
				if (cellIndexes.Contains(faceCellA) && cellElevation > weirCrest && cellElevationWeirApproachVelTerm > cellElevation2WeirApproachVel2Term)
				{
					cellVelocity[faceCellA] = _hmesh.CellComputes.VelocityWLS(faceCellA, ws.FaceArea, ws.FaceVelocity, faceIndex);
				}
				else if (cellIndexes.Contains(faceCellB) && cellElevation2 > weirCrest && cellElevation2WeirApproachVel2Term > cellElevationWeirApproachVelTerm)
				{
					cellVelocity[faceCellB] = _hmesh.CellComputes.VelocityWLS(faceCellB, ws.FaceArea, ws.FaceVelocity, faceIndex);
				}
			}
		}
	}

	public void UpdateFaceVelocityAndStateMANY(MeshWaterState ws, double[] newFaceVelocity, List<int> faceIndexes, int start, int stop)
	{
		FaceFlag[] faceFlags = FaceFlags;
		double[] faceArea = ws.FaceArea;
		double[] faceFlow = ws.FaceFlow;
		double[] faceVelocity = ws.FaceVelocity;
		bool faceElevationMethodControlTerm = Control.faceElevationMethod == FaceElevationMethod.Frozen;
		for (int i = start; i <= stop; i++)
		{
			int fIdx = faceIndexes[i];
			if ((faceFlags[fIdx] & FaceFlag.COMPUTE_FLOW) != 0)
			{
				bool faceVelocityFIdxTerm = faceVelocity[fIdx] != newFaceVelocity[fIdx] && faceVelocity[fIdx] * newFaceVelocity[fIdx] <= 0.0;
				faceVelocity[fIdx] = newFaceVelocity[fIdx];
				if (faceVelocityFIdxTerm && !faceElevationMethodControlTerm)
				{
					double wsel = HydraulicMeshComputer.FaceComputes.WSEL_Engine(fIdx, ws.CellElevation, ws.PinnedFaceStage, faceVelocity, Control.faceElevationMethod, _propertyTables);
					_propertyTables.UpdateFaceONE(in fIdx, ws.FacePriorIndexes, ws.FaceElevation, faceArea, ws.FaceHydraulicRadius, ws.FaceHydraulicDepth, ws.FaceMannings, ws.FaceNonlinFrictCoeff, ws.FaceShearFluxCoeff, ws.FaceConveyance, in wsel);
				}
				faceFlow[fIdx] = faceArea[fIdx] * faceVelocity[fIdx];
			}
		}
	}

	public void ComputeCellAdvVar(MeshWaterState ws, List<int> cellIndexes)
	{
		threadFor.Run(cellIndexes.Count, (int start, int stop) =>
		{
			ComputeCellAdvVarMANY(ws, cellIndexes, start, stop);
		});
	}

	private void ComputeCellAdvVarMANY(MeshWaterState ws, List<int> cellIndexes, int start, int stop)
	{
		ReadOnlySpan<double> faceVelocity = ws.FaceVelocity.AsSpan();
		ReadOnlySpan<double> faceArea = ws.FaceArea.AsSpan();
		ReadOnlySpan<Geospatial.Vectors.Vector> cellVelocity = ws.CellVelocity.AsSpan();
		ReadOnlySpan<int> neighborFaces = _meshConnectivity.CellNeighborFaces.AsSpan();
		ReadOnlySpan<int> neighborCells = _meshConnectivity.CellNeighborCells.AsSpan();
		ReadOnlySpan<int> faceOrientations = _meshConnectivity.CellFaceOrientations.AsSpan();
		int[] cellNeighborOffsets = _meshConnectivity.CellNeighborOffsets;
		Geospatial.Vectors.Vector[] faceNormal = _hmesh.FaceNormal;
		Geospatial.Vectors.Vector[,] faceNormal2 = _localCoordinates.FaceNormal;
		for (int i = start; i <= stop; i++)
		{
			int cellIndex = cellIndexes[i];
			double sourceFlow = 0.0;
			Geospatial.Vectors.Vector sourceMomentum = default;
			if (HydraulicStructures.HasSourceSinks && HydraulicStructures.SourceSinks.TryGetValue(cellIndex, out (double, double, Geospatial.Vectors.Vector) valueTuple) && valueTuple.Item1 > 0.0)
			{
				(sourceFlow, _, sourceMomentum) = valueTuple;
			}
			Solver.ComputeCellAccumulation(cellIndex, cellNeighborOffsets, neighborFaces, neighborCells, faceOrientations, faceVelocity, faceArea, cellVelocity, faceNormal, faceNormal2, CellFlags, _localCoordinates, null, null, _bcVelocityMap, sourceFlow, sourceMomentum, out var explicitVariable, out var implicitVariable);
			CellAdvVarExp[cellIndex] = explicitVariable;
			CellAdvVarImp[cellIndex] = implicitVariable;
		}
	}

	public void ComputeCellDiffVar(MeshWaterState ws, List<int> cellIndexes)
	{
		float[] cellFactors = (Control.dtLimitDiffusion ? CellEddyVisFactor : null);
		ComputeCellTurbulentMixing(ws, cellIndexes, CellDiffVarExp, cellFactors);
	}

	public override void UpdateFaceState(MeshWaterState ws, List<int> faceIndexes)
	{
		if (Control.faceElevationMethod != FaceElevationMethod.Frozen)
		{
			threadFor.Run(faceIndexes.Count, (int start, int stop) =>
			{
				ws.UpdateFacesMANY<SWEWaterStateConfig>(Control.faceElevationMethod, faceIndexes, start, stop);
			});
		}
	}

	public void UpdateFaceStateAndFlowMANY(MeshWaterState ws, List<int> faceIndexes, int start, int stop)
	{
		FaceFlag[] faceFlags = FaceFlags;
		double[] faceArea = ws.FaceArea;
		double[] faceFlow = ws.FaceFlow;
		double[] faceVelocity = ws.FaceVelocity;
		double[] faceElevation = ws.FaceElevation;
		float[] faceHydraulicRadius = ws.FaceHydraulicRadius;
		float[] faceHydraulicDepth = ws.FaceHydraulicDepth;
		float[] faceMannings = ws.FaceMannings;
		float[] faceNonlinFrictCoeff = ws.FaceNonlinFrictCoeff;
		float[] faceShearFluxCoeff = ws.FaceShearFluxCoeff;
		double[] faceConveyance = ws.FaceConveyance;
		double[] cellElevation = ws.CellElevation;
		double[] pinnedFaceStage = ws.PinnedFaceStage;
		FaceElevationMethod faceElevationMethod = Control.faceElevationMethod;
		bool faceElevationMethodFrozenTerm = faceElevationMethod == FaceElevationMethod.Frozen;
		int[] facePriorIndexes = ws.FacePriorIndexes;
		for (int i = start; i <= stop; i++)
		{
			int fIdx = faceIndexes[i];
			if (!faceElevationMethodFrozenTerm)
			{
				double wsel = HydraulicMeshComputer.FaceComputes.WSEL_Engine(fIdx, cellElevation, pinnedFaceStage, faceVelocity, faceElevationMethod, _propertyTables);
				_propertyTables.UpdateFaceONE(in fIdx, facePriorIndexes, faceElevation, faceArea, faceHydraulicRadius, faceHydraulicDepth, faceMannings, faceNonlinFrictCoeff, faceShearFluxCoeff, faceConveyance, in wsel);
			}
			if ((faceFlags[fIdx] & FaceFlag.COMPUTE_FLOW) != 0)
			{
				faceFlow[fIdx] = faceArea[fIdx] * faceVelocity[fIdx];
			}
		}
	}

	public ISolver.Status SolveStepForwardBackForward(double absTime, double dt)
	{
		MeshWaterState ws = _waterState;
		ws.ErrorCellVolumeNegative = false;
		UpdateCellVolume(ws, newCellVolume, 0.5 * dt, ILCellVolume);
		IncrementConservation(0.5 * dt);
		if (ws.ErrorCellVolumeNegative)
		{
			return ISolver.Status.VOLUME_NEGATIVE;
		}
		ComputeCellVelocity(ws, ws.CellVelocity, ILCellVelocity);
		UpdateFaceState(ws, ILFaceAll);
		if (Control.advectionMethod != SolverControl.AdvectionMethod.None)
		{
			ComputeCellAdvVar(ws, ILCellVelocity);
		}
		ComputeFaceVelComp(ws, ILFaceVelFrict);
		ComputeViscosity(ws, ILFaceVelFrict, ILCellVelocity);
		ComputeFaceVelocity(ws, newFaceVelocity, absTime, dt, ILFaceVelocity);
		ComputeWeirFaceVelocity(ws, newFaceVelocity, dt, ILFaceVelocity);
		threadFor.Run(ILFaceVelocity.Count, (int start, int stop) =>
		{
			UpdateFaceVelocityAndStateMANY(ws, newFaceVelocity, ILFaceVelocity, start, stop);
		});
		UpdateCellVolume(ws, newCellVolume, 0.5 * dt, ILCellVolume);
		IncrementConservation(0.5 * dt);
		if (!ws.ErrorCellVolumeNegative)
		{
			return ISolver.Status.OK;
		}
		return ISolver.Status.VOLUME_NEGATIVE;
	}

	public void ComputeFaceDepths(int fIdx, int cL, int cR, MeshWaterState ws, out double faceHbar)
	{
		if (ws.FaceArea[fIdx] <= 0.0)
		{
			faceHbar = 0.0;
		}
		else if (cR >= 0)
		{
			FaceAB faceAB = _hmesh.FaceAlpha[fIdx];
			faceHbar = faceAB.ValueA * (double)ws.CellHydraulicDepth[cL] + faceAB.ValueB * (double)ws.CellHydraulicDepth[cR];
		}
		else
		{
			faceHbar = _propertyTables.ComputeFaceHydraulicDepth(in fIdx, ws.FacePriorIndexes, in ws.FaceElevation[fIdx]);
		}
	}

	public unsafe void ComputeAdvectionFace(int fIdx, int cA, int cB, MeshWaterState ws, double faceHbar, out double advection)
	{
		advection = 0.0;
		if (cB < 0)
		{
			return;
		}
		double dzMinAdvection = Control.dzMinAdvection;
		int ordinal = 0;
		int cA2 = cA;
		while (ordinal < 2)
		{
			double computed = 0.0;
			Geospatial.Vectors.Vector vector = _localCoordinates.FaceNormal[fIdx, ordinal];
			ReadOnlySpan<int> cellFaces = _meshConnectivity.GetCellFaces(cA2);
			ReadOnlySpan<int> cellNeighbors = _meshConnectivity.GetCellNeighbors(cA2);
			ReadOnlySpan<int> cellFaceOrientations = _meshConnectivity.GetCellFaceOrientations(cA2);
			for (int i = 0; i < cellFaces.Length; i++)
			{
				int cellFaceIndex = cellFaces[i];
				int cellNeighbor = cellNeighbors[i];
				int cellFaceOrientation = cellFaceOrientations[i];
				int cellFaceOrientationTerm = 1 - cellFaceOrientation >> 1;
				bool cellNeighborIsNegativeAndHasBcVelocityMapAndNotIsNaN = cellNeighbor < 0 && _bcVelocityMap != null && !double.IsNaN(_bcVelocityMap[cellFaceIndex].X);
				Geospatial.Vectors.Vector input;
				double faceVelocity;
				if (cellNeighbor < 0)
				{
					if (!cellNeighborIsNegativeAndHasBcVelocityMapAndNotIsNaN)
					{
						continue;
					}
					input = _bcVelocityMap[cellFaceIndex];
					faceVelocity = ws.FaceVelocity[cellFaceIndex];
				}
				else
				{
					input = ws.CellVelocity[cellNeighbor];
					faceVelocity = ws.FaceVelocity[cellFaceIndex];
				}
				double cellFaceOrientationTimesFaceVelocity = (double)cellFaceOrientation * faceVelocity;
				if (cellFaceOrientationTimesFaceVelocity < 0.0)
				{
					Geospatial.Vectors.Vector vector2 = _localCoordinates.RotateThroughFace(input, cellFaceIndex, 1 - cellFaceOrientationTerm);
					double dotProduct = vector2.X * vector.X + vector2.Y * vector.Y;
					computed += ws.FaceArea[cellFaceIndex] * cellFaceOrientationTimesFaceVelocity * (dotProduct - ws.FaceVelocity[fIdx]);
				}
			}
			if (HydraulicStructures.HasSourceSinks && HydraulicStructures.SourceSinks.TryGetValue(cA2, out (double, double, Geospatial.Vectors.Vector) valueTuple) && valueTuple.Item1 > 0.0)
			{
				double dotProduct2 = valueTuple.Item3.X * vector.X + valueTuple.Item3.Y * vector.Y;
				computed -= dotProduct2 - valueTuple.Item1 * ws.FaceVelocity[fIdx];
			}
			advection += _hmesh.FaceAlphaCellAInv[fIdx].LR[ordinal] * computed;
			ordinal++;
			cA2 = cB;
		}
		advection /= Math.Max(faceHbar, dzMinAdvection);
	}

	public unsafe void ComputeAdvectionSemiFace(int fIdx, int cL, int cR, MeshWaterState ws, double faceHbar, Vector2[] fNorms, out double advExp, out double advImp)
	{
		advExp = 0.0;
		advImp = 0.0;
		double dzMinAdvection = Control.dzMinAdvection;
		if (cR < 0)
		{
			return;
		}
		Vector2 vector = fNorms[fIdx];
		int ordinal = 0;
		int cellIndex = cL;
		while (ordinal < 2)
		{
			double computed = 0.0;
			double computed2 = 0.0;
			ReadOnlySpan<int> cellFaces = _meshConnectivity.GetCellFaces(cellIndex);
			ReadOnlySpan<int> cellNeighbors = _meshConnectivity.GetCellNeighbors(cellIndex);
			ReadOnlySpan<int> cellFaceOrientations = _meshConnectivity.GetCellFaceOrientations(cellIndex);
			for (int i = 0; i < cellFaces.Length; i++)
			{
				int cellFaceIndex = cellFaces[i];
				int cellNeighbor = cellNeighbors[i];
				if (cellNeighbor >= 0)
				{
					double cellFaceOrientationsFaceVelocityTerm = (double)cellFaceOrientations[i] * ws.FaceVelocity[cellFaceIndex];
					if (cellFaceOrientationsFaceVelocityTerm < 0.0)
					{
						Geospatial.Vectors.Vector vector2 = ws.CellVelocity[cellNeighbor];
						double dotProduct = vector2.X * (double)vector.X + vector2.Y * (double)vector.Y;
						double faceAreaWsTerm = ws.FaceArea[cellFaceIndex] * cellFaceOrientationsFaceVelocityTerm;
						computed += faceAreaWsTerm * dotProduct;
						computed2 -= faceAreaWsTerm;
					}
				}
			}
			FaceAB faceAB = _hmesh.FaceAlphaCellAInv[fIdx];
			advExp += faceAB.LR[ordinal] * computed;
			advImp += faceAB.LR[ordinal] * computed2;
			ordinal++;
			cellIndex = cR;
		}
		double faceHbarDzMinAdvectionTerm = 1.0 / Math.Max(faceHbar, dzMinAdvection);
		advExp *= faceHbarDzMinAdvectionTerm;
		advImp *= faceHbarDzMinAdvectionTerm;
	}

	public void ComputeFaceAdvectionFromCellAccumulations(int fIdx, int cL, int cR, MeshWaterState ws, double faceHbar, Geospatial.Vectors.Vector[] fNorms, double theta, out double advExp, out double advImp)
	{
		double faceHbarInverse = 1.0 / Math.Max(faceHbar, Control.dzMinAdvection);
		Solver.ComputeFaceAdvectionFromCellAccumulations(fIdx, cL, cR, faceHbarInverse, ws.FaceVelocity[fIdx], theta, CellAdvVarExp, CellAdvVarImp, fNorms, _localCoordinates.FaceNormal, CellFlags, _hmesh.FaceAlphaCellAInv, out advExp, out advImp);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ComputeDiffusionFace(int fIdx, int cL, int cR, double faceHbar, double dzMin, out double diffusion, ReadOnlySpan<Vector2> cellDiffVarExpSp, Geospatial.Vectors.Vector[] fNorms, FaceAB[] faceAlphaCellAInv)
	{
		Geospatial.Vectors.Vector vector = fNorms[fIdx];
		Vector2 faceNormal = new Vector2((float)vector.X, (float)vector.Y);
		float faceDepthInverse = (float)(1.0 / Math.Max(faceHbar, dzMin));
		diffusion = Solver.ComputeFaceTurbulentMixing(cL, cR, faceDepthInverse, faceNormal, faceAlphaCellAInv[fIdx], cellDiffVarExpSp);
	}

	public void ComputeDiffusionFaceFused(int fIdx, int cL, int cR, MeshWaterState ws, double faceHbar, out double diffusion, Geospatial.Vectors.Vector[] fNorms, double[] faceDistInv, FaceAB[] faceAlphaCellAInv)
	{
		ComputeDiffusionFaceFused(fIdx, cL, cR, ws, faceHbar, out diffusion, ws.CellVelocity, fNorms, faceDistInv, faceAlphaCellAInv);
	}

	public unsafe void ComputeDiffusionFaceFused(int fIdx, int cL, int cR, MeshWaterState ws, double faceHbar, out double diffusion, Geospatial.Vectors.Vector[] cellVel, Geospatial.Vectors.Vector[] fNorms, double[] faceDistInv, FaceAB[] faceAlphaCellAInv)
	{
		diffusion = 0.0;
		double dzMinAdvection = Control.dzMinAdvection;
		if (cR < 0)
		{
			return;
		}
		Geospatial.Vectors.Vector vector = fNorms[fIdx];
		GradientUVXY[] gradientUVXYs = ws.turb?.cellVelGrad;
		int[] faceCellA = _meshConnectivity.FaceCellA;
		FaceAB[] faceAlpha = _hmesh.FaceAlpha;
		int ordinal = 0;
		int cL2 = cL;
		while (ordinal < 2)
		{
			double computed = 0.0;
			ReadOnlySpan<int> cellFaces = _meshConnectivity.GetCellFaces(cL2);
			ReadOnlySpan<int> cellNeighbors = _meshConnectivity.GetCellNeighbors(cL2);
			for (int i = 0; i < cellFaces.Length; i++)
			{
				int cellFaceIndex = cellFaces[i];
				double faceNuH = ws.FaceNuH[cellFaceIndex];
				if (faceNuH == 0.0)
				{
					continue;
				}
				int cellNeighbor = cellNeighbors[i];
				if (cellNeighbor < 0)
				{
					continue;
				}
				double faceArea = ws.FaceArea[cellFaceIndex];
				if (faceArea > 0.0)
				{
					Geospatial.Vectors.Vector vector2 = cellVel[cellNeighbor];
					Geospatial.Vectors.Vector vector3 = cellVel[cL2];
					computed += faceNuH * faceArea * (vector.DeltaX * (vector2.X - vector3.X) + vector.DeltaY * (vector2.Y - vector3.Y)) * faceDistInv[cellFaceIndex];
					if (gradientUVXYs != null)
					{
						bool sign = faceCellA[cellFaceIndex] == cL2;
						GradientUVXY gradientUVXY = gradientUVXYs[sign ? cL2 : cellNeighbor];
						GradientUVXY gradientUVXY2 = gradientUVXYs[sign ? cellNeighbor : cL2];
						FaceAB faceAB = faceAlpha[cellFaceIndex];
						double valueAFaceABTerm = faceAB.ValueA * gradientUVXY.DU_DX + faceAB.ValueB * gradientUVXY2.DU_DX;
						double valueAFaceABTerm2 = faceAB.ValueA * gradientUVXY.DU_DY + faceAB.ValueB * gradientUVXY2.DU_DY;
						double valueAFaceABTerm3 = faceAB.ValueA * gradientUVXY.DV_DX + faceAB.ValueB * gradientUVXY2.DV_DX;
						double valueAFaceABTerm4 = faceAB.ValueA * gradientUVXY.DV_DY + faceAB.ValueB * gradientUVXY2.DV_DY;
						Geospatial.Vectors.Vector vector4 = fNorms[cellFaceIndex];
						double signTerm = (sign ? 1.0 : (-1.0));
						double deltaXVector4Term = signTerm * vector4.DeltaX;
						double deltaYVector4Term = signTerm * vector4.DeltaY;
						computed += faceNuH * faceArea * (vector.DeltaX * (valueAFaceABTerm * deltaXVector4Term + valueAFaceABTerm3 * deltaYVector4Term) + vector.DeltaY * (valueAFaceABTerm2 * deltaXVector4Term + valueAFaceABTerm4 * deltaYVector4Term));
					}
				}
			}
			FaceAB faceAB2 = faceAlphaCellAInv[fIdx];
			double lrFaceAB2Term = faceAB2.LR[ordinal] * computed;
			if (Control.dtLimitDiffusion)
			{
				lrFaceAB2Term *= (double)CellEddyVisFactor[cL2];
			}
			diffusion += lrFaceAB2Term;
			ordinal++;
			cL2 = cR;
		}
		diffusion /= Math.Max(faceHbar, dzMinAdvection);
	}

	public void ComputeFaceVelocity(MeshWaterState ws, double[] newFaceVelocity, double absTime, double dt, List<int> faceIndexes)
	{
		if (HasNonOrthogonalCorrection)
		{
			UpdateFaceGradientData(ws);
		}
		threadFor.Run(faceIndexes.Count, (int start, int end) =>
		{
			ComputeFaceVelocityMANY(ws, newFaceVelocity, absTime, dt, faceIndexes, start, end);
		});
	}

	private void ComputeFaceVelocityMANY(MeshWaterState ws, double[] newFaceVelocity, double absTime, double dt, List<int> faceIndexes, int start, int end)
	{
		Geospatial.Vectors.Vector[] faceNormal = _hmesh.FaceNormal;
		float[] faceVelMag = ws.FaceVelMag;
		double g_dt = dt * grav;
		double[] faceInverseDistances = FaceInverseDistances;
		FaceAB[] faceAlphaCellAInv = _hmesh.FaceAlphaCellAInv;
		for (int i = start; i <= end; i++)
		{
			ComputeFaceVelocityONE(ws, newFaceVelocity, dt, faceIndexes[i], faceInverseDistances, faceVelMag, faceNormal, faceAlphaCellAInv, g_dt);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void ComputeFaceVelocityONE(MeshWaterState ws, double[] newFaceVelocity, double dt, int fIdx, double[] faceDistInv, float[] faceVelMag, Geospatial.Vectors.Vector[] fNorms, FaceAB[] faceAlphaCellAInv, double g_dt)
	{
		FaceFlag faceFlag = FaceFlags[fIdx];
		if (ws.FaceArea[fIdx] <= 0.0)
		{
			newFaceVelocity[fIdx] = 0.0;
			return;
		}
		if ((faceFlag & FaceFlag.FORCE_FLOW) != 0)
		{
			newFaceVelocity[fIdx] = ws.FaceVelocity[fIdx];
			return;
		}
		int faceCellA = _meshConnectivity.FaceCellA[fIdx];
		int faceCellB = _meshConnectivity.FaceCellB[fIdx];
		double faceVelocity = ws.FaceVelocity[fIdx];
		ComputeFaceDepths(fIdx, faceCellA, faceCellB, ws, out var faceHbar);
		double advImp = 0.0;
		if (Control.advectionMethod != SolverControl.AdvectionMethod.None)
		{
			double theta = ((Control.advectionMethod == SolverControl.AdvectionMethod.ExplicitUpwind) ? 0.0 : Control.AdvectionImplicitFactor);
			ComputeFaceAdvectionFromCellAccumulations(fIdx, faceCellA, faceCellB, ws, faceHbar, fNorms, theta, out var advExp, out advImp);
			faceVelocity -= dt * advExp;
		}
		if (Control.streamwiseInertialTerm)
		{
			faceVelocity += dt * ComputeInertialTerm(fIdx, ws);
		}
		if (Control.Turbulence.IsActive)
		{
			ComputeDiffusionFace(fIdx, faceCellA, faceCellB, faceHbar, Control.dzMinAdvection, out var diffusion, CellDiffVarExp, fNorms, faceAlphaCellAInv);
			faceVelocity += dt * diffusion;
		}
		if (_secondaryFlow != null)
		{
			faceVelocity += dt * _secondaryFlow.State.FaceMomentumDispersion[fIdx];
		}
		double faceNormalGradient = ComputeFaceNormalGradient(ws, fIdx, faceDistInv);
		if (FaceUsesNonOrthogonalCorrection(fIdx))
		{
			faceNormalGradient += ComputeFaceWaterSurfaceCorrection(fIdx, ws);
		}
		faceVelocity -= g_dt * faceNormalGradient;
		double faceVelocity2 = faceVelocity;
		if (faceHbar > Control.dzMinAdvection)
		{
			if (BoundaryConditions.HasAirPressure)
			{
				faceVelocity2 -= dt * BoundaryConditions.AirPressure.FaceGradAirPressure[fIdx] / WaterDensity;
			}
			if (BoundaryConditions.HasWind)
			{
				faceVelocity2 += dt * (double)BoundaryConditions.Wind.FaceWindStressN[fIdx] / (WaterDensity * faceHbar);
			}
			if (Control.fCoriolis != 0.0)
			{
				faceVelocity2 += dt * Control.fCoriolis * ws.FaceVelTangA[fIdx];
			}
		}
		double linFrictCoeffControlTerm = Control.linFrictCoeff + (double)ws.FaceNonlinFrictCoeff[fIdx] * (double)faceVelMag[fIdx];
		newFaceVelocity[fIdx] = faceVelocity2 / (1.0 + dt * (linFrictCoeffControlTerm + advImp));
		if (CellFlowFactor != null)
		{
			int faceUpwindCell = _meshConnectivity.GetFaceUpwindCell(fIdx, ws.FaceVelocity);
			if (faceUpwindCell >= 0)
			{
				newFaceVelocity[fIdx] *= CellFlowFactor[faceUpwindCell];
			}
		}
		if (faceCellB >= 0 && ((newFaceVelocity[fIdx] > 0.0 && ws.CellVolume[faceCellA] <= 0.0) || (newFaceVelocity[fIdx] < 0.0 && ws.CellVolume[faceCellB] <= 0.0)))
		{
			newFaceVelocity[fIdx] = 0.0;
		}
	}

	public double ComputeInertialTerm(int fIdx, MeshWaterState ws)
	{
		double inertialTerm = 0.0;
		int faceCellA = _meshConnectivity.FaceCellA[fIdx];
		int faceCellB = _meshConnectivity.FaceCellB[fIdx];
		if (faceCellB < 0)
		{
			return inertialTerm;
		}
		for (int i = 0; i < 2; i++)
		{
			int faceCellA2 = ((i == 0) ? faceCellA : faceCellB);
			double cellCurvatureLocalCoordinatesTerm = _localCoordinates.CellCurvature[faceCellA2, 0];
			if (cellCurvatureLocalCoordinatesTerm != 0.0)
			{
				Geospatial.Vectors.Vector vector = _localCoordinates.FaceNormal[fIdx, i];
				Geospatial.Vectors.Vector vector2 = ws.CellVelocity[faceCellA2];
				double x = vector2.X;
				double y = vector2.Y;
				Geospatial.Vectors.Vector other = new Geospatial.Vectors.Vector(cellCurvatureLocalCoordinatesTerm * x * y, (0.0 - cellCurvatureLocalCoordinatesTerm) * x * x);
				double valueA = ((i == 0) ? _hmesh.FaceAlpha[fIdx].ValueA : _hmesh.FaceAlpha[fIdx].ValueB);
				inertialTerm += valueA * vector.Dot(other);
			}
		}
		return inertialTerm;
	}

	public void ComputeFaceVelComp(MeshWaterState ws, List<int> faceIndexes)
	{
		if (Control.faceTangentialVelocityMethod == SolverControl.FaceTangentialVelocityMethod.CellBased)
		{
			threadFor.Run(faceIndexes.Count, (int begin, int end) =>
			{
				List<int> faceIndexesList = faceIndexes;
				MeshWaterState ws2 = ws;
				Geospatial.Vectors.Vector[] cellVelocity = ws.CellVelocity;
				Geospatial.Vectors.Vector[] faceNormal = _hmesh.FaceNormal;
				for (int i = begin; i <= end; i++)
				{
					int faceIndex = faceIndexesList[i];
					ComputeFaceVelCompONE(ws2, faceIndex, cellVelocity, faceNormal[faceIndex]);
				}
			});
			return;
		}
		threadFor.Run(faceIndexes.Count, (int begin, int end) =>
		{
			List<int> faceIndexes2 = faceIndexes;
			MeshWaterState ws2 = ws;
			double[] faceVelocity = ws.FaceVelocity;
			for (int i = begin; i <= end; i++)
			{
				int fIdx = faceIndexes2[i];
				ComputeFaceVelCompWLSONE(ws2, fIdx, faceVelocity);
			}
		});
	}

	public void ComputeFaceVelCompONE(MeshWaterState ws, int fIdx, Geospatial.Vectors.Vector[] cellVelocity, Geospatial.Vectors.Vector fNorm)
	{
		int faceCellA = _meshConnectivity.FaceCellA[fIdx];
		int faceCellB = _meshConnectivity.FaceCellB[fIdx];
		if (faceCellB == -1)
		{
			Geospatial.Vectors.Vector input = cellVelocity[faceCellA];
			Geospatial.Vectors.Vector vector = _localCoordinates.RotateToFace(input, fIdx, 0);
			if (ws.FaceArea[fIdx] > 0.0)
			{
				double faceVelocity = ws.FaceVelocity[fIdx];
				double y = vector.Y;
				ws.FaceVelTangA[fIdx] = (ws.FaceVelTangB[fIdx] = y);
				ws.FaceVelMag[fIdx] = (float)Math.Sqrt(faceVelocity * faceVelocity + y * y);
			}
			else
			{
				ws.FaceVelTangA[fIdx] = vector.Y;
				ws.FaceVelTangB[fIdx] = 0.0;
				ws.FaceVelMag[fIdx] = 0f;
			}
		}
		else
		{
			Geospatial.Vectors.Vector vector2 = _localCoordinates.RotateToFace(cellVelocity[faceCellA], fIdx, 0);
			Geospatial.Vectors.Vector vector3 = _localCoordinates.RotateToFace(cellVelocity[faceCellB], fIdx, 1);
			if (ws.FaceArea[fIdx] > 0.0)
			{
				double faceVelocity2 = ws.FaceVelocity[fIdx];
				double faceVelTangB = 0.5 * (vector2.Y + vector3.Y);
				ws.FaceVelTangA[fIdx] = (ws.FaceVelTangB[fIdx] = faceVelTangB);
				ws.FaceVelMag[fIdx] = (float)Math.Sqrt(faceVelocity2 * faceVelocity2 + faceVelTangB * faceVelTangB);
			}
			else
			{
				ws.FaceVelTangA[fIdx] = vector2.Y;
				ws.FaceVelTangB[fIdx] = vector3.Y;
				ws.FaceVelMag[fIdx] = 0f;
			}
		}
	}

	public void ComputeFaceVelCompWLSONE(MeshWaterState ws, int fIdx, double[] FaceVelocity)
	{
		_ = _meshConnectivity.FaceCellA[fIdx];
		if (_meshConnectivity.FaceCellB[fIdx] == -1)
		{
			double tangentialSideA = HydraulicMeshComputer.FaceComputes.TangentialSideA(fIdx, ws.FaceVelocity.AsSpan());
			if (ws.FaceArea[fIdx] > 0.0)
			{
				double faceVelocity = ws.FaceVelocity[fIdx];
				ws.FaceVelTangA[fIdx] = (ws.FaceVelTangB[fIdx] = tangentialSideA);
				ws.FaceVelMag[fIdx] = (float)Math.Sqrt(faceVelocity * faceVelocity + tangentialSideA * tangentialSideA);
			}
			else
			{
				ws.FaceVelTangA[fIdx] = tangentialSideA;
				ws.FaceVelTangB[fIdx] = 0.0;
				ws.FaceVelMag[fIdx] = 0f;
			}
		}
		else if (ws.FaceArea[fIdx] > 0.0)
		{
			double faceVelocity2 = ws.FaceVelocity[fIdx];
			double faceVelTangB = HydraulicMeshComputer.FaceComputes.Tangential(fIdx, ws.FaceVelocity.AsSpan());
			ws.FaceVelTangA[fIdx] = (ws.FaceVelTangB[fIdx] = faceVelTangB);
			ws.FaceVelMag[fIdx] = (float)Math.Sqrt(faceVelocity2 * faceVelocity2 + faceVelTangB * faceVelTangB);
		}
		else
		{
			ws.FaceVelTangA[fIdx] = HydraulicMeshComputer.FaceComputes.TangentialSideA(fIdx, ws.FaceVelocity.AsSpan());
			ws.FaceVelTangB[fIdx] = HydraulicMeshComputer.FaceComputes.TangentialSideB(fIdx, ws.FaceVelocity.AsSpan());
			ws.FaceVelMag[fIdx] = 0f;
		}
	}

	public void ComputeViscosity(MeshWaterState ws, List<int> faceIndexes, List<int> cellIndexes)
	{
		float[] cellFactors = (Control.dtLimitDiffusion ? CellEddyVisFactor : null);
		ComputeTurbulentMixing(ws, faceIndexes, cellIndexes, CellDiffVarExp, cellFactors);
	}

	public void ComputeWeirFaceVelocity(MeshWaterState ws, double[] newFaceVelocity, double dt, List<int> faceIndexes)
	{
		threadFor.Run(HydraulicStructures.Weirs.Count, (int start, int end) =>
		{
			ComputeWeirFaceVelocityMANY(ws, newFaceVelocity, dt, faceIndexes, start, end);
		});
	}

	private void ComputeWeirFaceVelocityMANY(MeshWaterState ws, double[] newFaceVelocity, double dt, List<int> faceIndexes, int start, int end)
	{
		double g_dt = dt * grav;
		double[] faceInverseDistances = FaceInverseDistances;
		for (int i = start; i <= end; i++)
		{
			ComputeWeirFaceVelocityONE(ws, newFaceVelocity, dt, g_dt, faceIndexes, faceInverseDistances, HydraulicStructures.Weirs[i]);
		}
	}

	public void ComputeWeirFaceVelocityONE(MeshWaterState ws, double[] newFaceVelocity, double dt, double g_dt, List<int> faceIndexes, double[] faceDistInv, Weir weir)
	{
		for (int i = 0; i < weir.Faces.Length; i++)
		{
			int fIdx = weir.Faces[i];
			if (!faceIndexes.Contains(fIdx) || fIdx < 0)
			{
				continue;
			}
			double weirCrest = weir.WeirCrest[i];
			Face face = _mesh.Faces[fIdx];
			int cellA = face.cellA;
			int cellB = face.cellB;
			double cellElevation = ws.CellElevation[cellA];
			double cellElevation2 = ws.CellElevation[cellB];
			double weirApproachVel = weir.GetWeirApproachVel(ws, _mesh, _hmesh, cellA, fIdx);
			double weirApproachVel2 = weir.GetWeirApproachVel(ws, _mesh, _hmesh, cellB, fIdx);
			double cellElevationWeirApproachVelTerm = cellElevation + weirApproachVel * weirApproachVel / (2.0 * grav);
			double cellElevation2WeirApproachVel2Term = cellElevation2 + weirApproachVel2 * weirApproachVel2 / (2.0 * grav);
			if (Math.Max(cellElevation, cellElevation2) <= weirCrest || ws.FaceArea[fIdx] <= 0.0)
			{
				newFaceVelocity[fIdx] = 0.0;
				continue;
			}
			bool sign = cellElevationWeirApproachVelTerm > cellElevation2WeirApproachVel2Term;
			double cellElevation3 = (sign ? cellElevation : cellElevation2);
			double cellElevation4 = (sign ? cellElevation2 : cellElevation);
			double weirApproachVel3 = (sign ? weirApproachVel : weirApproachVel2);
			double signTerm = (sign ? 1.0 : (-1.0));
			double cellElevation3WeirCrestTerm = cellElevation3 - weirCrest + weirApproachVel3 * weirApproachVel3 / (2.0 * grav);
			double computed = 2.0 / 3.0 * cellElevation3WeirCrestTerm;
			double cellElevation4WeirCrestTerm = Math.Max(cellElevation4, weirCrest + computed);
			double savedWeirVal = cellElevation4WeirCrestTerm;
			double savedWeirVal2 = weir.SavedWeirVal[i];
			double computed2 = 0.6;
			savedWeirVal = computed2 * savedWeirVal2 + (1.0 - computed2) * savedWeirVal;
			weir.SavedWeirVal[i] = savedWeirVal;
			double cellElevation5 = (sign ? cellElevation3 : cellElevation4WeirCrestTerm);
			double cellElevation6 = (sign ? cellElevation4WeirCrestTerm : cellElevation3);
			double faceVelocity = ws.FaceVelocity[fIdx];
			double faceVelocity2 = faceVelocity;
			double computed3 = 0.0;
			if (Control.advectionMethod != SolverControl.AdvectionMethod.None)
			{
				double faceVelocityWeirApproachVel3Term = signTerm * 0.5 * (faceVelocity * faceVelocity - weirApproachVel3 * weirApproachVel3) * faceDistInv[fIdx];
				faceVelocity2 -= dt * faceVelocityWeirApproachVel3Term;
			}
			if (Control.streamwiseInertialTerm)
			{
				faceVelocity2 += dt * ComputeInertialTerm(fIdx, ws);
			}
			double cellElevation6CellElevation5Term = (cellElevation6 - cellElevation5) * faceDistInv[fIdx];
			faceVelocity2 -= g_dt * cellElevation6CellElevation5Term;
			double kMinorLossWeirTerm = signTerm * 0.5 * weir.KMinorLoss * faceVelocity * faceVelocity * faceDistInv[fIdx];
			faceVelocity2 -= dt * kMinorLossWeirTerm;
			double computed4 = 0.0;
			double newFaceVelocity2 = faceVelocity2 / (1.0 + dt * (computed4 + computed3));
			newFaceVelocity[fIdx] = newFaceVelocity2;
			_propertyTables.UpdateFaceONE(in fIdx, ws.FacePriorIndexes, ws.FaceElevation, ws.FaceArea, ws.FaceHydraulicRadius, ws.FaceHydraulicDepth, ws.FaceMannings, ws.FaceNonlinFrictCoeff, ws.FaceShearFluxCoeff, ws.FaceConveyance, in savedWeirVal);
			if (cellB >= 0 && ((newFaceVelocity[fIdx] > 0.0 && ws.CellVolume[cellA] <= 0.0) || (newFaceVelocity[fIdx] < 0.0 && ws.CellVolume[cellB] <= 0.0)))
			{
				newFaceVelocity[fIdx] = 0.0;
			}
		}
	}

	public void PrepareForwardBackLts()
	{
		lMaxLts = (byte)Control.LtsLevels;
		if (lMaxLts < 1 || lMaxLts >= 63)
		{
			throw new SolverException($"LTS levels {lMaxLts} is invalid");
		}
		mMaxLts = 1 << (int)lMaxLts;
		mLts = 0;
		local_cell_steps = 0L;
		PrepareForwardBack();
		_waterState.CellLevel0Lts = new byte[_mesh.CellCount];
		_waterState.CellLevel2Lts = new byte[_mesh.CellCount];
		_waterState.CellLevel4Lts = new byte[_mesh.CellCount];
		_waterState.FaceLevel1Lts = new byte[_mesh.FaceCount];
		_waterState.FaceLevel3Lts = new byte[_mesh.FaceCount];
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			_waterState.CellLevel0Lts[i] = 1;
		}
		ILsCellVolumeLts = new List<List<int>>();
		ILsFaceVelocityLts = new List<List<int>>();
		ILsCellVelocityLts = new List<List<int>>();
		ILsFaceVelFrictLts = new List<List<int>>();
		volSlotIsStale = new bool[lMaxLts + 1];
		velSlotIsStale = new bool[lMaxLts + 1];
		for (int j = 0; j < lMaxLts + 1; j++)
		{
			ILsCellVolumeLts.Add(new List<int>());
			ILsCellVelocityLts.Add(new List<int>());
			ILsFaceVelocityLts.Add(new List<int>());
			ILsFaceVelFrictLts.Add(new List<int>());
			volSlotIsStale[j] = true;
			velSlotIsStale[j] = true;
		}
		if (CellDtMax == null)
		{
			CellDtMax = new double[_mesh.CellCount];
		}
	}

	public void PrepareForwardBackForwardLts()
	{
		PrepareForwardBackLts();
	}

	private void DebugSummary(MeshWaterState ws, double absTime, int cIdx = 942)
	{
		ILCellVolume.Contains(cIdx);
		Span<int> faces = _mesh.Cells[cIdx].Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			_ = _mesh.Faces[faceIndex].cellA;
			ILFaceVelocity.Contains(faceIndex);
		}
	}

	public void LevelSummary(MeshWaterState ws, double dt)
	{
		int[] intValues = new int[lMaxLts + 1];
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			intValues[ws.CellLevel2Lts[i]]++;
		}
		for (int j = 0; j < lMaxLts + 1; j++)
		{
		}
	}

	public ISolver.Status SolveStepLts(double absTime, double dt, double dtPrevious)
	{
		Stopwatch stopwatch = new Stopwatch();
		MeshWaterState ws = _waterState;
		ws.ErrorCellVolumeNegative = false;
		double dtOverMMaxLts = dt / (double)mMaxLts;
		if (dtPrevious == 0.0)
		{
			if (Control.stepType == SolverControl.StepType.FWDBACKFWD_LTS)
			{
				mIncrLts = mMaxLts / 2;
			}
			else
			{
				mIncrLts = mMaxLts;
			}
			stopwatch.Restart();
			CalculateCellLevel0Lts(ws, dtOverMMaxLts);
			if (HydraulicStructures.HasStructures)
			{
				CalculateStructLevelLts(ws, 0, 0);
			}
			if (BoundaryConditions.HasInternalFlows)
			{
				CalculateInternalFlowLevelLts(ws);
			}
			ticks[9] += stopwatch.ElapsedTicks;
		}
		bool condition = false;
		bool condition2 = false;
		for (int i = 0; i <= MaxLocalRetries; i++)
		{
			if (condition2)
			{
				RevertState();
				condition2 = false;
			}
			stopwatch.Restart();
			UpdateHalosLts(ws);
			if (HydraulicStructures.HasStructures)
			{
				UpdateHalosLtsStruct(ws);
			}
			ticks[10] += stopwatch.ElapsedTicks;
			for (int j = 0; j < lMaxLts + 1; j++)
			{
				volSlotIsStale[j] = true;
				velSlotIsStale[j] = true;
			}
			for (mLts = 0; mLts < mMaxLts; mLts += mIncrLts)
			{
				if (Control.stepType == SolverControl.StepType.FWDBACK_LTS)
				{
					stopwatch.Restart();
					UpdateIndexListsIndexParallel<FBIndexListDefinitions>(mLts, ws);
					ticks[11] += stopwatch.ElapsedTicks;
					local_cell_steps += ILCellVelocity.Count;
					ApplyNormalDepthBoundaryConditions(ws, absTime);
					stopwatch.Restart();
					UpdateCellVolumeLts(ws, newCellVolume, dtOverMMaxLts, ILCellVolume, ws.CellLevel2Lts);
					ticks[5] += stopwatch.ElapsedTicks;
					IncrementConservation(dtOverMMaxLts * (double)mIncrLts);
					stopwatch.Restart();
					ComputeCellVelocity(ws, ws.CellVelocity, ILCellVelocity);
					ticks[8] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					UpdateFaceState(ws, ILFaceVelocity);
					ticks[6] += stopwatch.ElapsedTicks;
					if (Control.advectionMethod != SolverControl.AdvectionMethod.None)
					{
						stopwatch.Restart();
						ComputeCellAdvVar(ws, ILCellVelocity);
						ticks[2] += stopwatch.ElapsedTicks;
					}
					stopwatch.Restart();
					ComputeFaceVelComp(ws, ILFaceVelFrict);
					ticks[3] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					ComputeViscosity(ws, ILFaceVelFrict, ILCellVelocity);
					ticks[7] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					ComputeFaceVelocitySWELts(ws, newFaceVelocity, absTime, dtOverMMaxLts, ILFaceVelocity, ws.FaceLevel1Lts);
					ticks[1] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					threadFor.Run(ILFaceVelocity.Count, (int start, int stop) =>
					{
						UpdateFaceVelocityAndStateMANY(ws, newFaceVelocity, ILFaceVelocity, start, stop);
					});
					ticks[6] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					if (HydraulicStructures.HasStructures)
					{
						HydraulicStructures.ApplyLTS(absTime, dtOverMMaxLts, ws, _mesh, _propertyTables, _hmesh, mLts, mIncrLts, isFBF: false);
						if (HydraulicStructures.HasSourceSinks)
						{
							CellWaterSources.MarkCurrentRatesDirty();
							CellWaterSources.RefreshCurrentRates();
						}
					}
					ticks[4] += stopwatch.ElapsedTicks;
				}
				else
				{
					stopwatch.Restart();
					UpdateIndexListsIndexParallel<FBFIndexListDefinitions>(mLts, ws);
					ticks[11] += stopwatch.ElapsedTicks;
					local_cell_steps += ILCellVelocity.Count;
					stopwatch.Restart();
					UpdateFaceState(ws, ILFaceVelocity);
					ticks[6] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					ComputeCellVelocity(ws, ws.CellVelocity, ILCellVelocity);
					ticks[8] += stopwatch.ElapsedTicks;
					if (Control.advectionMethod != SolverControl.AdvectionMethod.None)
					{
						stopwatch.Restart();
						ComputeCellAdvVar(ws, ILCellVelocity);
						ticks[2] += stopwatch.ElapsedTicks;
					}
					stopwatch.Restart();
					ComputeFaceVelComp(ws, ILFaceVelFrict);
					ticks[3] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					ComputeViscosity(ws, ILFaceVelFrict, ILCellVelocity);
					ticks[7] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					ComputeFaceVelocitySWELts(ws, newFaceVelocity, absTime, dtOverMMaxLts, ILFaceVelocity, ws.FaceLevel1Lts);
					ticks[1] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					threadFor.Run(ILFaceVelocity.Count, (int start, int stop) =>
					{
						UpdateFaceVelocityAndStateMANY(ws, newFaceVelocity, ILFaceVelocity, start, stop);
					});
					ticks[6] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					if (HydraulicStructures.HasStructures)
					{
						HydraulicStructures.ApplyLTS(absTime, dtOverMMaxLts, ws, _mesh, _propertyTables, _hmesh, mLts, mIncrLts, isFBF: true);
						if (HydraulicStructures.HasSourceSinks)
						{
							CellWaterSources.MarkCurrentRatesDirty();
							CellWaterSources.RefreshCurrentRates();
						}
					}
					ticks[4] += stopwatch.ElapsedTicks;
					stopwatch.Restart();
					UpdateCellVolumeLts(ws, newCellVolume, 0.5 * dtOverMMaxLts, ILCellVolume, ws.CellLevel2Lts);
					ticks[5] += stopwatch.ElapsedTicks;
					IncrementConservation((double)mIncrLts * dtOverMMaxLts);
				}
				if (MaxLocalRetries > 0 && ws.ErrorCellVolumeNegative)
				{
					checker.reason = StepChecker.RejectReason.LOCAL_VOLUME_NEGATIVE;
					if (i >= MaxLocalRetries)
					{
						using (IEnumerator<int> intEnumerator = ILCellVolume.Where((int ordinal) => ws.CellVolume[ordinal] < 0.0).GetEnumerator())
						{
							if (intEnumerator.MoveNext())
							{
								int current = intEnumerator.Current;
								checker.rejectCell = current;
								return ISolver.Status.VOLUME_NEGATIVE;
							}
						}
						checker.rejectCell = -1;
						return ISolver.Status.VOLUME_NEGATIVE;
					}
					int ordinal2 = 0;
					byte b = byte.MaxValue;
					foreach (int rejectCell in ILCellVolume.Where((int ordinal3) => ws.CellVolume[ordinal3] < 0.0))
					{
						ordinal2++;
						checker.rejectCell = rejectCell;
						if (ws.CellLevel0Lts[rejectCell] > 1)
						{
							ws.CellLevel0Lts[rejectCell]--;
							b = Math.Min(b, ws.CellLevel0Lts[rejectCell]);
							continue;
						}
						condition = true;
						break;
					}
					Solver._logger.LogTrace("End of local step and {ErrorCount} volumes went negative", ordinal2);
					if (condition)
					{
						Solver._logger.LogTrace("Could not demote. Will abort step");
						break;
					}
					mIncrLts = Math.Min(mIncrLts, 1 << b - 1);
					condition2 = true;
					break;
				}
			}
			if (!condition2)
			{
				break;
			}
		}
		stopwatch.Restart();
		if (Control.stepType == SolverControl.StepType.FWDBACK_LTS)
		{
			UpdateFaceState(ws, ILFaceAll);
		}
		else
		{
			threadFor.Run(ILFaceAll.Count, (int start, int stop) =>
			{
				UpdateFaceStateAndFlowMANY(ws, ILFaceAll, start, stop);
			});
		}
		ticks[6] += stopwatch.ElapsedTicks;
		if (!condition)
		{
			return ISolver.Status.OK;
		}
		return ISolver.Status.VOLUME_NEGATIVE;
	}

	public void UpdateHalosLts(MeshWaterState ws)
	{
		if (lInactiveLts != lMaxLts)
		{
			BufferLevelsLts(ws.CellLevel0Lts, ws.FaceLevel1Lts);
		}
		CellToFaceHalo(ws.CellLevel0Lts, ws.FaceLevel1Lts);
		FaceToCellHalo(ws.FaceLevel1Lts, ws.CellLevel2Lts);
		CellToFaceHalo(ws.CellLevel2Lts, ws.FaceLevel3Lts);
		FaceToCellHalo(ws.FaceLevel3Lts, ws.CellLevel4Lts);
	}

	public void UpdateHalosLtsStruct(MeshWaterState ws)
	{
		int count = HydraulicStructures.AllHydraulicStructures.Count;
		for (int i = 0; i < count; i++)
		{
			if (!CalculateStructLevelLts(ws, 2, 0))
			{
				break;
			}
			UpdateHalosLts(ws);
		}
	}

	public void BufferLevelsLts(byte[] cellLevel, byte[] faceLevel)
	{
		CellToFaceHalo(cellLevel, faceLevel);
		threadFor.Run(_mesh.CellCount, (int start, int stop) =>
		{
			MeshConnectivity meshConnectivity = _meshConnectivity;
			byte[] faceLevelArray = faceLevel;
			byte[] cellLevelArray = cellLevel;
			for (int i = start; i <= stop; i++)
			{
				ReadOnlySpan<int> cellFaces = meshConnectivity.GetCellFaces(i);
				for (int j = 0; j < cellFaces.Length; j++)
				{
					if (cellLevelArray[i] == lInactiveLts && faceLevelArray[cellFaces[j]] < lMaxLts)
					{
						cellLevelArray[i] = lMaxLts;
					}
				}
			}
		});
	}

	public void CellToFaceHalo(byte[] cellLevel, byte[] faceLevel)
	{
		threadFor.Run(_mesh.FaceCount, (int start, int stop) =>
		{
			int[] faceCellA = _meshConnectivity.FaceCellA;
			int[] faceCellB = _meshConnectivity.FaceCellB;
			byte[] faceLevelArray = faceLevel;
			byte[] cellLevelArray = cellLevel;
			for (int i = start; i <= stop; i++)
			{
				int faceCellA2 = faceCellA[i];
				int faceCellB2 = faceCellB[i];
				int faceCellB3 = ((faceCellB2 >= 0) ? faceCellB2 : faceCellA2);
				faceLevelArray[i] = Math.Min(cellLevelArray[faceCellA2], cellLevelArray[faceCellB3]);
			}
		});
	}

	public void FaceToCellHalo(byte[] faceLevel, byte[] cellLevel)
	{
		threadFor.Run(_mesh.CellCount, (int start, int stop) =>
		{
			MeshConnectivity meshConnectivity = _meshConnectivity;
			byte[] faceLevelArray = faceLevel;
			byte[] cellLevelArray = cellLevel;
			for (int i = start; i <= stop; i++)
			{
				ReadOnlySpan<int> cellFaces = meshConnectivity.GetCellFaces(i);
				cellLevelArray[i] = byte.MaxValue;
				for (int j = 0; j < cellFaces.Length; j++)
				{
					cellLevelArray[i] = Math.Min(cellLevelArray[i], faceLevelArray[cellFaces[j]]);
				}
			}
		});
	}

	public bool CalculateStructLevelLts(MeshWaterState ws, int checkLevel, int assignLevel)
	{
		bool result = false;
		byte[] cellLevel0Lts = ((checkLevel == 0) ? ws.CellLevel0Lts : ws.CellLevel2Lts);
		byte[] cellLevel0LtsArray = ((assignLevel == 0) ? ws.CellLevel0Lts : ws.CellLevel2Lts);
		foreach (HydraulicStructure allHydraulicStructure in HydraulicStructures.AllHydraulicStructures)
		{
			byte b = byte.MaxValue;
			foreach (int union in allHydraulicStructure.UpCells.Union(allHydraulicStructure.DnCells))
			{
				b = Math.Min(b, cellLevel0Lts[union]);
			}
			if (allHydraulicStructure.CellLevelLts != b)
			{
				result = true;
			}
			allHydraulicStructure.CellLevelLts = b;
			foreach (int union2 in allHydraulicStructure.UpCells.Union(allHydraulicStructure.DnCells))
			{
				cellLevel0LtsArray[union2] = b;
			}
		}
		return result;
	}

	public bool CalculateInternalFlowLevelLts(MeshWaterState ws)
	{
		int[] cellIndicesInternalFlows = BoundaryConditions.CellIndicesInternalFlows;
		foreach (int cellIndicesInternalFlow in cellIndicesInternalFlows)
		{
			if (ws.CellLevel0Lts[cellIndicesInternalFlow] == lInactiveLts && BoundaryConditions.CellInternalFlows[cellIndicesInternalFlow] != 0.0)
			{
				ws.CellLevel0Lts[cellIndicesInternalFlow] = lMaxLts;
			}
		}
		return true;
	}

	public void CalculateCellLevel0Lts(MeshWaterState ws, double dt)
	{
		int minLevel;
		if (ltsStrategy == SolverControl.LtsStepStrategy.CONSTANT)
		{
			mIncrLts = 1;
		}
		else if (ltsStrategy == SolverControl.LtsStepStrategy.COURANT)
		{
			minLevel = lMaxLts;
			threadFor.Run(ILCellAll.Count, CalculateCellLevel0LtsMany);
			mIncrLts = Math.Min(mIncrLts, 1 << minLevel - 1);
		}
		void CalculateCellLevel0LtsMany(int start, int stop)
		{
			List<int> iLCellAll = ILCellAll;
			double[] cellDtMax = CellDtMax;
			int mMaxLts2 = mMaxLts;
			double timestepSafetyFactor = Control.TimestepSafetyFactor;
			double dt2 = dt;
			byte[] cellLevel0Lts = ws.CellLevel0Lts;
			int lMaxLts2 = lMaxLts;
			byte b = lInactiveLts;
			for (int i = start; i <= stop; i++)
			{
				int iLCellAll2 = iLCellAll[i];
				double cellDtMax2 = cellDtMax[iLCellAll2];
				byte b2;
				if (cellDtMax2 == 17280000.0)
				{
					b2 = b;
				}
				else
				{
					if (cellDtMax2 <= 0.0)
					{
						continue;
					}
					b2 = (byte)BitOperations.Log2((uint)Math.Min(mMaxLts2, timestepSafetyFactor * cellDtMax2 / dt2));
					b2 = Math.Max((byte)1, b2);
				}
				if (b2 != cellLevel0Lts[iLCellAll2])
				{
					cellLevel0Lts[iLCellAll2] = b2;
				}
				lMaxLts2 = Math.Min(lMaxLts2, b2);
				ThreadFor.InterlockedInplaceMin(ref minLevel, lMaxLts2);
			}
		}
	}

	public void UpdateIndexListsIndexParallel<TIndexListDefinitions>(int m, MeshWaterState ws) where TIndexListDefinitions : struct, IIndexListDefinitions
	{
		int m2 = m + mIncrLts;
		TIndexListDefinitions.CalculateSlots(m, m2, lMaxLts, out var volSlot, out var velSlot);
		ILCellVolume = ILsCellVolumeLts[volSlot];
		ILFaceVelocity = ILsFaceVelocityLts[velSlot];
		ILCellVelocity = ILsCellVelocityLts[velSlot];
		ILFaceVelFrict = ILsFaceVelFrictLts[velSlot];
		if (!volSlotIsStale[volSlot] && !velSlotIsStale[velSlot])
		{
			return;
		}
		if (partILCellVolume == null)
		{
			partILCellVolume = new List<int>[Control.numCores];
			partILCellVelocity = new List<int>[Control.numCores];
			partILFaceVelocity = new List<int>[Control.numCores];
			partILFaceVelFrict = new List<int>[Control.numCores];
			for (int i = 0; i < Control.numCores; i++)
			{
				partILCellVolume[i] = new List<int>();
				partILCellVelocity[i] = new List<int>();
				partILFaceVelocity[i] = new List<int>();
				partILFaceVelFrict[i] = new List<int>();
			}
		}
		if (volSlotIsStale[volSlot])
		{
			ILCellVolume.Clear();
		}
		if (velSlotIsStale[velSlot])
		{
			ILCellVelocity.Clear();
			ILFaceVelocity.Clear();
			ILFaceVelFrict.Clear();
		}
		Action<int, int> action = (int rank, int rank1) =>
		{
			double rankOverNumCores = (float)rank / (float)Control.numCores;
			double rank1OffsetOverNumCores = (float)(rank1 + 1) / (float)Control.numCores;
			int faceLevel3LtsWsTerm = (int)((double)ws.FaceLevel3Lts.Length * rankOverNumCores);
			int count = (int)((double)ws.FaceLevel3Lts.Length * rank1OffsetOverNumCores) - 1;
			List<int> partILFaceVelFrictList = partILFaceVelFrict[rank];
			List<int> partILFaceVelocityList = partILFaceVelocity[rank];
			List<int> partILCellVelocityList = partILCellVelocity[rank];
			List<int> partILCellVolumeList = partILCellVolume[rank];
			int cellLevel2LtsWsTerm = (int)((double)ws.CellLevel2Lts.Length * rankOverNumCores);
			int count2 = (int)((double)ws.CellLevel2Lts.Length * rank1OffsetOverNumCores) - 1;
			if (velSlotIsStale[velSlot])
			{
				partILFaceVelFrictList.Clear();
				partILFaceVelocityList.Clear();
				partILCellVelocityList.Clear();
				for (int j = faceLevel3LtsWsTerm; j <= count; j++)
				{
					if (TIndexListDefinitions.FaceVelFrict(m, m2, ws.FaceLevel3Lts[j]))
					{
						partILFaceVelFrictList.Add(j);
					}
					if ((FaceFlags[j] & FaceFlag.HAS_FLOW) != 0 && TIndexListDefinitions.FaceVelocity(m, m2, ws.FaceLevel1Lts[j]))
					{
						partILFaceVelocityList.Add(j);
					}
				}
				for (int k = cellLevel2LtsWsTerm; k <= count2; k++)
				{
					if (TIndexListDefinitions.CellVelocity(m, m2, ws.CellLevel4Lts[k]))
					{
						partILCellVelocityList.Add(k);
					}
				}
			}
			if (volSlotIsStale[volSlot])
			{
				partILCellVolumeList.Clear();
				if (m == 0)
				{
					for (int l = cellLevel2LtsWsTerm; l <= count2; l++)
					{
						if (TIndexListDefinitions.CellVolumeM0(ws.CellLevel2Lts[l], lMaxLts))
						{
							partILCellVolumeList.Add(l);
						}
					}
				}
				else
				{
					for (int n = cellLevel2LtsWsTerm; n <= count2; n++)
					{
						if (TIndexListDefinitions.CellVolume(m, m2, ws.CellLevel2Lts[n]))
						{
							partILCellVolumeList.Add(n);
						}
					}
				}
			}
		};
		threadFor.Run(Control.numCores, action, 1);
		for (int intervalIndex = 0; intervalIndex < Control.numCores; intervalIndex++)
		{
			if (volSlotIsStale[volSlot])
			{
				ILCellVolume.AddRange(partILCellVolume[intervalIndex]);
			}
			if (velSlotIsStale[velSlot])
			{
				ILCellVelocity.AddRange(partILCellVelocity[intervalIndex]);
				ILFaceVelocity.AddRange(partILFaceVelocity[intervalIndex]);
				ILFaceVelFrict.AddRange(partILFaceVelFrict[intervalIndex]);
			}
		}
		volSlotIsStale[volSlot] = false;
		velSlotIsStale[velSlot] = false;
	}

	public void UpdateIndexLists<TIndexListDefinitions>(int m, MeshWaterState ws) where TIndexListDefinitions : struct, IIndexListDefinitions
	{
		int m2 = m + mIncrLts;
		TIndexListDefinitions.CalculateSlots(m, m2, lMaxLts, out var volSlot, out var velSlot);
		ILCellVolume = ILsCellVolumeLts[volSlot];
		if (volSlotIsStale[volSlot])
		{
			ILCellVolume.Clear();
			volSlotIsStale[volSlot] = false;
			if (m == 0)
			{
				for (int i = 0; i < _mesh.CellCount; i++)
				{
					if (TIndexListDefinitions.CellVolumeM0(ws.CellLevel2Lts[i], lMaxLts))
					{
						ILCellVolume.Add(i);
					}
				}
			}
			else
			{
				for (int j = 0; j < _mesh.CellCount; j++)
				{
					if (TIndexListDefinitions.CellVolume(m, m2, ws.CellLevel2Lts[j]))
					{
						ILCellVolume.Add(j);
					}
				}
			}
		}
		ILFaceVelocity = ILsFaceVelocityLts[velSlot];
		ILCellVelocity = ILsCellVelocityLts[velSlot];
		ILFaceVelFrict = ILsFaceVelFrictLts[velSlot];
		if (!velSlotIsStale[velSlot])
		{
			return;
		}
		ILCellVelocity.Clear();
		ILFaceVelocity.Clear();
		ILFaceVelFrict.Clear();
		velSlotIsStale[velSlot] = false;
		for (int k = 0; k < _mesh.FaceCount; k++)
		{
			if (TIndexListDefinitions.FaceVelFrict(m, m2, ws.FaceLevel3Lts[k]))
			{
				ILFaceVelFrict.Add(k);
			}
			if ((FaceFlags[k] & FaceFlag.HAS_FLOW) != 0 && TIndexListDefinitions.FaceVelocity(m, m2, ws.FaceLevel1Lts[k]))
			{
				ILFaceVelocity.Add(k);
			}
		}
		for (int l = 0; l < _mesh.CellCount; l++)
		{
			if (TIndexListDefinitions.CellVelocity(m, m2, ws.CellLevel4Lts[l]))
			{
				ILCellVelocity.Add(l);
			}
		}
	}

	public void UpdateIndexListsTaskParallel<TIndexListDefinitions>(int m, MeshWaterState ws) where TIndexListDefinitions : struct, IIndexListDefinitions
	{
		int m2 = m + mIncrLts;
		TIndexListDefinitions.CalculateSlots(m, m2, lMaxLts, out var volSlot, out var velSlot);
		_ = m;
		ILCellVolume = ILsCellVolumeLts[volSlot];
		ILFaceVelocity = ILsFaceVelocityLts[velSlot];
		ILCellVelocity = ILsCellVelocityLts[velSlot];
		ILFaceVelFrict = ILsFaceVelFrictLts[velSlot];
		Action action = () =>
		{
			List<int> iLsCellVolumeLt = ILsCellVolumeLts[volSlot];
			byte[] cellLevel2Lts = ws.CellLevel2Lts;
			iLsCellVolumeLt.Clear();
			if (m == 0)
			{
				for (int i = 0; i < cellLevel2Lts.Length; i++)
				{
					if (TIndexListDefinitions.CellVolumeM0(cellLevel2Lts[i], lMaxLts))
					{
						iLsCellVolumeLt.Add(i);
					}
				}
			}
			else
			{
				for (int j = 0; j < cellLevel2Lts.Length; j++)
				{
					if (TIndexListDefinitions.CellVolume(m, m2, cellLevel2Lts[j]))
					{
						iLsCellVolumeLt.Add(j);
					}
				}
			}
			volSlotIsStale[volSlot] = false;
		};
		Action action2 = () =>
		{
			List<int> iLsFaceVelocityLt = ILsFaceVelocityLts[velSlot];
			iLsFaceVelocityLt.Clear();
			byte[] faceLevel1Lts = ws.FaceLevel1Lts;
			FaceFlag[] faceFlags = FaceFlags;
			for (int i = 0; i < faceLevel1Lts.Length; i++)
			{
				if ((faceFlags[i] & FaceFlag.HAS_FLOW) != 0 && TIndexListDefinitions.FaceVelocity(m, m2, faceLevel1Lts[i]))
				{
					iLsFaceVelocityLt.Add(i);
				}
			}
		};
		Action action3 = () =>
		{
			List<int> iLsCellVelocityLt = ILsCellVelocityLts[velSlot];
			iLsCellVelocityLt.Clear();
			byte[] cellLevel4Lts = ws.CellLevel4Lts;
			for (int i = 0; i < cellLevel4Lts.Length; i++)
			{
				if (TIndexListDefinitions.CellVelocity(m, m2, cellLevel4Lts[i]))
				{
					iLsCellVelocityLt.Add(i);
				}
			}
		};
		Action action4 = () =>
		{
			ILsFaceVelFrictLts[velSlot].Clear();
			byte[] faceLevel3Lts = ws.FaceLevel3Lts;
			for (int i = 0; i < faceLevel3Lts.Length; i++)
			{
				if (TIndexListDefinitions.FaceVelFrict(m, m2, faceLevel3Lts[i]))
				{
					ILFaceVelFrict.Add(i);
				}
			}
		};
		List<Task> tasks = new List<Task>();
		if (volSlotIsStale[volSlot])
		{
			tasks.Add(Task.Run(action));
			volSlotIsStale[volSlot] = false;
		}
		if (velSlotIsStale[velSlot])
		{
			tasks.Add(Task.Run(action2));
			tasks.Add(Task.Run(action3));
			tasks.Add(Task.Run(action4));
			velSlotIsStale[velSlot] = false;
		}
		Task.WaitAll(tasks.ToArray());
	}

	private void UpdateCellVolumeLts(MeshWaterState ws, double[] newCellVolume, double dt, List<int> cellIndexes, byte[] level)
	{
		PropertyTables.EnsurePartialTables();
		ws.InvalidateCellWaterSurfaceGradients();
		threadFor.Run(cellIndexes.Count, (int start, int stop) =>
		{
			MeshWaterState meshWaterState = ws;
			double[] newCellVolumeArray = newCellVolume;
			List<int> cellIndexesList = cellIndexes;
			double dt2 = dt;
			for (int i = start; i <= stop; i++)
			{
				int cellIndex = cellIndexesList[i];
				UpdateCellVolumeONE(meshWaterState, newCellVolumeArray, dt2 * (double)(1 << (int)level[cellIndex]), cellIndex);
			}
			double[] cellElevation = meshWaterState.CellElevation;
			double[] cellArea = meshWaterState.CellArea;
			double[] cellVolume = meshWaterState.CellVolume;
			float[] cellHydraulicDepth = meshWaterState.CellHydraulicDepth;
			int[] cellPriorIndexes = meshWaterState.CellPriorIndexes;
			for (int j = start; j <= stop; j++)
			{
				int cIdx = cellIndexesList[j];
				PropertyTables.UpdateCellsONE(in cIdx, cellElevation, cellArea, cellVolume, cellHydraulicDepth, cellPriorIndexes, in newCellVolumeArray, ref meshWaterState.ErrorCellVolumeNegative);
			}
		});
	}

	private void ComputeFaceVelocitySWELts(MeshWaterState ws, double[] newFaceVelocity, double absTime, double dt, List<int> faceIndexes, byte[] level)
	{
		if (faceIndexes.Count != 0)
		{
			if (HasNonOrthogonalCorrection)
			{
				UpdateFaceGradientData(ws);
			}
			threadFor.Run(faceIndexes.Count, (int start, int end) =>
			{
				ComputeFaceVelocitySWEMANYLts(ws, newFaceVelocity, absTime, dt, level, faceIndexes, start, end);
			});
		}
	}

	private void ComputeFaceVelocitySWEMANYLts(MeshWaterState ws, double[] newFaceVelocity, double absTime, double dt, byte[] level, List<int> faceIndexes, int start, int end)
	{
		Geospatial.Vectors.Vector[] faceNormal = _hmesh.FaceNormal;
		float[] faceVelMag = ws.FaceVelMag;
		double[] faceInverseDistances = FaceInverseDistances;
		FaceAB[] faceAlphaCellAInv = _hmesh.FaceAlphaCellAInv;
		for (int i = start; i <= end; i++)
		{
			int faceIndex = faceIndexes[i];
			double dtLevelTerm = dt * (double)(1 << (int)level[faceIndex]);
			double g_dt = dtLevelTerm * grav;
			ComputeFaceVelocityONE(ws, newFaceVelocity, dtLevelTerm, faceIndex, faceInverseDistances, faceVelMag, faceNormal, faceAlphaCellAInv, g_dt);
		}
	}
}
