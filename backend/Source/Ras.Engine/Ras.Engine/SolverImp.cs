using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Ras.Engine.Engine;
using Ras.Engine.Structures.Weirs;
using Utility.Logging;
using Utility.Matrices;
using Utility.Progress;

namespace Ras.Engine;

public abstract class SolverImp : Solver
{
	private readonly Lock _maxTimestepLock = new Lock();

	private long _dumpStepEndMicros;

	private long _dumpStepIndex = -1L;

	private bool _dumpFirstSaved;

	private LocalCoordinates _localCoordinates;

	protected override bool RefreshCellWaterSourcesAtStepStart => false;

	public double[] GuessCellElevation { get; protected set; }

	public double[] CellElevCorrInner { get; protected set; }

	public double[] CellElevResInner { get; protected set; }

	public double[] CellElevCorrOuter { get; protected set; }

	public bool[] MaskArray { get; protected set; }

	public double[] CellElevInterm
	{
		get
		{
			if (Convergence.OuterConv.Iterations > 1)
			{
				return CellElevAvg;
			}
			return _oldState.CellElevation;
		}
	}

	public double[] CellElevAvg { get; private set; }

	public SparseMatrix<double> MatrixNewton { get; protected set; }

	public int[] StencilFaces { get; protected set; }

	public int[] StencilSigns { get; protected set; }

	public double[] MatrixDiagonal { get; protected set; }

	public double[] FaceCoeff { get; protected set; }

	public double[] CellRHS { get; protected set; }

	public double[] CellRHSNewton { get; protected set; }

	public IMatrixSolver<double> MatSolver { get; protected set; }

	public IPreconditioner<double> Precond { get; protected set; }

	public IMatrixSolver<double> BlockMatSolver { get; protected set; }

	public PartialGrid PartGrid { get; protected set; }

	public PartialSolve PartSolve { get; protected set; }

	private IMatrixSolver<double> ActiveMatSolver
	{
		get
		{
			if (!Control.UseBlockSolver || PartGrid == null || !PartGrid.Enabled || BlockMatSolver == null)
			{
				return MatSolver;
			}
			return BlockMatSolver;
		}
	}

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

	public SolverImp(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState)
		: this(mesh, props, initialState, new SolverControl(), new SolverConvergence(), null)
	{
	}

	public SolverImp(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState, SolverControl control)
		: this(mesh, props, initialState, control, new SolverConvergence(), null)
	{
	}

	public SolverImp(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState, SolverControl control, SolverConvergence convergence, ComputeDriver driver)
		: base(mesh, props, initialState, control, convergence, driver)
	{
		Convergence = convergence;
	}

	public override void Prepare(ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		base.Prepare(pr);
		int cellCount = _mesh.CellCount;
		CellRHS = new double[cellCount];
		CellRHSNewton = new double[cellCount];
		GuessCellElevation = new double[cellCount];
		CellElevCorrInner = new double[cellCount];
		CellElevResInner = new double[cellCount];
		CellElevCorrOuter = new double[cellCount];
		MaskArray = new bool[cellCount];
		int faceCount = _mesh.FaceCount;
		CellElevAvg = new double[cellCount];
		_localCoordinates = new LocalCoordinates(_mesh, CellFlags);
		_hmesh.SetFaceTangential(null);
		Convergence.Prepare(Control.InnerControl, Control.OuterControl, cellCount);
		MatrixDiagonal = new double[cellCount];
		BuildMatrixStructureFull();
		FaceCoeff = new double[faceCount];
		MatrixConvergence<double> matrixConv = new MatrixConvergence<double>
		{
			ToleranceMaxMAE = 0.001,
			ToleranceMAE = 0.0001,
			ToleranceRMSE = 1E-05,
			MinIterations = 2,
			MaxIterations = 40,
			CheckConvergence = true
		};
		SolverRequest request = new SolverRequest(new SolverPolicy(Control.MatrixSolverPrimary, new SolverKind[] { SolverKind.PcgManaged }), new LinearSystemInfo(IsSparse: true, IsSymmetric: true, IsPositiveDefinite: true, 3), new SolverOptionsBundle
		{
			PardisoOpts = new PardisoOptions(),
			PcgNativeOpts = new PcgNativeOptions(),
			PcgManagedOpts = new PcgManagedOptions(matrixConv)
		});
		MatSolver = MatrixSolverFactory.Create(request);
		MatSolver.SetThreadBudget(Control.numCores);
		if (!MatSolver.IsDirectSolver)
		{
			Precond = new MIC<double>(0.95);
		}
		MatSolver.Prepare(MatrixNewton, Precond);
		Reporter.ReportMessage("Initialized Matrix Solver: " + MatSolver.SolverName);
		if (Control.UseBlockSolver)
		{
			BlockMatSolver = new BlockMatrixSolver(request)
			{
				TinyMaxSize = Control.BlockSolverTinyMaxSize,
				MediumMaxSize = Control.BlockSolverMediumMaxSize,
				Parallel = Control.BlockSolverParallel,
				NumCores = Control.numCores
			};
			Reporter.ReportMessage("Initialized Block Matrix Solver");
		}
		PartGrid = new PartialGrid(_mesh, BoundaryConditions, Control.UsePartialGrid, Control.ActiveThresholdPartGrid, Reporter);
	}

	public override void PreparePropertyTables()
	{
		base.PreparePropertyTables();
	}

	protected static int GetLoopSize(int loopSize, int loopCount)
	{
		return loopSize;
	}

	protected virtual void BuildMatrixStructureFull()
	{
		_mesh.ComputeCellAdjacency(out var rows);
		MatrixNewton = new SparseMatrix<double>(rows);
		MatrixNewton.Sort();
		MatrixNewton.Pack();
		MatrixNewton.ComputeDiagonalPointers();
		ComputeStencilFaces();
	}

