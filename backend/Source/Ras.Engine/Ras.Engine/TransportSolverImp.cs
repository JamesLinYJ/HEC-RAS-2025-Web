using System;
using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine.Engine;
using Utility.Logging;
using Utility.Math.x86;
using Utility.Matrices;

namespace Ras.Engine;

public class TransportSolverImp : TransportSolver
{
	private SparseMatrix<double>[] _A;

	private int[] _stencilFace;

	private int[] _stencilSign;

	private int[] _transferMatrixIndex;

	private int[] _preparedTransferDonors;

	private int[] _preparedTransferReceivers;

	private double[][] _rhs;

	private double[][] _sol;

	private double[][] _faceMass;

	private double[][] _faceCrossA;

	private int[] _boundaryFaces;

	private int[] _boundaryFailureCell;

	private TransportBoundaryCondition[] _bcLookup;

	private int[] _bcFaceSlot;

	private double[] _gPerp;

	private Vector[][] _gradNew;

	private Vector[][] _gradOld;

	private double[][] _cellGradientLimiterNew;

	private double[][] _cellGradientLimiterOld;

	private double[][] _nodeFaceNew;

	private double[][] _nodeFaceOld;

	private HydraulicMeshComputer.NodeFaceInterpolationWorkspace _nodeInterpolationNew;

	private HydraulicMeshComputer.NodeFaceInterpolationWorkspace _nodeInterpolationOld;

	private HydraulicMeshComputer.ScalarGradientWlsWorkspace _gradientNewWorkspace;

	private HydraulicMeshComputer.ScalarGradientWlsWorkspace _gradientOldWorkspace;

	private HydraulicMeshComputer.TVDVirtualPointInterpolationWorkspace _TVDVirtualPointInterpolationNew;

	private HydraulicMeshComputer.TVDVirtualPointInterpolationWorkspace _TVDVirtualPointInterpolationOld;

	private CasulliFaceFlowInterpolationWorkspace _TVDCasulliInterpolationNew;

	private CasulliFaceFlowInterpolationWorkspace _TVDCasulliInterpolationOld;

	private double[][] _TVDCasulliUpstreamNew;

	private double[][] _TVDCasulliUpstreamOld;

	private HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace[] _boundaryNew;

	private HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace[] _boundaryOld;

	private double[][] _outerError;

	private double[] _outerMaxError;

	private List<int> _allCells;

	private bool[] _activeMask;

	private double[] _effectiveNewVolume;

	private IMatrixSolver<double>[] _matSolver;

	private PartialSolve _partialSolve;

	private OperationResult[] _solveResults;

	private int[] _solverBudget;

	private double[] _bPacked;

	private double[] _xPacked;

	public TransportSolverImp(Mesh mesh, TransportControl control, TransportFlowField flow, int constituentCount, TransportBoundaryConditionCollection bcs = null, SolverConvergence convergence = null)
		: base(mesh, control, flow, constituentCount, bcs, convergence)
	{
	}

	public SparseMatrix<double> Matrix(int constituent)
	{
		return _A[constituent];
	}

	protected override void PrepareInternal()
	{
		int cellCount = _mesh.CellCount;
		int faceCount = _mesh.FaceCount;
		int constituentCount = _constituentCount;
		_TVDCasulliInterpolationNew = null;
		_TVDCasulliInterpolationOld = null;
		_TVDCasulliUpstreamNew = null;
		_TVDCasulliUpstreamOld = null;
		SparseMatrix<double> sparseMatrix = BuildMatrixStructure();
		sparseMatrix.Sort();
		sparseMatrix.ComputeDiagonalPointers();
		_A = new SparseMatrix<double>[constituentCount];
		for (int i = 0; i < constituentCount; i++)
		{
			_A[i] = new SparseMatrix<double>(sparseMatrix);
		}
		BuildStencil(sparseMatrix);
		BuildTransferMatrixIndex(sparseMatrix);
		CaptureTransferTopology();
		List<int> intList = new List<int>();
		for (int j = 0; j < faceCount; j++)
		{
			if (_connectivity.FaceCellB[j] < 0)
			{
				intList.Add(j);
			}
		}
		_boundaryFaces = intList.ToArray();
		_boundaryFailureCell = new int[constituentCount];
		_bcLookup = _bcs.BuildFaceLookup(faceCount, out _bcFaceSlot);
		_gPerp = _hmesh.FaceInvPerpDistances;
		_rhs = new double[constituentCount][];
		_sol = new double[constituentCount][];
		_faceMass = new double[constituentCount][];
		_faceCrossA = new double[constituentCount][];
		_gradNew = new Vector[constituentCount][];
		_gradOld = new Vector[constituentCount][];
		_cellGradientLimiterNew = new double[constituentCount][];
		_cellGradientLimiterOld = new double[constituentCount][];
		_outerError = new double[constituentCount][];
		_nodeFaceNew = new double[constituentCount][];
		_nodeFaceOld = new double[constituentCount][];
		_boundaryNew = new HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace[constituentCount];
		_boundaryOld = new HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace[constituentCount];
		for (int k = 0; k < constituentCount; k++)
		{
			_rhs[k] = new double[cellCount];
			_sol[k] = new double[cellCount];
			_faceMass[k] = new double[faceCount];
			_faceCrossA[k] = new double[faceCount];
			_gradNew[k] = new Vector[cellCount];
			_gradOld[k] = new Vector[cellCount];
			if (UsesCellGradientCorrection)
			{
				_cellGradientLimiterNew[k] = new double[cellCount];
				_cellGradientLimiterOld[k] = new double[cellCount];
			}
			if (_control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased)
			{
				int nodeFaceValueCount = _hmesh.NodeComputes.NodeFaceValueCount;
				_nodeFaceNew[k] = new double[nodeFaceValueCount];
				_nodeFaceOld[k] = new double[nodeFaceValueCount];
			}
			_boundaryNew[k] = _hmesh.CreateScalarBoundaryReconstructionWorkspace();
			_boundaryOld[k] = _hmesh.CreateScalarBoundaryReconstructionWorkspace();
			_outerError[k] = new double[cellCount];
		}
		_outerMaxError = new double[cellCount];
		_allCells = new List<int>(cellCount);
		_activeMask = new bool[cellCount];
		_effectiveNewVolume = new double[cellCount];
		if (_control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased)
		{
			_nodeInterpolationNew = _hmesh.NodeComputes.CreateNodeFaceInterpolationWorkspace();
			_nodeInterpolationOld = _hmesh.NodeComputes.CreateNodeFaceInterpolationWorkspace();
		}
		for (int l = 0; l < cellCount; l++)
		{
			_allCells.Add(l);
			_activeMask[l] = _flow.IsCellActive(l);
		}
		_matSolver = new IMatrixSolver<double>[constituentCount];
		_solveResults = new OperationResult[constituentCount];
		_partialSolve = new PartialSolve();
		_solverBudget = ApportionSolverThreads(constituentCount, ThreadCount);
		for (int m = 0; m < constituentCount; m++)
		{
			_matSolver[m] = BuildMatrixSolver(cellCount);
			_matSolver[m].SetThreadBudget(_solverBudget[m]);
		}
	}

	private SparseMatrix<double> BuildMatrixStructure()
	{
		_mesh.ComputeCellAdjacencyMatrix(out var rowPointers, out var columnIndices);
		List<int>[] intLists = new List<int>[_mesh.CellCount];
		ReadOnlySpan<CellWaterTransfer> transfers = _flow.CellWaterSources.Transfers;
		for (int i = 0; i < transfers.Length; i++)
		{
			CellWaterTransfer cellWaterTransfer = transfers[i];
			int receiverCell = cellWaterTransfer.ReceiverCell;
			int donorCell = cellWaterTransfer.DonorCell;
			if (receiverCell != donorCell && !ContainsColumn(rowPointers, columnIndices, receiverCell, donorCell))
			{
				List<int>[] intListsList = intLists;
				int receiverCell2 = receiverCell;
				List<int> intList = intListsList[receiverCell2] ?? (intListsList[receiverCell2] = new List<int>());
				if (!intList.Contains(donorCell))
				{
					intList.Add(donorCell);
				}
			}
		}
		int[] intValues = new int[_mesh.CellCount + 1];
		for (int j = 0; j < _mesh.CellCount; j++)
		{
			intValues[j + 1] = intValues[j] + rowPointers[j + 1] - rowPointers[j] + (intLists[j]?.Count ?? 0);
		}
		int[] intListArray = new int[intValues[^1]];
		for (int k = 0; k < _mesh.CellCount; k++)
		{
			int rowPointerStepSum = intValues[k];
			int rowPointerStep = rowPointers[k + 1] - rowPointers[k];
			Array.Copy(columnIndices, rowPointers[k], intListArray, rowPointerStepSum, rowPointerStep);
			rowPointerStepSum += rowPointerStep;
			if (intLists[k] != null)
			{
				intLists[k].CopyTo(intListArray, rowPointerStepSum);
			}
		}
		return new SparseMatrix<double>(intValues, intListArray);
	}

	private static bool ContainsColumn(int[] rows, int[] columns, int row, int column)
	{
		for (int i = rows[row]; i < rows[row + 1]; i++)
		{
			if (columns[i] == column)
			{
				return true;
			}
		}
		return false;
	}

	private void BuildTransferMatrixIndex(SparseMatrix<double> structure)
	{
		ReadOnlySpan<CellWaterTransfer> transfers = _flow.CellWaterSources.Transfers;
		_transferMatrixIndex = new int[transfers.Length];
		Array.Fill(_transferMatrixIndex, -1);
		int[] rowPointers = structure.RowPointers;
		int[] columnIndices = structure.ColumnIndices;
		for (int i = 0; i < transfers.Length; i++)
		{
			CellWaterTransfer cellWaterTransfer = transfers[i];
			if (cellWaterTransfer.DonorCell == cellWaterTransfer.ReceiverCell)
			{
				continue;
			}
			for (int j = rowPointers[cellWaterTransfer.ReceiverCell]; j < rowPointers[cellWaterTransfer.ReceiverCell + 1]; j++)
			{
				if (columnIndices[j] == cellWaterTransfer.DonorCell)
				{
					_transferMatrixIndex[i] = j;
					break;
				}
			}
		}
	}

	private void CaptureTransferTopology()
	{
		ReadOnlySpan<CellWaterTransfer> transfers = _flow.CellWaterSources.Transfers;
		_preparedTransferDonors = new int[transfers.Length];
		_preparedTransferReceivers = new int[transfers.Length];
		for (int i = 0; i < transfers.Length; i++)
		{
			_preparedTransferDonors[i] = transfers[i].DonorCell;
			_preparedTransferReceivers[i] = transfers[i].ReceiverCell;
		}
	}

	private void ValidateTransferTopology()
	{
		ReadOnlySpan<CellWaterTransfer> transfers = _flow.CellWaterSources.Transfers;
		if (transfers.Length != _preparedTransferDonors.Length)
		{
			throw new InvalidOperationException("Cell-water transfer topology changed after Prepare; call Prepare again.");
		}
		for (int i = 0; i < transfers.Length; i++)
		{
			if (transfers[i].DonorCell != _preparedTransferDonors[i] || transfers[i].ReceiverCell != _preparedTransferReceivers[i])
			{
				throw new InvalidOperationException("Cell-water transfer topology changed after Prepare; call Prepare again.");
			}
		}
	}

	private static int[] ApportionSolverThreads(int nc, int cores)
	{
		int[] intValues = new int[nc];
		int[] intValuesArray = new int[nc];
		Array.Fill(intValues, 1);
		BlockMatrixSolver.ApportionThreads(intValues, Math.Max(cores, 1), intValuesArray);
		return intValuesArray;
	}