	protected virtual void ComputeStencilFaces()
	{
		if (StencilFaces == null || StencilFaces.Length != MatrixNewton.MaxNonZero)
		{
			StencilFaces = new int[MatrixNewton.MaxNonZero];
		}
		if (StencilSigns == null || StencilSigns.Length != MatrixNewton.MaxNonZero)
		{
			StencilSigns = new int[MatrixNewton.MaxNonZero];
		}
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			for (int j = MatrixNewton.RowPointers[i]; j < MatrixNewton.RowPointers[i + 1]; j++)
			{
				int columnIndex = MatrixNewton.ColumnIndices[j];
				if (i == columnIndex)
				{
					StencilFaces[j] = -1;
					StencilSigns[j] = 0;
					continue;
				}
				if (!_meshConnectivity.TryGetFace(i, columnIndex, out var faceIdx, out var sgn))
				{
					Solver._logger.LogError("Error computing stencil faces: no face between cells {cIdx} and {col}", i, columnIndex);
				}
				StencilFaces[j] = faceIdx;
				StencilSigns[j] = sgn;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	protected override ISolver.Status SolveStepInternal(double absTime, double dt, double dtPrevious)
	{
		if (Control.SaveLinearSystem.Mode != LinearSystemDumpMode.None)
		{
			_dumpStepIndex++;
			_dumpStepEndMicros = ComputeDriver.SecondsToMicros(absTime) + ComputeDriver.SecondsToMicros(dt);
		}
		bool preSolveOuter = PreSolveOuter(absTime, dt, out var EOPTime);
		if (Control.UsePartialGrid && PartGrid.CellCount == 0)
		{
			return ISolver.Status.OK;
		}
		while (Convergence.ContinueOuter() & preSolveOuter)
		{
			preSolveOuter = PreSolveInner(EOPTime, dt);
			if (!preSolveOuter)
			{
				break;
			}
			preSolveOuter = PrepareMatrixSolver();
			while (Convergence.ContinueInner() & preSolveOuter)
			{
				preSolveOuter = SolveInner();
			}
			if (preSolveOuter)
			{
				PostSolveInner(dt);
			}
		}
		if (preSolveOuter)
		{
			PostSolveOuter(dt);
		}
		if (!preSolveOuter)
		{
			return ISolver.Status.ERROR;
		}
		return ISolver.Status.OK;
	}

	private void MaybeSaveLinearSystem()
	{
		LinearSystemDumpSettings saveLinearSystem = Control.SaveLinearSystem;
		if (saveLinearSystem.Mode != LinearSystemDumpMode.None)
		{
			long dumpStepEndMicros = _dumpStepEndMicros;
			bool iterationsOuterConvTerm = Convergence.OuterConv.Iterations == 1 && Convergence.InnerConv.Iterations == 1;
			if (saveLinearSystem.Mode switch
			{
				LinearSystemDumpMode.First => iterationsOuterConvTerm && !_dumpFirstSaved, 
				LinearSystemDumpMode.Last => (dumpStepEndMicros >= _endMicros) & iterationsOuterConvTerm, 
				LinearSystemDumpMode.OutputInterval => iterationsOuterConvTerm && saveLinearSystem.OutputIntervalMicros > 0 && dumpStepEndMicros % saveLinearSystem.OutputIntervalMicros == 0, 
				LinearSystemDumpMode.All => true, 
				_ => false, 
			})
			{
				_dumpFirstSaved = true;
				double microsToSeconds = ComputeDriver.MicrosToSeconds(dumpStepEndMicros);
				DateTime dateTime = saveLinearSystem.StartTime.AddSeconds(microsToSeconds);
				LinearSystemIO.Save(Path.Combine(saveLinearSystem.Directory, $"LinSys_{dateTime:yyyy-MM-dd_HHmmssfff}_o{Convergence.OuterConv.Iterations}_i{Convergence.InnerConv.Iterations}"), MatrixNewton, CellRHSNewton, CellElevCorrInner, microsToSeconds, dateTime, Convergence.OuterConv.Iterations, Convergence.InnerConv.Iterations, _dumpStepIndex, computeControl.Units, saveLinearSystem.Mode);
			}
		}
	}

	protected virtual bool PreSolveOuter(double absTime, double dt, out double EOPTime)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		EOPTime = absTime + dt;
		Convergence.ResetOuter();
		MeshWaterState waterState = _waterState;
		waterState.ErrorCellVolumeNegative = false;
		UpdatePartial();
		UpdateFaceElevationAndProperties(waterState);
		if (Convergence.OuterConv.MaxIterations > 0)
		{
			Array.Copy(waterState.CellElevation, GuessCellElevation, _mesh.CellCount);
		}
		OverwriteBoundaryFaceFlows(waterState, absTime, dt);
		CellWaterSources.RefreshCurrentRates();
		CellWaterSources.AccumulateAll(dt);
		ticks[17] += stopwatch.ElapsedTicks;
		return true;
	}

	protected virtual void UpdatePartial()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (Control.UsePartialGrid)
		{
			PartGrid.Update(_waterState, _propertyTables, BoundaryConditions, HydraulicStructures, Control.BufferCountPartGrid, Control.ElevationThresholdPartGrid, Control.UseBlockSolver);
		}
		if (Control.UsePartialGrid && PartGrid.Enabled)
		{
			if (Control.UseBlockSolver)
			{
				ResetBlockPartSolve();
			}
			else
			{
				ResetPartSolve();
			}
			ResetRemovedFaces();
		}
		else
		{
			PartSolve = null;
		}
		ticks[18] += stopwatch.ElapsedTicks;
	}