	private void BuildStencil(SparseMatrix<double> structure)
	{
		int nonZeroCount = structure.NonZeroCount;
		_stencilFace = new int[nonZeroCount];
		_stencilSign = new int[nonZeroCount];
		int[] rowPointers = structure.RowPointers;
		int[] columnIndices = structure.ColumnIndices;
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			for (int j = rowPointers[i]; j < rowPointers[i + 1]; j++)
			{
				int columnIndex = columnIndices[j];
				int faceIdx;
				int sgn;
				if (columnIndex == i)
				{
					_stencilFace[j] = -1;
					_stencilSign[j] = 0;
				}
				else if (_connectivity.TryGetFace(i, columnIndex, out faceIdx, out sgn))
				{
					_stencilFace[j] = faceIdx;
					_stencilSign[j] = sgn;
				}
				else
				{
					_stencilFace[j] = -2;
					_stencilSign[j] = 0;
				}
			}
		}
	}

	internal virtual IMatrixSolver<double> BuildMatrixSolver(int n)
	{
		SolverPolicy policy = new SolverPolicy(_control.MatrixSolverPrimary, new SolverKind[1] { SolverKind.Pgmres });
		LinearSystemInfo systemInfo = new LinearSystemInfo(IsSparse: true, IsSymmetric: false, IsPositiveDefinite: false, n);
		SolverOptionsBundle options = new SolverOptionsBundle
		{
			PardisoOpts = new PardisoOptions(11, IsExtractUpperTriangular: false),
			FgmresOpts = new FgmresOptions(500, 1E-12),
			PgmresOpts = new PgmresOptions()
		};
		return MatrixSolverFactory.Create(new SolverRequest(policy, systemInfo, options));
	}

	protected override void ValidateDerivedConfiguration()
	{
		if (!double.IsFinite(_control.ImplicitRelaxationFactor) || _control.ImplicitRelaxationFactor <= 0.0 || _control.ImplicitRelaxationFactor > 1.0)
		{
			throw new ArgumentOutOfRangeException("ImplicitRelaxationFactor");
		}
	}

	protected override ITransport.Status SolveStepInternal(double absTime, double dt, out int failureCell)
	{
		ValidateTransferTopology();
		failureCell = -1;
		bool useTVD = _control.advectionScheme == AdvectionScheme.TVD;
		double theta = _control.ThetaImplicit;
		double oneMinusTheta = 1.0 - theta;
		double invDt = 1.0 / dt;
		int nc = _constituentCount;
		int cellCount = _mesh.CellCount;
		int faceCount = _mesh.FaceCount;
		int nodeCount = _mesh.NodeCount;
		RefreshBoundaryLookup(_bcLookup, _bcFaceSlot);
		int ordinal = 0;
		for (int i = 0; i < cellCount; i++)
		{
			_activeMask[i] = _flow.IsCellActive(i);
			if (_activeMask[i])
			{
				ordinal++;
			}
		}
		if (ordinal == 0)
		{
			return ITransport.Status.OK;
		}
		PartialSolve partialSolve = null;
		if (_control.UsePartialGrid)
		{
			_partialSolve.Compute(_activeMask);
			partialSolve = _partialSolve;
		}
		for (int j = 0; j < cellCount; j++)
		{
			if (!_activeMask[j])
			{
				_effectiveNewVolume[j] = 0.0;
				continue;
			}
			double volume = EffectiveNewVolume(j, dt);
			_effectiveNewVolume[j] = volume;
			if (!double.IsFinite(volume))
			{
				failureCell = j;
				return ITransport.Status.NAN_STATE;
			}
			if (volume < 0.0)
			{
				if (!TryNormalizeDerivedVolume(j, ref volume))
				{
					failureCell = j;
					return ITransport.Status.ERROR;
				}
				_effectiveNewVolume[j] = volume;
			}
		}
		if (_control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased)
		{
			_nodeInterpolationNew.Prepare(_flow.FaceAreaNew);
			if (theta < 1.0)
			{
				_nodeInterpolationOld.Prepare(_flow.FaceAreaOld);
			}
		}
		if (useTVD && _control.TVDGradientRatioMethod == TVDGradientRatioMethod.UpstreamVirtualPoint)
		{
			if (_TVDVirtualPointInterpolationNew == null)
			{
				_TVDVirtualPointInterpolationNew = _hmesh.CreateTVDVirtualPointInterpolationWorkspace();
			}
			_TVDVirtualPointInterpolationNew.Prepare(_effectiveNewVolume, _flow.FaceAreaNew, _flow.CellActive, _flow.FaceActive);
			if (theta < 1.0)
			{
				if (_TVDVirtualPointInterpolationNew.WetStatusMatches(_flow.CellVolumeOld, _flow.FaceAreaOld, _flow.CellActive, _flow.FaceActive))
				{
					_TVDVirtualPointInterpolationOld = _TVDVirtualPointInterpolationNew;
				}
				else
				{
					if (_TVDVirtualPointInterpolationOld == null || _TVDVirtualPointInterpolationOld == _TVDVirtualPointInterpolationNew)
					{
						_TVDVirtualPointInterpolationOld = _hmesh.CreateTVDVirtualPointInterpolationWorkspace();
					}
					_TVDVirtualPointInterpolationOld.Prepare(_flow.CellVolumeOld, _flow.FaceAreaOld, _flow.CellActive, _flow.FaceActive);
				}
			}
		}
		bool useTVD2 = useTVD && _control.TVDGradientRatioMethod == TVDGradientRatioMethod.CasulliFaceFlowWeighted;
		if (useTVD2)
		{
			EnsureTVDCasulliBuffers(theta < 1.0);
			_TVDCasulliInterpolationNew.Prepare(_effectiveNewVolume, _flow.FaceFlowNew, _flow.FaceAreaNew, _flow.CellActive, _flow.FaceActive);
			if (theta < 1.0)
			{
				_TVDCasulliInterpolationOld.Prepare(_flow.CellVolumeOld, _flow.FaceFlowOld, _flow.FaceAreaOld, _flow.CellActive, _flow.FaceActive);
			}
		}
		Pardiso pardiso = null;
		if (_control.ReuseFactorizationAcrossConstituents && nc >= 2 && !_control.UsePartialGrid && MatricesShared() && _matSolver[0] is Pardiso pardiso2)
		{
			pardiso = pardiso2;
			pardiso.SetThreadBudget(ThreadCount);
		}
		else
		{
			for (int k = 0; k < nc; k++)
			{
				_matSolver[k].SetThreadBudget(_solverBudget[k]);
			}
		}
		bool condition = false;
		bool condition2 = false;
		for (int l = 0; l < nc; l++)
		{
			condition |= NeedsDeferredCorrection(l, useTVD);
			condition2 |= NeedGradient(l, useTVD);
		}
		if (condition2)
		{
			if (_gradientNewWorkspace == null)
			{
				_gradientNewWorkspace = _hmesh.CreateScalarGradientWlsWorkspace();
			}
			if (theta < 1.0 && _gradientOldWorkspace == null)
			{
				_gradientOldWorkspace = _hmesh.CreateScalarGradientWlsWorkspace();
			}
		}
		for (int m = 0; m < nc; m++)
		{
			ConfigureBoundaryReconstruction(_boundaryNew[m], m, _boundaryFaces, _bcLookup, _bcFaceSlot, _flow.FaceFlowNew, reverseFlowUsesEquilibrium: true, oldLevel: false);
			ConfigureBoundaryReconstruction(_boundaryOld[m], m, _boundaryFaces, _bcLookup, _bcFaceSlot, _flow.FaceFlowOld, reverseFlowUsesEquilibrium: true, oldLevel: true);
		}
		OperationResult[] solveResults = _solveResults;
		_convergence.ResetOuter();
		while (_convergence.ContinueOuter())
		{
			_convergence.OuterConv.IncrementIterations();
			long timestamp = Stopwatch.GetTimestamp();
			if (condition2)
			{
				if (_convergence.OuterConv.Iterations == 1)
				{
					_threadFor.Run(cellCount, PrepareGradientRange);
				}
				_threadFor.Run(cellCount, GradientsRange);
				for (int n = 0; n < nc; n++)
				{
					if (!NeedGradient(n, useTVD))
					{
						continue;
					}
					if (NeedsDiffusionGradient(n))
					{
						_boundaryNew[n].ApplyCellGradientAndLimiterOverrides(_gradientNewWorkspace, _state.CellConcentration[n], _effectiveNewVolume, _gradNew[n], _cellGradientLimiterNew[n], _control.multidimensionalSlopeLimiter);
					}
					else
					{
						_boundaryNew[n].ApplyCellGradientOverrides(_gradientNewWorkspace, _state.CellConcentration[n], _effectiveNewVolume, _gradNew[n], HydraulicMeshComputer.MultidimensionalSlopeLimiter.None);
					}
					if (theta < 1.0 && _convergence.OuterConv.Iterations == 1)
					{
						if (NeedsDiffusionGradient(n))
						{
							_boundaryOld[n].ApplyCellGradientAndLimiterOverrides(_gradientOldWorkspace, _oldState.CellConcentration[n], _flow.CellVolumeOld, _gradOld[n], _cellGradientLimiterOld[n], _control.multidimensionalSlopeLimiter);
						}
						else
						{
							_boundaryOld[n].ApplyCellGradientOverrides(_gradientOldWorkspace, _oldState.CellConcentration[n], _flow.CellVolumeOld, _gradOld[n], HydraulicMeshComputer.MultidimensionalSlopeLimiter.None);
						}
					}
				}
			}
			if (useTVD2)
			{
				_threadFor.Run(cellCount, CasulliUpstreamRange);
			}
			if (_control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased)
			{
				_threadFor.Run(nodeCount, NodeValuesRange);
				for (int intervalIndex = 0; intervalIndex < nc; intervalIndex++)
				{
					if (NeedsDiffusionNodeValues(intervalIndex))
					{
						_boundaryNew[intervalIndex].ApplyNodeFaceValueOverrides(_state.CellConcentration[intervalIndex], _effectiveNewVolume, _nodeInterpolationNew, _nodeFaceNew[intervalIndex]);
						if (theta < 1.0 && _convergence.OuterConv.Iterations == 1)
						{
							_boundaryOld[intervalIndex].ApplyNodeFaceValueOverrides(_oldState.CellConcentration[intervalIndex], _flow.CellVolumeOld, _nodeInterpolationOld, _nodeFaceOld[intervalIndex]);
						}
					}
				}
			}
			AddPhase(TransportPhase.Gradients, timestamp);
			timestamp = Stopwatch.GetTimestamp();
			_threadFor.Run(faceCount, AssembleFaceRange);
			_threadFor.Run(cellCount, AssembleCellRange);
			AddPhase(TransportPhase.Assembly, timestamp);
			timestamp = Stopwatch.GetTimestamp();
			Array.Fill(_boundaryFailureCell, -1);
			_threadFor.Run(nc, ApplyBoundaryFacesRange);
			for (int intervalIndex2 = 0; intervalIndex2 < nc; intervalIndex2++)
			{
				if (_boundaryFailureCell[intervalIndex2] >= 0)
				{
					failureCell = _boundaryFailureCell[intervalIndex2];
					return ITransport.Status.ERROR;
				}
			}
			double inverseRelaxation = 1.0 / _control.ImplicitRelaxationFactor;
			double rhsCorrectionFactor;
			if (inverseRelaxation > 1.0)
			{
				rhsCorrectionFactor = inverseRelaxation - 1.0;
				_threadFor.Run(cellCount, ApplyImplicitRelaxationRange);
			}
			AddPhase(TransportPhase.Boundary, timestamp);
			timestamp = Stopwatch.GetTimestamp();
			Array.Clear(solveResults);
			if (pardiso != null)
			{
				solveResults[0] = SolveSharedMultiRHS(pardiso, nc, cellCount);
			}
			else
			{
				_threadFor.Run(nc, PrepareAndSolveConstituentRange);
			}
			int nc2 = ((pardiso != null) ? 1 : nc);
			for (int intervalIndex3 = 0; intervalIndex3 < nc2; intervalIndex3++)
			{
				OperationResult operationResult = solveResults[intervalIndex3];
				if (operationResult != null && operationResult.Result)
				{
					continue;
				}
				if (operationResult != null)
				{
					foreach (Message message in operationResult.Messages)
					{
						_reporter?.ReportMessage(message);
					}
				}
				return ITransport.Status.ERROR;
			}
			AddPhase(TransportPhase.MatrixSolve, timestamp);
			timestamp = Stopwatch.GetTimestamp();
			_threadFor.Run(cellCount, OuterErrorRange);
			AddPhase(TransportPhase.Update, timestamp);
			if (!condition)
			{
				_convergence.OuterConv.Status = LoopConvergence.ConvStatus.SKIPPED;
				break;
			}
			_convergence.ComputeOuter(_control.OuterControl, _outerMaxError, _allCells, _activeMask);
			void ApplyImplicitRelaxationRange(int start, int stop)
			{
				for (int intervalIndex4 = 0; intervalIndex4 < nc; intervalIndex4++)
				{
					SparseMatrix<double> sparseMatrix = _A[intervalIndex4];
					double[] values = sparseMatrix.Values;
					int[] diagonalPointers = sparseMatrix.DiagonalPointers;
					double[] rhs = _rhs[intervalIndex4];
					double[] cellConcentrationArray = _state.CellConcentration[intervalIndex4];
					for (int start2 = start; start2 <= stop; start2++)
					{
						if (_activeMask[start2] && !(_effectiveNewVolume[start2] <= 0.0))
						{
							int diagonalPointer = diagonalPointers[start2];
							double diagonalPointerTerm = values[diagonalPointer];
							rhs[start2] += rhsCorrectionFactor * diagonalPointerTerm * cellConcentrationArray[start2];
							values[diagonalPointer] = inverseRelaxation * diagonalPointerTerm;
						}
					}
				}
			}
		}
		if (_convergence.OuterConv.Status == LoopConvergence.ConvStatus.MAX_ITERATIONS && _control.FailOnOuterNonConvergence)
		{
			return ITransport.Status.ERROR;
		}
		return ClampAndValidateStateRanges(_activeMask, out failureCell);
		void ApplyBoundaryFacesRange(int start, int stop)
		{
			for (int start3 = start; start3 <= stop; start3++)
			{
				_boundaryFailureCell[start3] = ApplyBoundaryFaces(start3, theta, oneMinusTheta, _oldState.CellConcentration[start3]);
			}
		}
		void AssembleCellRange(int start, int stop)
		{
			for (int ordinal2 = 0; ordinal2 < nc; ordinal2++)
			{
				for (int start4 = start; start4 <= stop; start4++)
				{
					AssembleCell(ordinal2, start4, dt, theta, invDt);
				}
			}
		}
		void AssembleFaceRange(int start, int stop)
		{
			for (int ordinal3 = 0; ordinal3 < nc; ordinal3++)
			{
				for (int start5 = start; start5 <= stop; start5++)
				{
					AssembleFace(ordinal3, start5, theta, oneMinusTheta, useTVD);
				}
			}
		}
		void CasulliUpstreamRange(int start, int stop)
		{
			bool thetaIterationsTerm = theta < 1.0 && _convergence.OuterConv.Iterations == 1;
			for (int intervalIndex5 = 0; intervalIndex5 < nc; intervalIndex5++)
			{
				double[] cellConcentration2 = _state.CellConcentration[intervalIndex5];
				double[] tvdCasulliUpstreamNew = _TVDCasulliUpstreamNew[intervalIndex5];
				for (int start6 = start; start6 <= stop; start6++)
				{
					tvdCasulliUpstreamNew[start6] = (_TVDCasulliInterpolationNew.TryInterpolateUpstreamValueUnchecked(start6, cellConcentration2, _boundaryNew[intervalIndex5], out var upstreamValue) ? upstreamValue : double.NaN);
				}
				if (thetaIterationsTerm)
				{
					double[] cellConcentration3 = _oldState.CellConcentration[intervalIndex5];
					double[] tvdCasulliUpstreamOld = _TVDCasulliUpstreamOld[intervalIndex5];
					for (int start7 = start; start7 <= stop; start7++)
					{
						tvdCasulliUpstreamOld[start7] = (_TVDCasulliInterpolationOld.TryInterpolateUpstreamValueUnchecked(start7, cellConcentration3, _boundaryOld[intervalIndex5], out var upstreamValue2) ? upstreamValue2 : double.NaN);
					}
				}
			}
		}
		void GradientsRange(int start, int stop)
		{
			for (int intervalIndex6 = 0; intervalIndex6 < nc; intervalIndex6++)
			{
				if (NeedGradient(intervalIndex6, useTVD))
				{
					bool needsDiffusionGradient = NeedsDiffusionGradient(intervalIndex6);
					double[] cellConcentration4 = _state.CellConcentration[intervalIndex6];
					Vector[] gradNew = _gradNew[intervalIndex6];
					double[] cellGradientLimiterNew = _cellGradientLimiterNew[intervalIndex6];
					for (int start8 = start; start8 <= stop; start8++)
					{
						Vector gradient = (gradNew[start8] = _gradientNewWorkspace.ScalarGradient(start8, cellConcentration4));
						if (needsDiffusionGradient)
						{
							cellGradientLimiterNew[start8] = _gradientNewWorkspace.ScalarGradientLimiter(start8, gradient, cellConcentration4, _control.multidimensionalSlopeLimiter);
						}
					}
					if (theta < 1.0 && _convergence.OuterConv.Iterations == 1)
					{
						double[] cellConcentration5 = _oldState.CellConcentration[intervalIndex6];
						Vector[] gradOld = _gradOld[intervalIndex6];
						double[] cellGradientLimiterOld = _cellGradientLimiterOld[intervalIndex6];
						for (int start9 = start; start9 <= stop; start9++)
						{
							Vector gradient2 = (gradOld[start9] = _gradientOldWorkspace.ScalarGradient(start9, cellConcentration5));
							if (needsDiffusionGradient)
							{
								cellGradientLimiterOld[start9] = _gradientOldWorkspace.ScalarGradientLimiter(start9, gradient2, cellConcentration5, _control.multidimensionalSlopeLimiter);
							}
						}
					}
				}
			}
		}
		void NodeValuesRange(int start, int stop)
		{
			for (int intervalIndex7 = 0; intervalIndex7 < nc; intervalIndex7++)
			{
				if (NeedsDiffusionNodeValues(intervalIndex7))
				{
					for (int start10 = start; start10 <= stop; start10++)
					{
						_hmesh.NodeComputes.InterpolateNodeFaceValues(start10, _nodeInterpolationNew, _state.CellConcentration[intervalIndex7], _nodeFaceNew[intervalIndex7]);
						if (theta < 1.0 && _convergence.OuterConv.Iterations == 1)
						{
							_hmesh.NodeComputes.InterpolateNodeFaceValues(start10, _nodeInterpolationOld, _oldState.CellConcentration[intervalIndex7], _nodeFaceOld[intervalIndex7]);
						}
					}
				}
			}
		}
		void OuterErrorRange(int start, int stop)
		{
			double[][] cellConcentration = _state.CellConcentration;
			double[][] sol = _sol;
			double[][] outerError = _outerError;
			double[] outerMaxError = _outerMaxError;
			bool[] activeMask = _activeMask;
			for (int start11 = start; start11 <= stop; start11++)
			{
				if (!activeMask[start11])
				{
					outerMaxError[start11] = 0.0;
					for (int intervalIndex8 = 0; intervalIndex8 < nc; intervalIndex8++)
					{
						outerError[intervalIndex8][start11] = 0.0;
					}
				}
				else
				{
					double outerMaxError2 = 0.0;
					for (int intervalIndex9 = 0; intervalIndex9 < nc; intervalIndex9++)
					{
						double[] cellConcentration6 = cellConcentration[intervalIndex9];
						double sol2 = sol[intervalIndex9][start11];
						double outerError2 = Math.Abs(sol2 - cellConcentration6[start11]);
						outerError[intervalIndex9][start11] = outerError2;
						cellConcentration6[start11] = sol2;
						if (outerError2 > outerMaxError2)
						{
							outerMaxError2 = outerError2;
						}
					}
					outerMaxError[start11] = outerMaxError2;
				}
			}
		}
		void PrepareAndSolveConstituentRange(int start, int stop)
		{
			for (int start12 = start; start12 <= stop; start12++)
			{
				solveResults[start12] = _matSolver[start12].PrepareAndSolve(_A[start12], _sol[start12], _rhs[start12], null, partialSolve);
			}
		}
		void PrepareGradientRange(int start, int stop)
		{
			ReadOnlySpan<double> cellVolumes = _effectiveNewVolume;
			ReadOnlySpan<double> cellVolumes2 = _flow.CellVolumeOld;
			ReadOnlySpan<double> faceAreas = _flow.FaceAreaNew;
			ReadOnlySpan<double> faceAreas2 = _flow.FaceAreaOld;
			for (int start13 = start; start13 <= stop; start13++)
			{
				_gradientNewWorkspace.PrepareCell(start13, cellVolumes, faceAreas, _flow.CellActive, _flow.FaceActive);
				if (theta < 1.0)
				{
					_gradientOldWorkspace.PrepareCell(start13, cellVolumes2, faceAreas2, _flow.CellActive, _flow.FaceActive);
				}
			}
		}
	}

	private bool NeedGradient(int k, bool useTVD)
	{
		if (!useTVD || _control.TVDGradientRatioMethod != TVDGradientRatioMethod.DarwishMoukalledCellGradient)
		{
			return NeedsDiffusionGradient(k);
		}
		return true;
	}

	private void EnsureTVDCasulliBuffers(bool includeOldLevel)
	{
		int cellCount = _mesh.CellCount;
		if (_TVDCasulliInterpolationNew == null)
		{
			_TVDCasulliInterpolationNew = new CasulliFaceFlowInterpolationWorkspace(_connectivity);
		}
		if (_TVDCasulliUpstreamNew == null)
		{
			_TVDCasulliUpstreamNew = new double[_constituentCount][];
			for (int i = 0; i < _constituentCount; i++)
			{
				_TVDCasulliUpstreamNew[i] = new double[cellCount];
			}
		}
		if (!includeOldLevel)
		{
			return;
		}
		if (_TVDCasulliInterpolationOld == null)
		{
			_TVDCasulliInterpolationOld = new CasulliFaceFlowInterpolationWorkspace(_connectivity);
		}
		if (_TVDCasulliUpstreamOld == null)
		{
			_TVDCasulliUpstreamOld = new double[_constituentCount][];
			for (int j = 0; j < _constituentCount; j++)
			{
				_TVDCasulliUpstreamOld[j] = new double[cellCount];
			}
		}
	}

	private bool NeedsDeferredCorrection(int k, bool useTVD)
	{
		if (!useTVD && !NeedsDiffusionGradient(k))
		{
			return NeedsDiffusionNodeValues(k);
		}
		return true;
	}

	private void AddPhase(TransportPhase phase, long startTimestamp)
	{
		PhaseSeconds[(int)phase] += Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
	}

	private bool MatricesShared()
	{
		for (int i = 1; i < _constituentCount; i++)
		{
			if (_diffusivity[i] != _diffusivity[0])
			{
				return false;
			}
			if (_faceDiffusivity[i] != _faceDiffusivity[0])
			{
				return false;
			}
			if (_faceAdvectionAlpha[i] != _faceAdvectionAlpha[0])
			{
				return false;
			}
			if (_cellTemporalBeta[i] != _cellTemporalBeta[0])
			{
				return false;
			}
			if (!ArraysEqual(_cellSinkCoeffNew[i], _cellSinkCoeffNew[0]))
			{
				return false;
			}
		}
		return true;
	}

	private static bool ArraysEqual(double[] a, double[] b)
	{
		if (a == b)
		{
			return true;
		}
		if (a == null || b == null)
		{
			return false;
		}
		if (a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i])
			{
				return false;
			}
		}
		return true;
	}

	private OperationResult SolveSharedMultiRHS(Pardiso solver, int nc, int n)
	{
		int count = n * nc;
		if (_bPacked == null || _bPacked.Length < count)
		{
			_bPacked = new double[count];
			_xPacked = new double[count];
		}
		for (int i = 0; i < nc; i++)
		{
			Array.Copy(_rhs[i], 0, _bPacked, i * n, n);
		}
		SparseMatrix<double> sparseMatrix = _A[0];
		int nrhs = solver.nrhs;
		solver.nrhs = nc;
		OperationResult operationResult;
		try
		{
			operationResult = solver.AnalysisFactorizationSolve(sparseMatrix.RowCount, sparseMatrix.RowPointers, sparseMatrix.ColumnIndices, sparseMatrix.Values, _xPacked, _bPacked);
		}
		finally
		{
			solver.nrhs = nrhs;
		}
		if (!operationResult.Result)
		{
			return operationResult;
		}
		for (int j = 0; j < nc; j++)
		{
			Array.Copy(_xPacked, j * n, _sol[j], 0, n);
		}
		return operationResult;
	}

	private void AssembleFace(int k, int f, double theta, double oneMinusTheta, bool useTVD)
	{
		int faceCellA = _connectivity.FaceCellA[f];
		int faceCellB = _connectivity.FaceCellB[f];
		if (faceCellB < 0)
		{
			return;
		}
		if (!_flow.IsFaceActive(f) || !_activeMask[faceCellA] || !_activeMask[faceCellB])
		{
			_faceMass[k][f] = 0.0;
			_faceCrossA[k][f] = 0.0;
			return;
		}
		double diffusivityAt = DiffusivityAt(k, f);
		double advectionAlphaAt = AdvectionAlphaAt(k, f);
		bool faceNormalGradientMethodControlTerm = _control.faceNormalGradientMethod != FaceNormalGradientMethod.TwoPoint && diffusivityAt > 0.0;
		double[] cellConcentration = _oldState.CellConcentration[k];
		double[] c = _state.CellConcentration[k];
		double[] faceFlowNew = _flow.FaceFlowNew;
		double[] faceFlowOld = _flow.FaceFlowOld;
		double[] faceAreaNew = _flow.FaceAreaNew;
		double gPerp = Math.Max(_gPerp[f], 0.0);
		double faceMass = 0.0;
		if (oneMinusTheta > 0.0)
		{
			double faceFlowOld2 = faceFlowOld[f];
			double cellConcentration2 = ((faceFlowOld2 > 0.0) ? cellConcentration[faceCellA] : cellConcentration[faceCellB]);
			double advectionAlphaAtFaceFlowOld2Term = advectionAlphaAt * faceFlowOld2 * cellConcentration2;
			double diffusivityAtFaceAreaOldTerm = ((diffusivityAt > 0.0) ? (diffusivityAt * _flow.FaceAreaOld[f] * gPerp) : 0.0) * (cellConcentration[faceCellA] - cellConcentration[faceCellB]);
			faceMass += oneMinusTheta * (advectionAlphaAtFaceFlowOld2Term + diffusivityAtFaceAreaOldTerm);
		}
		if (useTVD && advectionAlphaAt != 0.0)
		{
			double faceMass2 = faceMass;
			double thetaTimesAdvectionAlphaAt = theta * advectionAlphaAt;
			Vector[] grad = _gradNew[k];
			HydraulicMeshComputer.TVDVirtualPointInterpolationWorkspace tVDVirtualPointInterpolationNew = _TVDVirtualPointInterpolationNew;
			CasulliFaceFlowInterpolationWorkspace tVDCasulliInterpolationNew = _TVDCasulliInterpolationNew;
			double[][] tVDCasulliUpstreamNew = _TVDCasulliUpstreamNew;
			faceMass = faceMass2 + thetaTimesAdvectionAlphaAt * AdvHoDeltaMassAB(f, c, grad, tVDVirtualPointInterpolationNew, tVDCasulliInterpolationNew, (tVDCasulliUpstreamNew != null) ? tVDCasulliUpstreamNew[k] : null, faceFlowNew[f]);
			if (oneMinusTheta > 0.0)
			{
				double faceMass3 = faceMass;
				double oneMinusThetaTimesAdvectionAlphaAt = oneMinusTheta * advectionAlphaAt;
				Vector[] grad2 = _gradOld[k];
				HydraulicMeshComputer.TVDVirtualPointInterpolationWorkspace tVDVirtualPointInterpolationOld = _TVDVirtualPointInterpolationOld;
				CasulliFaceFlowInterpolationWorkspace tVDCasulliInterpolationOld = _TVDCasulliInterpolationOld;
				double[][] tVDCasulliUpstreamOld = _TVDCasulliUpstreamOld;
				faceMass = faceMass3 + oneMinusThetaTimesAdvectionAlphaAt * AdvHoDeltaMassAB(f, cellConcentration, grad2, tVDVirtualPointInterpolationOld, tVDCasulliInterpolationOld, (tVDCasulliUpstreamOld != null) ? tVDCasulliUpstreamOld[k] : null, faceFlowOld[f]);
			}
		}
		_faceMass[k][f] = faceMass;
		double faceCrossA = 0.0;
		if (faceNormalGradientMethodControlTerm)
		{
			faceCrossA += theta * CrossIntoA(f, c, _gradNew[k], _cellGradientLimiterNew[k], _nodeFaceNew[k], _nodeInterpolationNew, faceAreaNew[f], diffusivityAt, gPerp);
			if (oneMinusTheta > 0.0)
			{
				faceCrossA += oneMinusTheta * CrossIntoA(f, cellConcentration, _gradOld[k], _cellGradientLimiterOld[k], _nodeFaceOld[k], _nodeInterpolationOld, _flow.FaceAreaOld[f], diffusivityAt, gPerp);
			}
		}
		_faceCrossA[k][f] = faceCrossA;
	}

	private void AssembleCell(int k, int i, double dt, double theta, double invDt)
	{
		double diffusivity = _diffusivity[k];
		double[] faceDiffusivity = _faceDiffusivity[k];
		double[] faceAdvectionAlpha = _faceAdvectionAlpha[k];
		double[] cellTemporalBetaNew = _cellTemporalBetaNew[k];
		double[] cellTemporalBetaOld = _cellTemporalBetaOld[k];
		double[] cellSinkCoeffNew = _cellSinkCoeffNew[k];
		double[] cellSinkCoeffOld = _cellSinkCoeffOld[k];
		double[] cellSourceMassRateNew = _cellSourceMassRateNew[k];
		double[] cellSourceMassRateOld = _cellSourceMassRateOld[k];
		double[] cellConcentration = _oldState.CellConcentration[k];
		double[] cellVolumeOld = _flow.CellVolumeOld;
		double[] faceFlowNew = _flow.FaceFlowNew;
		double[] faceAreaNew = _flow.FaceAreaNew;
		double[] values = _A[k].Values;
		int[] rowPointers = _A[k].RowPointers;
		int[] diagonalPointers = _A[k].DiagonalPointers;
		double[] faceMass = _faceMass[k];
		double[] faceCrossA = _faceCrossA[k];
		if (!_activeMask[i] || _effectiveNewVolume[i] <= 0.0)
		{
			int rowPointer = rowPointers[i];
			int rowPointer2 = rowPointers[i + 1];
			Array.Clear(values, rowPointer, rowPointer2 - rowPointer);
			values[diagonalPointers[i]] = 1.0;
			_rhs[k][i] = cellConcentration[i];
			return;
		}
		double effectiveNewVolumeInvDtTerm = _effectiveNewVolume[i] * invDt;
		if (cellTemporalBetaNew != null)
		{
			effectiveNewVolumeInvDtTerm /= cellTemporalBetaNew[i];
		}
		bool sourceTimeIntegrationControlTerm = _control.sourceTimeIntegration == SourceTimeIntegration.ThetaWeighted;
		double theta2 = (sourceTimeIntegrationControlTerm ? theta : 1.0);
		if (cellSinkCoeffNew != null)
		{
			effectiveNewVolumeInvDtTerm -= theta2 * cellSinkCoeffNew[i];
		}
		int rowPointerStart = rowPointers[i];
		int rowPointerEnd = rowPointers[i + 1];
		double computed = 0.0;
		for (int j = rowPointerStart; j < rowPointerEnd; j++)
		{
			int stencilFace = _stencilFace[j];
			if (stencilFace < 0)
			{
				if (stencilFace == -2)
				{
					values[j] = 0.0;
				}
				continue;
			}
			if (!_flow.IsFaceActive(stencilFace))
			{
				values[j] = 0.0;
				continue;
			}
			int columnIndex = _A[k].ColumnIndices[j];
			if (!_activeMask[columnIndex])
			{
				values[j] = 0.0;
				continue;
			}
			int stencilSign = _stencilSign[j];
			double stencilSignFaceFlowNewTerm = (double)stencilSign * faceFlowNew[stencilFace];
			if (faceAdvectionAlpha != null)
			{
				stencilSignFaceFlowNewTerm *= faceAdvectionAlpha[stencilFace];
			}
			double faceDiffusivity2 = ((faceDiffusivity != null) ? faceDiffusivity[stencilFace] : diffusivity);
			double faceDiffusivity2FaceAreaNewTerm = ((faceDiffusivity2 > 0.0) ? (faceDiffusivity2 * faceAreaNew[stencilFace] * Math.Max(_gPerp[stencilFace], 0.0)) : 0.0);
			values[j] = theta * (Math.Min(stencilSignFaceFlowNewTerm, 0.0) - faceDiffusivity2FaceAreaNewTerm);
			effectiveNewVolumeInvDtTerm += theta * (Math.Max(stencilSignFaceFlowNewTerm, 0.0) + faceDiffusivity2FaceAreaNewTerm);
			computed += (double)stencilSign * (faceCrossA[stencilFace] - faceMass[stencilFace]);
		}
		CellWaterSourceSnapshot cellWaterSources = _flow.CellWaterSources;
		double thetaOffset = 1.0 - theta;
		double advectiveOutflow = cellWaterSources.GetAdvectiveOutflow(i);
		effectiveNewVolumeInvDtTerm += theta * advectiveOutflow;
		values[diagonalPointers[i]] = effectiveNewVolumeInvDtTerm;
		double rhs = cellVolumeOld[i] * invDt * cellConcentration[i];
		if (cellTemporalBetaOld != null)
		{
			rhs /= cellTemporalBetaOld[i];
		}
		if (cellSourceMassRateNew != null)
		{
			rhs += theta2 * cellSourceMassRateNew[i];
		}
		if (sourceTimeIntegrationControlTerm)
		{
			double thetaOffset2 = 1.0 - theta;
			if (cellSourceMassRateOld != null)
			{
				rhs += thetaOffset2 * cellSourceMassRateOld[i];
			}
			if (cellSinkCoeffOld != null)
			{
				rhs += thetaOffset2 * cellSinkCoeffOld[i] * cellConcentration[i];
			}
		}
		rhs -= thetaOffset * advectiveOutflow * cellConcentration[i];
		ReadOnlySpan<CellWaterExternalInflow> externalInflows = cellWaterSources.ExternalInflows;
		ReadOnlySpan<int> incomingExternalIndices = cellWaterSources.GetIncomingExternalIndices(i);
		for (int l = 0; l < incomingExternalIndices.Length; l++)
		{
			int index = incomingExternalIndices[l];
			CellWaterExternalInflow cellWaterExternalInflow = externalInflows[index];
			rhs += cellWaterExternalInflow.Flow * _externalSourceConcentration[k][cellWaterExternalInflow.SourceId.Value];
		}
		ReadOnlySpan<CellWaterTransfer> transfers = cellWaterSources.Transfers;
		ReadOnlySpan<int> incomingTransferIndices = cellWaterSources.GetIncomingTransferIndices(i);
		for (int l = 0; l < incomingTransferIndices.Length; l++)
		{
			int incomingTransferIndex = incomingTransferIndices[l];
			CellWaterTransfer cellWaterTransfer = transfers[incomingTransferIndex];
			if (cellWaterTransfer.Flow > 0.0)
			{
				int transferMatrixIndex = _transferMatrixIndex[incomingTransferIndex];
				if (transferMatrixIndex >= 0)
				{
					values[transferMatrixIndex] -= theta * cellWaterTransfer.Flow;
				}
				rhs += thetaOffset * cellWaterTransfer.Flow * cellConcentration[cellWaterTransfer.DonorCell];
			}
		}
		rhs += computed;
		_rhs[k][i] = rhs;
	}

	private double AdvHoDeltaMassAB(int f, double[] C, Vector[] grad, HydraulicMeshComputer.TVDVirtualPointInterpolationWorkspace TVDVirtualPointInterpolation, CasulliFaceFlowInterpolationWorkspace TVDCasulliInterpolation, double[] TVDCasulliUpstreamValues, double faceFlow)
	{
		if (faceFlow == 0.0)
		{
			return 0.0;
		}
		bool cellAIsUpwind = faceFlow > 0.0;
		double tvdDeferredCorrection = ComputeTVDDeferredCorrection(f, cellAIsUpwind, C, grad, TVDVirtualPointInterpolation, TVDCasulliInterpolation, TVDCasulliUpstreamValues);
		return faceFlow * tvdDeferredCorrection;
	}

	private double CrossIntoA(int f, double[] C, Vector[] grad, double[] gradientLimiters, double[] nodeFaceValues, HydraulicMeshComputer.NodeFaceInterpolationWorkspace nodeInterpolation, double faceArea, double diffusivity, double gp)
	{
		double faceNormalGradientCorrection = ComputeFaceNormalGradientCorrection(f, C, grad, gradientLimiters, nodeFaceValues, nodeInterpolation, gp);
		return diffusivity * faceArea * faceNormalGradientCorrection;
	}

	private int ApplyBoundaryFaces(int k, double theta, double oneMinusTheta, double[] Cn)
	{
		double[] values = _A[k].Values;
		int[] diagonalPointers = _A[k].DiagonalPointers;
		double[] rhs = _rhs[k];
		double[] faceFlowNew = _flow.FaceFlowNew;
		double[] faceFlowOld = _flow.FaceFlowOld;
		int[] boundaryFaces = _boundaryFaces;
		foreach (int boundaryFaceIndex in boundaryFaces)
		{
			int faceCellA = _connectivity.FaceCellA[boundaryFaceIndex];
			if (!_flow.IsFaceActive(boundaryFaceIndex) || !_activeMask[faceCellA])
			{
				continue;
			}
			double faceFlowNew2 = faceFlowNew[boundaryFaceIndex];
			double faceFlowOld2 = faceFlowOld[boundaryFaceIndex];
			double advectionAlphaAt = AdvectionAlphaAt(k, boundaryFaceIndex);
			double advectionAlphaAtTimesFaceFlowNew2 = advectionAlphaAt * faceFlowNew2;
			double advectionAlphaAtTimesFaceFlowOld2 = advectionAlphaAt * faceFlowOld2;
			TransportBoundaryCondition transportBoundaryCondition = _bcLookup[boundaryFaceIndex];
			switch (transportBoundaryCondition?.Type ?? TransportBCType.ZeroGradientOutflow)
			{
			case TransportBCType.ZeroGradientOutflow:
			{
				double diffusivityAt = DiffusivityAt(k, boundaryFaceIndex);
				double boundaryPerpDistance = BoundaryPerpDistance(boundaryFaceIndex, faceCellA);
				if (faceFlowNew2 < -1E-08)
				{
					if (!TryEquilibriumConcentration(k, faceCellA, oldLevel: false, out var concentration))
					{
						return faceCellA;
					}
					rhs[faceCellA] -= theta * advectionAlphaAtTimesFaceFlowNew2 * concentration;
					if (diffusivityAt > 0.0)
					{
						double diffusivityAtFaceAreaNewTerm = diffusivityAt * _flow.FaceAreaNew[boundaryFaceIndex] / boundaryPerpDistance;
						values[diagonalPointers[faceCellA]] += theta * diffusivityAtFaceAreaNewTerm;
						rhs[faceCellA] += theta * diffusivityAtFaceAreaNewTerm * concentration;
					}
				}
				else
				{
					values[diagonalPointers[faceCellA]] += theta * advectionAlphaAtTimesFaceFlowNew2;
				}
				if (!(oneMinusTheta > 0.0))
				{
					break;
				}
				if (faceFlowOld2 < -1E-08)
				{
					if (!TryEquilibriumConcentration(k, faceCellA, oldLevel: true, out var concentration2))
					{
						return faceCellA;
					}
					rhs[faceCellA] -= oneMinusTheta * advectionAlphaAtTimesFaceFlowOld2 * concentration2;
					if (diffusivityAt > 0.0)
					{
						double diffusivityAtFaceAreaOldTerm = diffusivityAt * _flow.FaceAreaOld[boundaryFaceIndex] / boundaryPerpDistance;
						rhs[faceCellA] += oneMinusTheta * diffusivityAtFaceAreaOldTerm * (concentration2 - Cn[faceCellA]);
					}
				}
				else
				{
					rhs[faceCellA] -= oneMinusTheta * advectionAlphaAtTimesFaceFlowOld2 * Cn[faceCellA];
				}
				break;
			}
			case TransportBCType.DirichletInflow:
			{
				double concentrationAt = transportBoundaryCondition.ConcentrationAt(k, _bcFaceSlot[boundaryFaceIndex]);
				if (faceFlowNew2 < 0.0)
				{
					rhs[faceCellA] -= theta * advectionAlphaAtTimesFaceFlowNew2 * concentrationAt;
				}
				else
				{
					values[diagonalPointers[faceCellA]] += theta * advectionAlphaAtTimesFaceFlowNew2;
				}
				if (oneMinusTheta > 0.0)
				{
					if (faceFlowOld2 < 0.0)
					{
						rhs[faceCellA] -= oneMinusTheta * advectionAlphaAtTimesFaceFlowOld2 * concentrationAt;
					}
					else
					{
						rhs[faceCellA] -= oneMinusTheta * advectionAlphaAtTimesFaceFlowOld2 * Cn[faceCellA];
					}
				}
				double diffusivityAt2 = DiffusivityAt(k, boundaryFaceIndex);
				if (diffusivityAt2 > 0.0)
				{
					double boundaryPerpDistance2 = BoundaryPerpDistance(boundaryFaceIndex, faceCellA);
					double diffusivityAt2FaceAreaNewTerm = diffusivityAt2 * _flow.FaceAreaNew[boundaryFaceIndex] / boundaryPerpDistance2;
					values[diagonalPointers[faceCellA]] += theta * diffusivityAt2FaceAreaNewTerm;
					rhs[faceCellA] += theta * diffusivityAt2FaceAreaNewTerm * concentrationAt;
					if (oneMinusTheta > 0.0)
					{
						double diffusivityAt2FaceAreaOldTerm = diffusivityAt2 * _flow.FaceAreaOld[boundaryFaceIndex] / boundaryPerpDistance2;
						rhs[faceCellA] += oneMinusTheta * diffusivityAt2FaceAreaOldTerm * (concentrationAt - Cn[faceCellA]);
					}
				}
				break;
			}
			case TransportBCType.EquilibriumInflow:
			{
				double concentrationAt2 = transportBoundaryCondition.ConcentrationAt(k, _bcFaceSlot[boundaryFaceIndex]);
				double boundaryPerpDistance3 = BoundaryPerpDistance(boundaryFaceIndex, faceCellA);
				double diffusivityAt3 = DiffusivityAt(k, boundaryFaceIndex);
				if (faceFlowNew2 < 0.0)
				{
					rhs[faceCellA] -= theta * advectionAlphaAtTimesFaceFlowNew2 * concentrationAt2;
					if (diffusivityAt3 > 0.0)
					{
						double diffusivityAt3FaceAreaNewTerm = diffusivityAt3 * _flow.FaceAreaNew[boundaryFaceIndex] / boundaryPerpDistance3;
						values[diagonalPointers[faceCellA]] += theta * diffusivityAt3FaceAreaNewTerm;
						rhs[faceCellA] += theta * diffusivityAt3FaceAreaNewTerm * concentrationAt2;
					}
				}
				else
				{
					values[diagonalPointers[faceCellA]] += theta * advectionAlphaAtTimesFaceFlowNew2;
				}
				if (!(oneMinusTheta > 0.0))
				{
					break;
				}
				if (faceFlowOld2 < 0.0)
				{
					rhs[faceCellA] -= oneMinusTheta * advectionAlphaAtTimesFaceFlowOld2 * concentrationAt2;
					if (diffusivityAt3 > 0.0)
					{
						double diffusivityAt3FaceAreaOldTerm = diffusivityAt3 * _flow.FaceAreaOld[boundaryFaceIndex] / boundaryPerpDistance3;
						rhs[faceCellA] += oneMinusTheta * diffusivityAt3FaceAreaOldTerm * (concentrationAt2 - Cn[faceCellA]);
					}
				}
				else
				{
					rhs[faceCellA] -= oneMinusTheta * advectionAlphaAtTimesFaceFlowOld2 * Cn[faceCellA];
				}
				break;
			}
			case TransportBCType.PrescribedMassFlux:
			{
				double concentrationAt3 = transportBoundaryCondition.ConcentrationAt(k, _bcFaceSlot[boundaryFaceIndex]);
				rhs[faceCellA] -= theta * advectionAlphaAtTimesFaceFlowNew2 * concentrationAt3;
				if (oneMinusTheta > 0.0)
				{
					rhs[faceCellA] -= oneMinusTheta * advectionAlphaAtTimesFaceFlowOld2 * concentrationAt3;
				}
				break;
			}
			}
			double diffusivityAt4 = DiffusivityAt(k, boundaryFaceIndex);
			if (!(diffusivityAt4 <= 0.0) && _control.faceNormalGradientMethod != FaceNormalGradientMethod.TwoPoint)
			{
				double inversePerpendicularDistance = 1.0 / BoundaryPerpDistance(boundaryFaceIndex, faceCellA);
				if (_boundaryNew[k].TryGetCondition(boundaryFaceIndex, out var type, out var localValue) && type == ScalarBoundaryConditionType.Value)
				{
					double faceNormalGradientCorrection = _boundaryNew[k].FaceNormalGradientCorrection(boundaryFaceIndex, _control.faceNormalGradientMethod, _gradNew[k], _cellGradientLimiterNew[k], _nodeFaceNew[k], _nodeInterpolationNew, inversePerpendicularDistance);
					rhs[faceCellA] += theta * diffusivityAt4 * _flow.FaceAreaNew[boundaryFaceIndex] * faceNormalGradientCorrection;
				}
				if (oneMinusTheta > 0.0 && _boundaryOld[k].TryGetCondition(boundaryFaceIndex, out var type2, out localValue) && type2 == ScalarBoundaryConditionType.Value)
				{
					double faceNormalGradientCorrection2 = _boundaryOld[k].FaceNormalGradientCorrection(boundaryFaceIndex, _control.faceNormalGradientMethod, _gradOld[k], _cellGradientLimiterOld[k], _nodeFaceOld[k], _nodeInterpolationOld, inversePerpendicularDistance);
					rhs[faceCellA] += oneMinusTheta * diffusivityAt4 * _flow.FaceAreaOld[boundaryFaceIndex] * faceNormalGradientCorrection2;
				}
			}
		}
		return -1;
	}

	public override void Cleanup()
	{
		if (_matSolver != null && _A != null)
		{
			for (int i = 0; i < _matSolver.Length; i++)
			{
				if (_matSolver[i] == null || _A[i] == null)
				{
					continue;
				}
				OperationResult operationResult = _matSolver[i].Destroy(_A[i]);
				if (operationResult.Result)
				{
					continue;
				}
				foreach (Message message in operationResult.Messages)
				{
					_reporter?.ReportMessage(message);
				}
			}
		}
		_matSolver = null;
		_A = null;
		base.Cleanup();
	}
}