	private void ResetPartSolve()
	{
		if (PartSolve == null)
		{
			PartialSolve partialSolve = (PartSolve = new PartialSolve(PartGrid.CellToPartialCell, PartGrid.CellIndicesPartial));
		}
	}

	private void ResetBlockPartSolve()
	{
		if (PartSolve == null)
		{
			PartialSolve partialSolve = (PartSolve = new PartialSolve());
		}
		int regionCount = PartGrid.RegionCount;
		int cellCount = PartGrid.CellCount;
		if (regionCount <= 0 || cellCount <= 0)
		{
			PartSolve.BlockCount = 0;
		}
		else
		{
			PartSolve.ComputeFromPartialGrid(PartGrid.Permutation, cellCount, PartGrid.BlockOffsets.AsSpan(0, regionCount + 1), PartGrid.RegionIds.AsSpan(0, regionCount), _mesh.CellCount);
		}
	}

	protected virtual void ResetRemovedFaces()
	{
		ReadOnlySpan<int> faceIndicesRemoved = CollectionsMarshal.AsSpan(PartGrid.FaceIndicesRemoved);
		int length = faceIndicesRemoved.Length;
		MeshWaterState waterState = _waterState;
		Span<double> faceVelocity = waterState.FaceVelocity.AsSpan();
		Span<double> faceArea = waterState.FaceArea.AsSpan();
		Span<double> faceCoeff = FaceCoeff.AsSpan();
		for (int i = 0; i < length; i++)
		{
			int index = faceIndicesRemoved[i];
			faceVelocity[index] = 0.0;
			faceArea[index] = 0.0;
			faceCoeff[index] = 0.0;
		}
	}

	protected virtual bool PreSolveInner(double EOPTime, double dt)
	{
		if (Convergence.OuterConv.Iterations > 1)
		{
			ComputeCellElevAvg();
			UpdateFaceElevationAndProperties(_waterState);
			OverwriteBoundaryFaceFlows(_waterState, EOPTime, dt);
		}
		OverwriteWeirFaceDepths(_waterState);
		return true;
	}

	private void ComputeCellElevAvg()
	{
		int cellCount = PartGrid.CellCount;
		int loopSize = GetLoopSize(5000, cellCount);
		MeshWaterState ws = _waterState;
		MeshWaterState wsOld = _oldState;
		threadFor.Run(cellCount, (int start, int stop) =>
		{
			ComputeCellElevAvgStartStop(ws, wsOld, start, stop);
		}, loopSize);
	}

	private void ComputeCellElevAvgStartStop(MeshWaterState ws, MeshWaterState wsOld, int start, int stop)
	{
		ReadOnlySpan<double> aSp = wsOld.CellElevation.AsSpan();
		ReadOnlySpan<double> bSp = ws.CellElevation.AsSpan();
		Span<double> resultSp = CellElevAvg.AsSpan();
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> cellIndices = CollectionsMarshal.AsSpan(PartGrid.CellIndices);
			for (int i = start; i <= stop; i++)
			{
				int index = cellIndices[i];
				resultSp[index] = 0.5 * (aSp[index] + bSp[index]);
			}
		}
		else
		{
			AverageHalf(aSp, bSp, resultSp, start, stop);
		}
	}

	protected virtual void OverwriteWeirFaceDepths(MeshWaterState ms)
	{
	}

	private bool PrepareMatrixSolver()
	{
		bool result = true;
		if (PartGrid.Changed)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (!ActiveMatSolver.Prepare(MatrixNewton, Precond, PartSolve))
			{
				result = false;
			}
			ticks[19] += stopwatch.ElapsedTicks;
		}
		if (!Control.UsePartialGrid)
		{
			PartGrid.Changed = false;
		}
		return result;
	}

	protected virtual void PostSolveInner(double dt)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		ComputeOuterCorrectionConvergence();
		ComputeFaceFlowsAndVelocities(dt);
		ticks[27] += stopwatch.ElapsedTicks;
	}

	protected abstract void ComputeFaceFlowsAndVelocities(double dt);

	protected virtual void ComputeOuterCorrectionConvergence()
	{
		if (Control.OuterControl.IterMax <= 1)
		{
			Convergence.OuterConv.Status = LoopConvergence.ConvStatus.MAX_ITERATIONS;
			return;
		}
		int cellCount = PartGrid.CellCount;
		int loopSize = GetLoopSize(5000, cellCount);
		double[] cellElevation = _waterState.CellElevation;
		float[] cellMinElev = _propertyTables.CellMinimumElevations;
		threadFor.Run(cellCount, (int start, int stop) =>
		{
			ComputeOuterCorrectionConvergenceStartStop(cellElevation, cellMinElev, start, stop);
		}, loopSize);
		Convergence.ComputeOuter(Control.OuterControl, CellElevCorrOuter, PartGrid.CellIndices, MaskArray);
	}

	private void ComputeOuterCorrectionConvergenceStartStop(double[] cellElevation, float[] cellMinElev, int start, int stop)
	{
		ReadOnlySpan<double> cellElevSp = cellElevation.AsSpan();
		ReadOnlySpan<double> guessCellElevSp = GuessCellElevation.AsSpan();
		ReadOnlySpan<float> cellMinElevSp = cellMinElev.AsSpan();
		Span<double> cellElevCorrOuterSp = CellElevCorrOuter.AsSpan();
		Span<bool> maskArraySp = MaskArray.AsSpan();
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> cellIndices = CollectionsMarshal.AsSpan(PartGrid.CellIndices);
			for (int i = start; i <= stop; i++)
			{
				int index = cellIndices[i];
				cellElevCorrOuterSp[index] = cellElevSp[index] - guessCellElevSp[index];
				maskArraySp[index] = cellElevSp[index] > (double)cellMinElevSp[index];
			}
		}
		else
		{
			ComputeOuterCorrectionConvergence(cellElevSp, guessCellElevSp, cellMinElevSp, cellElevCorrOuterSp, maskArraySp, start, stop);
		}
	}

	protected virtual void UpdateFaceElevationAndProperties(MeshWaterState ws)
	{
		if (Control.faceElevationMethod != FaceElevationMethod.Frozen)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			int faceCount = PartGrid.FaceCount;
			int loopSize = GetLoopSize(4000, faceCount);
			threadFor.Run(faceCount, (int start, int stop) =>
			{
				UpdateFaceElevationAndPropertiesStartStop(ws, start, stop);
			}, loopSize);
			ticks[21] += stopwatch.ElapsedTicks;
		}
	}

	private void UpdateFaceElevationAndPropertiesStartStop(MeshWaterState ws, int start, int stop)
	{
		Span<double> cellElevations = CellElevInterm.AsSpan();
		Span<double> facePinned = ws.PinnedFaceStage.AsSpan();
		Span<double> faceVelocity = ws.FaceVelocity.AsSpan();
		FaceElevationMethod faceElevationMethod = Control.faceElevationMethod;
		double[] faceElevation = ws.FaceElevation;
		double[] faceArea = ws.FaceArea;
		float[] faceMannings = ws.FaceMannings;
		float[] faceHydraulicRadius = ws.FaceHydraulicRadius;
		float[] faceHydraulicDepth = ws.FaceHydraulicDepth;
		int[] facePriorIndexes = ws.FacePriorIndexes;
		float[] faceNonlinFrictCoeff = ws.FaceNonlinFrictCoeff;
		float[] faceShearFluxCoeff = ws.FaceShearFluxCoeff;
		double[] faceConveyance = ws.FaceConveyance;
		MeshPropertyTables propertyTables = _propertyTables;
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		for (int i = start; i <= stop; i++)
		{
			int fIdx = faceIndices[i];
			propertyTables.UpdateFaceONE(in fIdx, facePriorIndexes, faceElevation, faceArea, faceHydraulicRadius, faceHydraulicDepth, faceMannings, faceNonlinFrictCoeff, faceShearFluxCoeff, faceConveyance, HydraulicMeshComputer.FaceComputes.WSEL_Engine(fIdx, cellElevations, facePinned, faceVelocity, faceElevationMethod, propertyTables));
		}
	}

	public void OverrideStructFaceCellVelocity(MeshWaterState ws)
	{
		bool[] cellActive = PartGrid.CellActive;
		Geospatial.Vectors.Vector[] cellVelocity = ws.CellVelocity;
		foreach (Weir weir2 in HydraulicStructures.Weirs)
		{
			Weir weir = weir2 as Weir;
			for (int i = 0; i < weir2.Faces.Length; i++)
			{
				int faceIndex = weir2.Faces[i];
				int faceCellA = _meshConnectivity.FaceCellA[faceIndex];
				int faceCellB = _meshConnectivity.FaceCellB[faceIndex];
				double cellElevation = ws.CellElevation[faceCellA];
				double cellElevation2 = ws.CellElevation[faceCellB];
				double weirCrest = weir.WeirCrest[i];
				double weirApproachVel = weir.GetWeirApproachVel(ws, _mesh, _hmesh, faceCellA, faceIndex);
				double weirApproachVel2 = weir.GetWeirApproachVel(ws, _mesh, _hmesh, faceCellB, faceIndex);
				double cellElevationWeirApproachVelTerm = cellElevation + weirApproachVel * weirApproachVel / (2.0 * grav);
				double cellElevation2WeirApproachVel2Term = cellElevation2 + weirApproachVel2 * weirApproachVel2 / (2.0 * grav);
				if (cellActive[faceCellA] && cellElevation > weirCrest && cellElevationWeirApproachVelTerm > cellElevation2WeirApproachVel2Term)
				{
					cellVelocity[faceCellA] = _hmesh.CellComputes.VelocityWLS(faceCellA, ws.FaceArea, ws.FaceVelocity, faceIndex);
				}
				else if (cellActive[faceCellB] && cellElevation2 > weirCrest && cellElevation2WeirApproachVel2Term > cellElevationWeirApproachVelTerm)
				{
					cellVelocity[faceCellB] = _hmesh.CellComputes.VelocityWLS(faceCellB, ws.FaceArea, ws.FaceVelocity, faceIndex);
				}
			}
		}
	}

	private void CheckFaceWaterLevel(MeshWaterState ws, MeshPropertyTables props, int fIdx, double wsel)
	{
		int faceCellA = _meshConnectivity.FaceCellA[fIdx];
		int faceCellB = _meshConnectivity.FaceCellB[fIdx];
		if (faceCellB != -1)
		{
			double cellElevation = ws.CellElevation[faceCellA];
			double cellElevation2 = cellElevation;
			double faceMinimumElevation = props.FaceMinimumElevations[fIdx];
			if (faceCellB >= 0)
			{
				cellElevation = Math.Max(cellElevation, ws.CellElevation[faceCellB]);
				cellElevation2 = Math.Min(cellElevation2, ws.CellElevation[faceCellB]);
			}
			if (wsel > faceMinimumElevation + 1E-05 && !(wsel > cellElevation + 1E-05))
			{
				_ = cellElevation2 - 1E-05;
			}
			_ = faceMinimumElevation - 1E-05;
		}
	}

	protected virtual void ComputeAssembly(double EOPTime, double dt)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int cellCount = PartGrid.CellCount;
		int loopSize = GetLoopSize(3000, cellCount);
		threadFor.Run(cellCount, (int start, int stop) =>
		{
			ReadOnlySpan<int> cellIndices = CollectionsMarshal.AsSpan(PartGrid.CellIndices);
			for (int i = start; i <= stop; i++)
			{
				int cIdx = cellIndices[i];
				ComputeAssemblyRow(cIdx);
			}
		}, loopSize);
		OverrideAssemblyBC(_waterState, EOPTime, dt);
		ticks[16] += stopwatch.ElapsedTicks;
	}

	protected abstract void ComputeAssemblyRow(int cIdx);

	protected void OverrideAssemblyBC(MeshWaterState ws, double EOPTime, double dt)
	{
		foreach (BoundaryCondition stageBoundaryCondition in StageBoundaryConditions)
		{
			int[] faces = stageBoundaryCondition.Faces;
			foreach (int fIdx in faces)
			{
				OverrideAssemblyExternalStageBC(ws, dt, fIdx);
			}
		}
		foreach (BoundaryCondition variableStageBoundaryCondition in VariableStageBoundaryConditions)
		{
			int[] faces = variableStageBoundaryCondition.Faces;
			foreach (int fIdx2 in faces)
			{
				OverrideAssemblyExternalStageBC(ws, dt, fIdx2);
			}
		}
		foreach (BoundaryCondition spatiallyVariableStageVelocityBoundaryCondition in SpatiallyVariableStageVelocityBoundaryConditions)
		{
			int[] faces = spatiallyVariableStageVelocityBoundaryCondition.Faces;
			foreach (int fIdx3 in faces)
			{
				OverrideAssemblyExternalStageBC(ws, dt, fIdx3);
			}
		}
		foreach (BoundaryCondition flowBoundaryCondition in FlowBoundaryConditions)
		{
			int[] faces = flowBoundaryCondition.Faces;
			foreach (int fIdx4 in faces)
			{
				OverrideAssemblyExternalFlowBC(ws, dt, fIdx4);
			}
		}
		foreach (BoundaryCondition variableFlowBoundaryCondition in VariableFlowBoundaryConditions)
		{
			int[] faces = variableFlowBoundaryCondition.Faces;
			foreach (int fIdx5 in faces)
			{
				OverrideAssemblyExternalFlowBC(ws, dt, fIdx5);
			}
		}
		foreach (BoundaryCondition normalDepthBoundaryCondition in NormalDepthBoundaryConditions)
		{
			int[] faces = normalDepthBoundaryCondition.Faces;
			foreach (int fIdx6 in faces)
			{
				OverrideAssemblyExternalFlowBC(ws, dt, fIdx6);
			}
		}
		double[] sourceFlow = CellWaterSources.CurrentNetFlowArray;
		int[] sourceCells = CellWaterSources.ActiveNetSourceCells;
		int activeNetSourceCellCount = CellWaterSources.ActiveNetSourceCellCount;
		if (activeNetSourceCellCount > 0)
		{
			int loopSize = GetLoopSize(3000, activeNetSourceCellCount);
			threadFor.Run(activeNetSourceCellCount, (int start, int stop) =>
			{
				for (int j = start; j <= stop; j++)
				{
					int sourceCellIndex = sourceCells[j];
					OverrideAssemblyInternalFlowBC(dt, sourceCellIndex, sourceFlow[sourceCellIndex]);
				}
			}, loopSize);
		}
		if (!HydraulicStructures.HasWeirs)
		{
			return;
		}
		foreach (Weir weir in HydraulicStructures.Weirs)
		{
			OverrideAssemblyWeir(ws, dt, weir);
		}
	}

	protected abstract void OverrideAssemblyExternalStageBC(MeshWaterState ws, double dt, int fIdx);

	protected virtual void OverrideAssemblyExternalFlowBC(MeshWaterState ws, double dt, int fIdx)
	{
		int faceCellA = _meshConnectivity.FaceCellA[fIdx];
		CellRHS[faceCellA] -= dt * ws.FaceFlow[fIdx];
	}

	protected virtual void OverrideAssemblyInternalFlowBC(double dt, int cIdx, double flow)
	{
		CellRHS[cIdx] += dt * flow;
	}

	protected virtual void OverrideAssemblyWeir(MeshWaterState ws, double dt, Weir weir)
	{
	}

	protected virtual void PreComputeNewton(MeshWaterState ws)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int cellCount = PartGrid.CellCount;
		int loopSize = GetLoopSize(5000, cellCount);
		threadFor.Run(cellCount, (int start, int stop) =>
		{
			PreComputeNewtonStartStop(ws, start, stop);
		}, loopSize);
		ticks[23] += stopwatch.ElapsedTicks;
	}

	private void PreComputeNewtonStartStop(MeshWaterState ws, int start, int stop)
	{
		ReadOnlySpan<double> matrixDiagonal = MatrixDiagonal.AsSpan();
		ReadOnlySpan<double> cellArea = ws.CellArea.AsSpan();
		ReadOnlySpan<double> cellVolume = ws.CellVolume.AsSpan();
		ReadOnlySpan<double> input = ws.CellElevation.AsSpan();
		ReadOnlySpan<double> cellRHS = CellRHS.AsSpan();
		Span<double> cellRHSNewton = CellRHSNewton.AsSpan();
		ReadOnlySpan<int> cellIndices = CollectionsMarshal.AsSpan(PartGrid.CellIndices);
		for (int i = start; i <= stop; i++)
		{
			int cellIndex = cellIndices[i];
			double matrixDiagonalCellIndexTerm = matrixDiagonal[cellIndex] + cellArea[cellIndex];
			MatrixNewton.SetDataDiagonalRow(cellIndex, matrixDiagonalCellIndexTerm);
			double productRow = SparseMatrix<double>.ProductRow(MatrixNewton, input, cellIndex);
			cellRHSNewton[cellIndex] = cellRHS[cellIndex] - cellVolume[cellIndex] - productRow + cellArea[cellIndex] * input[cellIndex];
		}
	}

	protected virtual bool SolveInner()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Convergence.InnerConv.IncrementIterations();
		bool solveNewton = SolveNewton();
		if (solveNewton)
		{
			MaybeSaveLinearSystem();
			solveNewton = PostSolveNewton();
		}
		ticks[15] += stopwatch.ElapsedTicks;
		return solveNewton;
	}

	protected virtual bool SolveNewton()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Stopwatch stopwatch2 = Stopwatch.StartNew();
		IMatrixSolver<double> activeMatSolver = ActiveMatSolver;
		if (!activeMatSolver.IsDirectSolver)
		{
			Array.Fill(CellElevCorrInner, 0.0);
		}
		OperationResult operationResult = activeMatSolver.Solve(MatrixNewton, CellElevCorrInner, CellRHSNewton);
		ticks[25] += stopwatch2.ElapsedTicks;
		if (!operationResult.Result)
		{
			Solver._logger.LogTrace("Matrix solve failed");
			return false;
		}
		MeshWaterState ws = _waterState;
		int cellCount = PartGrid.CellCount;
		int loopSize = GetLoopSize(3000, cellCount);
		ws.InvalidateCellWaterSurfaceGradients();
		threadFor.Run(cellCount, (int start, int stop) =>
		{
			UpdateCellVariablesStartStop(ws, start, stop);
		}, loopSize);
		ticks[24] += stopwatch.ElapsedTicks;
		return true;
	}

	private void UpdateCellVariablesStartStop(MeshWaterState ws, int start, int stop)
	{
		ReadOnlySpan<double> cellElevCorrSp = CellElevCorrInner.AsSpan();
		Span<double> cellAreaSp = ws.CellArea.AsSpan();
		Span<double> cellElevSp = ws.CellElevation.AsSpan();
		Span<double> cellVolSp = ws.CellVolume.AsSpan();
		int[] cellPriorIndexes = ws.CellPriorIndexes;
		Span<float> cellMinElevSp = _propertyTables.CellMinimumElevations.AsSpan();
		ReadOnlySpan<int> cellIndices = CollectionsMarshal.AsSpan(PartGrid.CellIndices);
		for (int i = start; i <= stop; i++)
		{
			int cIdx = cellIndices[i];
			UpdateCellVariablesOne(cellElevCorrSp, cellMinElevSp, cellElevSp, cellAreaSp, cellVolSp, cellPriorIndexes, cIdx);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void UpdateCellVariablesOne(ReadOnlySpan<double> cellElevCorrSp, Span<float> cellMinElevSp, Span<double> cellElevSp, Span<double> cellAreaSp, Span<double> cellVolSp, int[] cellPriorIndexes, int cIdx)
	{
		cellElevSp[cIdx] += cellElevCorrSp[cIdx];
		double wsel = cellElevSp[cIdx];
		_propertyTables.GetCellPropertiesAtWaterSurface(cIdx, wsel, cellPriorIndexes, out var volume, out var wetArea);
		cellVolSp[cIdx] = volume;
		cellAreaSp[cIdx] = wetArea;
	}

	protected virtual bool PostSolveNewton()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		MeshWaterState ws = _waterState;
		int cellCount = PartGrid.CellCount;
		int loopSize = GetLoopSize(3000, cellCount);
		double[] cellPolyArea = _hmesh.CellPolygonAreas;
		float[] cellMinElev = _propertyTables.CellMinimumElevations;
		int unstableFlag = 0;
		threadFor.Run(cellCount, (int start, int stop) =>
		{
			if (ComputeNewtonDiagonalRHSInnerConvStartStop(ws, cellPolyArea, cellMinElev, start, stop))
			{
				Interlocked.Exchange(ref unstableFlag, 1);
			}
		}, loopSize);
		Convergence.ComputeInner(Control.InnerControl, CellElevResInner, PartGrid.CellIndices, MaskArray);
		ticks[26] += stopwatch.ElapsedTicks;
		return unstableFlag == 0;
	}

	private bool ComputeNewtonDiagonalRHSInnerConvStartStop(MeshWaterState ws, double[] cellPolyArea, float[] cellMinElev, int start, int stop)
	{
		ReadOnlySpan<double> matrixDiagonal = MatrixDiagonal.AsSpan();
		ReadOnlySpan<double> cellArea = ws.CellArea.AsSpan();
		Span<double> cellElevation = ws.CellElevation.AsSpan();
		ReadOnlySpan<double> cellVolume = ws.CellVolume.AsSpan();
		ReadOnlySpan<double> cellPolyAreaSpan = cellPolyArea.AsSpan();
		ReadOnlySpan<float> cellMinElevSpan = cellMinElev.AsSpan();
		Span<double> cellRHS = CellRHS.AsSpan();
		Span<double> cellRHSNewton = CellRHSNewton.AsSpan();
		Span<double> cellElevResInner = CellElevResInner.AsSpan();
		bool result = false;
		ReadOnlySpan<int> cellIndices = CollectionsMarshal.AsSpan(PartGrid.CellIndices);
		for (int i = start; i <= stop; i++)
		{
			int cellIndex = cellIndices[i];
			double matrixDiagonalCellIndexTerm = matrixDiagonal[cellIndex] + cellArea[cellIndex];
			MatrixNewton.SetDataDiagonalRow(cellIndex, matrixDiagonalCellIndexTerm);
			double productRow = SparseMatrix<double>.ProductRow(MatrixNewton, cellElevation, cellIndex);
			cellRHSNewton[cellIndex] = cellRHS[cellIndex] - cellVolume[cellIndex] - productRow + cellArea[cellIndex] * cellElevation[cellIndex];
			double cellAreaCellIndexTerm = Math.Max(cellArea[cellIndex], 0.1 * cellPolyAreaSpan[cellIndex]);
			cellElevResInner[cellIndex] = cellRHSNewton[cellIndex] / cellAreaCellIndexTerm;
			if (Math.Abs(cellElevResInner[cellIndex]) > 1000000.0)
			{
				result = true;
			}
			MaskArray[cellIndex] = cellElevation[cellIndex] > (double)cellMinElevSpan[cellIndex];
		}
		return result;
	}

	protected virtual void PostSolveOuter(double dt)
	{
		ClampCellElevationsToInverts();
		IncrementConservation(dt);
	}

	protected virtual void ClampCellElevationsToInverts()
	{
		MeshWaterState ws = _waterState;
		int cellCount = PartGrid.CellCount;
		int loopSize = GetLoopSize(1000, cellCount);
		float[] cellMinElev = _propertyTables.CellMinimumElevations;
		ws.InvalidateCellWaterSurfaceGradients();
		threadFor.Run(cellCount, (int start, int stop) =>
		{
			ClampCellElevationsToInvertsStartStop(ws, cellMinElev, start, stop);
		}, loopSize);
	}

	private void ClampCellElevationsToInvertsStartStop(MeshWaterState ws, float[] cellMinElev, int start, int stop)
	{
		Span<double> valuesSp = ws.CellElevation.AsSpan();
		Span<float> cellMinElevSpan = cellMinElev.AsSpan();
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> cellIndices = CollectionsMarshal.AsSpan(PartGrid.CellIndices);
			for (int i = start; i <= stop; i++)
			{
				int index = cellIndices[i];
				if (valuesSp[index] < (double)cellMinElevSpan[index])
				{
					valuesSp[index] = cellMinElevSpan[index];
				}
			}
		}
		else
		{
			ClampToMinimum(valuesSp, cellMinElevSpan, start, stop);
		}
	}

	public override double ComputeMaxTimestep(MeshWaterState ws)
	{
		if (!computeControl.variableDt)
		{
			stepLimitation = StepLimitation.LIM_USER;
			limitingFace = -1;
			return computeControl.Timestep;
		}
		double dtMax = 86400.0;
		stepLimitation = StepLimitation.LIM_ADVECTION;
		limitingFace = -1;
		double[] faceDist = _hmesh.FaceRepresentativeDistances;
		int faceCount = PartGrid.FaceCount;
		int loopSize = GetLoopSize(1000, faceCount);
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			var (dtMax2, limitingFace2) = ComputeMaxTimeStepStartStop(ws, PartGrid, 1E-06, 86400.0, faceDist, start, stop);
			using (_maxTimestepLock.EnterScope())
			{
				if (dtMax2 < dtMax)
				{
					dtMax = dtMax2;
					limitingFace = limitingFace2;
				}
			}
		}, loopSize);
		dtMax *= Control.MaxCourant;
		long secondsToMicros = ComputeDriver.SecondsToMicros(dtMax);
		long dtMicros = computeControl.DtMicros;
		long dtMicrosSecondsToMicrosTerm = (long)Math.Ceiling((double)dtMicros / (double)secondsToMicros);
		dtMicros /= dtMicrosSecondsToMicrosTerm;
		dtMax = ComputeDriver.MicrosToSeconds(dtMicros);
		return dtMax;
	}

	private static (double localDtMax, int localLimitingFace) ComputeMaxTimeStepStartStop(MeshWaterState ws, PartialGrid partGrid, double MinVelocity, double MaxTimeStep, double[] faceDist, int start, int stop)
	{
		Span<double> faceVelocity = ws.FaceVelocity.AsSpan();
		double maxTimeStep = MaxTimeStep;
		int faceIndexTerm = -1;
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(partGrid.FaceIndices);
		for (int i = start; i <= stop; i++)
		{
			int faceIndex = faceIndices[i];
			double absoluteFaceVelocity = Math.Abs(faceVelocity[faceIndex]);
			if (!(absoluteFaceVelocity <= MinVelocity))
			{
				double faceDistOverAbsoluteFaceVelocity = faceDist[faceIndex] / absoluteFaceVelocity;
				if (faceDistOverAbsoluteFaceVelocity < maxTimeStep)
				{
					maxTimeStep = faceDistOverAbsoluteFaceVelocity;
					faceIndexTerm = faceIndex;
				}
			}
		}
		return (localDtMax: maxTimeStep, localLimitingFace: faceIndexTerm);
	}

	public override void Cleanup()
	{
		base.Cleanup();
		if (MatSolver is MatrixSolver<double> matrixSolver)
		{
			ticks[19] -= matrixSolver.ticks[0];
			matrixSolver.WriteTicker();
		}
		if (MatrixNewton != null)
		{
			MatSolver?.Destroy(MatrixNewton);
			BlockMatSolver?.Destroy(MatrixNewton);
		}
		PartGrid?.WriteTicker();
	}

	protected abstract double ComputeFaceCoeff(MeshWaterState ws, int fIdx, double dt);

	protected static double ContraharmonicMean(double a, double b)
	{
		double computed = a + b;
		if (computed < -19.0)
		{
			return 0.0;
		}
		return (a * a + b * b) / computed;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal static void AverageHalf(ReadOnlySpan<double> aSp, ReadOnlySpan<double> bSp, Span<double> resultSp, int start, int stop)
	{
		int count2 = stop + 1;
		int count = Vector<double>.Count;
		Vector<double> vector = new Vector<double>(0.5);
		int i;
		for (i = start; i + count <= count2; i += count)
		{
			(vector * (new Vector<double>(aSp.Slice(i, count)) + new Vector<double>(bSp.Slice(i, count)))).CopyTo(resultSp.Slice(i, count));
		}
		for (; i < count2; i++)
		{
			resultSp[i] = 0.5 * (aSp[i] + bSp[i]);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal static void ComputeOuterCorrectionConvergence(ReadOnlySpan<double> cellElevSp, ReadOnlySpan<double> guessCellElevSp, ReadOnlySpan<float> cellMinElevSp, Span<double> cellElevCorrOuterSp, Span<bool> maskArraySp, int start, int stop)
	{
		int count3 = stop + 1;
		int count = Vector<float>.Count;
		int count2 = Vector<byte>.Count;
		Span<byte> asBytes = MemoryMarshal.AsBytes(maskArraySp);
		int i;
		for (i = start; i + count2 <= count3; i += count2)
		{
			Vector<long> low = SubAndCompare(cellElevSp, guessCellElevSp, cellMinElevSp, cellElevCorrOuterSp, i, out var hiMask);
			Vector<long> low2 = SubAndCompare(cellElevSp, guessCellElevSp, cellMinElevSp, cellElevCorrOuterSp, i + count, out var hiMask2);
			Vector<long> low3 = SubAndCompare(cellElevSp, guessCellElevSp, cellMinElevSp, cellElevCorrOuterSp, i + 2 * count, out var hiMask3);
			Vector<long> low4 = SubAndCompare(cellElevSp, guessCellElevSp, cellMinElevSp, cellElevCorrOuterSp, i + 3 * count, out var hiMask4);
			Vector<short> low5 = System.Numerics.Vector.Narrow(System.Numerics.Vector.Narrow(low, hiMask), System.Numerics.Vector.Narrow(low2, hiMask2));
			Vector<short> high = System.Numerics.Vector.Narrow(System.Numerics.Vector.Narrow(low3, hiMask3), System.Numerics.Vector.Narrow(low4, hiMask4));
			System.Numerics.Vector.AsVectorByte(System.Numerics.Vector.Narrow(low5, high) & Vector<sbyte>.One).CopyTo(asBytes.Slice(i, count2));
		}
		for (; i < count3; i++)
		{
			cellElevCorrOuterSp[i] = cellElevSp[i] - guessCellElevSp[i];
			maskArraySp[i] = cellElevSp[i] > (double)cellMinElevSp[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector<long> SubAndCompare(ReadOnlySpan<double> cellElevSp, ReadOnlySpan<double> guessCellElevSp, ReadOnlySpan<float> cellMinElevSp, Span<double> cellElevCorrOuterSp, int i, out Vector<long> hiMask)
	{
		int count = Vector<double>.Count;
		int count2 = Vector<float>.Count;
		Vector<double> vector = new Vector<double>(cellElevSp.Slice(i, count));
		Vector<double> vector2 = new Vector<double>(cellElevSp.Slice(i + count, count));
		(vector - new Vector<double>(guessCellElevSp.Slice(i, count))).CopyTo(cellElevCorrOuterSp.Slice(i, count));
		(vector2 - new Vector<double>(guessCellElevSp.Slice(i + count, count))).CopyTo(cellElevCorrOuterSp.Slice(i + count, count));
		System.Numerics.Vector.Widen(new Vector<float>(cellMinElevSp.Slice(i, count2)), out var low, out var high);
		hiMask = System.Numerics.Vector.GreaterThan(vector2, high);
		return System.Numerics.Vector.GreaterThan(vector, low);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal static void ClampToMinimum(Span<double> valuesSp, ReadOnlySpan<float> minValuesSp, int start, int stop)
	{
		int count3 = stop + 1;
		int count = Vector<double>.Count;
		int count2 = Vector<float>.Count;
		int i;
		for (i = start; i + count2 <= count3; i += count2)
		{
			System.Numerics.Vector.Widen(new Vector<float>(minValuesSp.Slice(i, count2)), out var low, out var high);
			Vector<double> vector = new Vector<double>((ReadOnlySpan<double>)valuesSp.Slice(i, count));
			Vector<double> vector2 = new Vector<double>((ReadOnlySpan<double>)valuesSp.Slice(i + count, count));
			System.Numerics.Vector.ConditionalSelect(System.Numerics.Vector.LessThan(vector, low), low, vector).CopyTo(valuesSp.Slice(i, count));
			System.Numerics.Vector.ConditionalSelect(System.Numerics.Vector.LessThan(vector2, high), high, vector2).CopyTo(valuesSp.Slice(i + count, count));
		}
		for (; i < count3; i++)
		{
			if (valuesSp[i] < (double)minValuesSp[i])
			{
				valuesSp[i] = minValuesSp[i];
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static Vector<long> WetAndFlaggedMask(ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<FaceFlag> faceFlagsSp, Span<long> flagLanes, int i)
	{
		int count = Vector<double>.Count;
		Vector<long> vector = System.Numerics.Vector.GreaterThan(new Vector<double>(faceAreaSp.Slice(i, count)), Vector<double>.Zero);
		for (int j = 0; j < count; j++)
		{
			flagLanes[j] = ((faceFlagsSp[i + j] != 0) ? (-1) : 0);
		}
		return vector & new Vector<long>((ReadOnlySpan<long>)flagLanes);
	}

	[Conditional("DEBUG")]
	internal static void AssertRange<T>(ReadOnlySpan<T> buffer, int start, int stop)
	{
		if (start < 0 || stop >= buffer.Length)
		{
			throw new ArgumentOutOfRangeException("stop", $"Inclusive range [{start}, {stop}] outside span of length {buffer.Length}");
		}
	}

	[Conditional("DEBUG")]
	internal static void AssertRange<T>(Span<T> buffer, int start, int stop)
	{
		if (start < 0 || stop >= buffer.Length)
		{
			throw new ArgumentOutOfRangeException("stop", $"Inclusive range [{start}, {stop}] outside span of length {buffer.Length}");
		}
	}
}
