using System;
using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine.Engine;

namespace Ras.Engine;

public class TransportSolverExp(Mesh mesh, TransportControl control, TransportFlowField flow, int constituentCount, TransportBoundaryConditionCollection bcs = null, SolverConvergence convergence = null) : TransportSolver(mesh, control, flow, constituentCount, bcs, convergence)
{
	private double[][] _faceFlux;

	private double[][] _rPredictor;

	private double[][] _rCorrector;

	private double[][] _cStart;

	private double[][] _cStage;

	private Vector[][] _grad;

	private double[][] _cellGradientLimiter;

	private double[][] _nodeFaceValues;

	private HydraulicMeshComputer.NodeFaceInterpolationWorkspace _nodeFaceInterpolation;

	private HydraulicMeshComputer.ScalarGradientWlsWorkspace _gradientWorkspace;

	private HydraulicMeshComputer.TVDVirtualPointInterpolationWorkspace _TVDVirtualPointInterpolation;

	private HydraulicMeshComputer.TVDVirtualPointInterpolationWorkspace _TVDVirtualPointInterpolationAlternate;

	private CasulliFaceFlowInterpolationWorkspace _TVDCasulliInterpolation;

	private double[][] _TVDCasulliUpstreamValues;

	private HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace[] _boundaryReconstruction;

	private double[] _volStart;

	private double[] _volPred;

	private double[] _volEnd;

	private double[] _subFlow0;

	private double[] _subFlow1;

	private double[] _subArea0;

	private double[] _subArea1;

	private double[] _subVol0;

	private double[] _subVol1;

	private double[][] _subBeta0;

	private double[][] _subBeta1;

	private double[] _lerpFlow0;

	private double[] _lerpFlow1;

	private double[] _lerpArea0;

	private double[] _lerpArea1;

	private double[] _lerpVol0;

	private double[] _lerpVol1;

	private double[][] _lerpBeta0;

	private double[][] _lerpBeta1;

	private int[] _boundaryFaces;

	private TransportBoundaryCondition[] _bcLookup;

	private int[] _bcFaceSlot;

	private double[] _gPerp;

	private double[] _dtCell;

	private double[] _maxFaceAdvectionAlpha;

	private double[] _maxFaceDiffusivity;

	public int LastSubStepCount { get; private set; } = 1;

	protected override void PrepareInternal()
	{
		int cellCount = _mesh.CellCount;
		int faceCount = _mesh.FaceCount;
		int constituentCount = _constituentCount;
		_TVDCasulliInterpolation = null;
		_TVDCasulliUpstreamValues = null;
		_gPerp = _hmesh.FaceInvPerpDistances;
		List<int> intList = new List<int>();
		for (int i = 0; i < faceCount; i++)
		{
			if (_connectivity.FaceCellB[i] < 0)
			{
				intList.Add(i);
			}
		}
		_boundaryFaces = intList.ToArray();
		_bcLookup = _bcs.BuildFaceLookup(faceCount, out _bcFaceSlot);
		_faceFlux = new double[constituentCount][];
		_rPredictor = new double[constituentCount][];
		_rCorrector = new double[constituentCount][];
		_cStart = new double[constituentCount][];
		_cStage = new double[constituentCount][];
		_grad = new Vector[constituentCount][];
		_cellGradientLimiter = new double[constituentCount][];
		_nodeFaceValues = new double[constituentCount][];
		_subBeta0 = new double[constituentCount][];
		_subBeta1 = new double[constituentCount][];
		_boundaryReconstruction = new HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace[constituentCount];
		for (int j = 0; j < constituentCount; j++)
		{
			_faceFlux[j] = new double[faceCount];
			_rPredictor[j] = new double[cellCount];
			_rCorrector[j] = new double[cellCount];
			_cStart[j] = new double[cellCount];
			_cStage[j] = new double[cellCount];
			_grad[j] = new Vector[cellCount];
			if (UsesCellGradientCorrection)
			{
				_cellGradientLimiter[j] = new double[cellCount];
			}
			if (_control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased)
			{
				_nodeFaceValues[j] = new double[_hmesh.NodeComputes.NodeFaceValueCount];
			}
			_boundaryReconstruction[j] = _hmesh.CreateScalarBoundaryReconstructionWorkspace();
		}
		_volStart = new double[cellCount];
		_volPred = new double[cellCount];
		_volEnd = new double[cellCount];
		_dtCell = new double[cellCount];
		_maxFaceAdvectionAlpha = new double[faceCount];
		_maxFaceDiffusivity = new double[faceCount];
		if (_control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased)
		{
			_nodeFaceInterpolation = _hmesh.NodeComputes.CreateNodeFaceInterpolationWorkspace();
		}
	}

	public override double MaximumTimestep(out int limitingCell)
	{
		EnsurePrepared();
		RefreshBoundaryLookup(_bcLookup, _bcFaceSlot);
		RefreshFaceStabilityCoefficients();
		int cellCount = _mesh.CellCount;
		double[] dtCell = _dtCell;
		_threadFor.Run(cellCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				dtCell[i] = CellStabilityLimit(i);
			}
		});
		double positiveInfinity = double.PositiveInfinity;
		limitingCell = -1;
		for (int limitingCell2 = 0; limitingCell2 < cellCount; limitingCell2++)
		{
			if (dtCell[limitingCell2] < positiveInfinity)
			{
				positiveInfinity = dtCell[limitingCell2];
				limitingCell = limitingCell2;
			}
		}
		return positiveInfinity;
	}

	private double CellStabilityLimit(int c)
	{
		if (!_flow.IsCellActive(c))
		{
			return double.PositiveInfinity;
		}
		int cellNeighborOffsetStart = _connectivity.CellNeighborOffsets[c];
		int cellNeighborOffsetEnd = _connectivity.CellNeighborOffsets[c + 1];
		double computed = 0.0;
		double computed2 = 0.0;
		for (int i = cellNeighborOffsetStart; i < cellNeighborOffsetEnd; i++)
		{
			int cellNeighborFaceIndex = _connectivity.CellNeighborFaces[i];
			if (cellNeighborFaceIndex < 0 || !_flow.IsFaceActive(cellNeighborFaceIndex))
			{
				continue;
			}
			int faceCellA = _connectivity.FaceCellA[cellNeighborFaceIndex];
			int faceCellB = _connectivity.FaceCellB[cellNeighborFaceIndex];
			int faceCellB2 = ((faceCellA == c) ? faceCellB : faceCellA);
			if (faceCellB2 >= 0 && !_flow.IsCellActive(faceCellB2))
			{
				continue;
			}
			int cellFaceOrientation = _connectivity.CellFaceOrientations[i];
			double maxFaceAdvectionAlpha = _maxFaceAdvectionAlpha[cellNeighborFaceIndex];
			double cellFaceOrientationFaceFlowOldTerm = (double)cellFaceOrientation * _flow.FaceFlowOld[cellNeighborFaceIndex] * maxFaceAdvectionAlpha;
			double cellFaceOrientationFaceFlowNewTerm = (double)cellFaceOrientation * _flow.FaceFlowNew[cellNeighborFaceIndex] * maxFaceAdvectionAlpha;
			if (cellFaceOrientationFaceFlowOldTerm > 0.0)
			{
				computed += cellFaceOrientationFaceFlowOldTerm;
			}
			if (cellFaceOrientationFaceFlowNewTerm > 0.0)
			{
				computed2 += cellFaceOrientationFaceFlowNewTerm;
			}
			double maxFaceDiffusivity = _maxFaceDiffusivity[cellNeighborFaceIndex];
			if (maxFaceDiffusivity <= 0.0)
			{
				continue;
			}
			if (faceCellB >= 0)
			{
				double gPerp = Math.Max(_gPerp[cellNeighborFaceIndex], 0.0);
				computed += maxFaceDiffusivity * _flow.FaceAreaOld[cellNeighborFaceIndex] * gPerp;
				computed2 += maxFaceDiffusivity * _flow.FaceAreaNew[cellNeighborFaceIndex] * gPerp;
				continue;
			}
			TransportBCType transportBCType = _bcLookup[cellNeighborFaceIndex]?.Type ?? TransportBCType.ZeroGradientOutflow;
			bool faceFlowOldIsNegativeOrFaceFlowOldIsNegative = transportBCType == TransportBCType.DirichletInflow || (transportBCType == TransportBCType.EquilibriumInflow && _flow.FaceFlowOld[cellNeighborFaceIndex] < 0.0) || (transportBCType == TransportBCType.ZeroGradientOutflow && _flow.FaceFlowOld[cellNeighborFaceIndex] < -1E-08);
			bool faceFlowNewIsNegativeOrFaceFlowNewIsNegative = transportBCType == TransportBCType.DirichletInflow || (transportBCType == TransportBCType.EquilibriumInflow && _flow.FaceFlowNew[cellNeighborFaceIndex] < 0.0) || (transportBCType == TransportBCType.ZeroGradientOutflow && _flow.FaceFlowNew[cellNeighborFaceIndex] < -1E-08);
			if (faceFlowOldIsNegativeOrFaceFlowOldIsNegative)
			{
				computed += maxFaceDiffusivity * _flow.FaceAreaOld[cellNeighborFaceIndex] * BoundaryPerpInverseDistance(cellNeighborFaceIndex);
			}
			if (faceFlowNewIsNegativeOrFaceFlowNewIsNegative)
			{
				computed2 += maxFaceDiffusivity * _flow.FaceAreaNew[cellNeighborFaceIndex] * BoundaryPerpInverseDistance(cellNeighborFaceIndex);
			}
		}
		double advectiveOutflow = _flow.CellWaterSources.GetAdvectiveOutflow(c);
		computed += advectiveOutflow;
		computed2 += advectiveOutflow;
		double maxTemporalBetaAt = MaxTemporalBetaAt(c);
		double levelStabilityLimit = LevelStabilityLimit(_flow.CellVolumeOld[c] / maxTemporalBetaAt, computed);
		double levelStabilityLimit2 = LevelStabilityLimit(_flow.CellVolumeNew[c] / maxTemporalBetaAt, computed2);
		return _control.ExplicitStabilitySafety * Math.Min(levelStabilityLimit, levelStabilityLimit2);
	}

	private static double LevelStabilityLimit(double storedVolume, double removalRate)
	{
		if (removalRate <= 0.0)
		{
			return double.PositiveInfinity;
		}
		if (storedVolume <= 0.0)
		{
			return 0.0;
		}
		return storedVolume / removalRate;
	}

	protected override void ValidateDerivedConfiguration()
	{
		if (!double.IsFinite(_control.ExplicitStabilitySafety) || _control.ExplicitStabilitySafety <= 0.0)
		{
			throw new ArgumentOutOfRangeException("ExplicitStabilitySafety");
		}
		if (_control.MaxSubStepsPerStep <= 0)
		{
			throw new ArgumentOutOfRangeException("MaxSubStepsPerStep");
		}
	}

	private void RefreshFaceStabilityCoefficients()
	{
		int faceCount = _mesh.FaceCount;
		for (int i = 0; i < faceCount; i++)
		{
			double maxFaceAdvectionAlpha = 0.0;
			double maxFaceDiffusivity = 0.0;
			for (int j = 0; j < _constituentCount; j++)
			{
				maxFaceAdvectionAlpha = Math.Max(maxFaceAdvectionAlpha, AdvectionAlphaAt(j, i));
				maxFaceDiffusivity = Math.Max(maxFaceDiffusivity, DiffusivityAt(j, i));
			}
			_maxFaceAdvectionAlpha[i] = maxFaceAdvectionAlpha;
			_maxFaceDiffusivity[i] = maxFaceDiffusivity;
		}
	}

	protected override ITransport.Status SolveStepInternal(double absTime, double dt, out int failureCell)
	{
		failureCell = -1;
		int constituentCount = _constituentCount;
		int nCells = _mesh.CellCount;
		double maximumTimestep = MaximumTimestep(out var limitingCell);
		if (maximumTimestep <= 0.0)
		{
			failureCell = limitingCell;
			return ITransport.Status.ERROR;
		}
		int lastSubStepCount = 1;
		if (double.IsFinite(maximumTimestep) && maximumTimestep > 0.0 && dt > maximumTimestep)
		{
			lastSubStepCount = (int)Math.Ceiling(dt / maximumTimestep);
		}
		if (lastSubStepCount > _control.MaxSubStepsPerStep)
		{
			failureCell = limitingCell;
			return ITransport.Status.ERROR;
		}
		LastSubStepCount = lastSubStepCount;
		double dtSub = dt / (double)lastSubStepCount;
		bool lastSubStepCountTerm = lastSubStepCount == 1;
		if (!lastSubStepCountTerm)
		{
			EnsureSubStepBuffers();
		}
		for (int i = 0; i < lastSubStepCount; i++)
		{
			double tau0 = (double)i / (double)lastSubStepCount;
			double tau1 = (double)(i + 1) / (double)lastSubStepCount;
			SelectSubStepLevels(lastSubStepCountTerm, tau0, tau1);
			if (!SeedSubStep(i, dtSub, out failureCell))
			{
				return ITransport.Status.ERROR;
			}
			if (!TryComputeResidual(_cStart, _volStart, _subFlow0, _subArea0, tau0, _rPredictor, out failureCell))
			{
				return ITransport.Status.ERROR;
			}
			long timestamp = Stopwatch.GetTimestamp();
			_threadFor.Run(constituentCount * nCells, (int start, int stop) =>
			{
				for (int j = start; j <= stop; j++)
				{
					int nCellsTerm = j / nCells;
					int nCellsTerm2 = j - nCellsTerm * nCells;
					_cStage[nCellsTerm][nCellsTerm2] = (_flow.IsCellActive(nCellsTerm2) ? StageCombine(nCellsTerm, nCellsTerm2, dtSub, _rPredictor[nCellsTerm][nCellsTerm2], 0.0, tau0, tau1, predictor: true) : _cStart[nCellsTerm][nCellsTerm2]);
				}
			});
			AddPhase(TransportPhase.Update, timestamp);
			if (!TryComputeResidual(_cStage, _volPred, _subFlow1, _subArea1, tau1, _rCorrector, out failureCell))
			{
				return ITransport.Status.ERROR;
			}
			timestamp = Stopwatch.GetTimestamp();
			_threadFor.Run(constituentCount * nCells, (int start, int stop) =>
			{
				for (int j = start; j <= stop; j++)
				{
					int nCellsTerm3 = j / nCells;
					int nCellsTerm4 = j - nCellsTerm3 * nCells;
					_state.CellConcentration[nCellsTerm3][nCellsTerm4] = (_flow.IsCellActive(nCellsTerm4) ? StageCombine(nCellsTerm3, nCellsTerm4, dtSub, _rPredictor[nCellsTerm3][nCellsTerm4], _rCorrector[nCellsTerm3][nCellsTerm4], tau0, tau1, predictor: false) : _cStart[nCellsTerm3][nCellsTerm4]);
				}
			});
			AddPhase(TransportPhase.Update, timestamp);
		}
		return ClampAndValidateStateRanges(_flow.CellActive, out failureCell);
	}

	private void AddPhase(TransportPhase phase, long startTimestamp)
	{
		PhaseSeconds[(int)phase] += Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
	}

	private void EnsureSubStepBuffers()
	{
		if (_lerpFlow0 == null)
		{
			int faceCount = _mesh.FaceCount;
			int cellCount = _mesh.CellCount;
			_lerpFlow0 = new double[faceCount];
			_lerpFlow1 = new double[faceCount];
			_lerpArea0 = new double[faceCount];
			_lerpArea1 = new double[faceCount];
			_lerpVol0 = new double[cellCount];
			_lerpVol1 = new double[cellCount];
			_lerpBeta0 = new double[_constituentCount][];
			_lerpBeta1 = new double[_constituentCount][];
		}
	}

	private void SelectSubStepLevels(bool single, double tau0, double tau1)
	{
		if (single)
		{
			_subFlow0 = _flow.FaceFlowOld;
			_subFlow1 = _flow.FaceFlowNew;
			_subArea0 = _flow.FaceAreaOld;
			_subArea1 = _flow.FaceAreaNew;
			_subVol0 = _flow.CellVolumeOld;
			_subVol1 = _flow.CellVolumeNew;
			for (int i = 0; i < _constituentCount; i++)
			{
				_subBeta0[i] = _cellTemporalBetaOld[i];
				_subBeta1[i] = _cellTemporalBetaNew[i];
			}
			return;
		}
		_subFlow0 = _lerpFlow0;
		_subFlow1 = _lerpFlow1;
		_subArea0 = _lerpArea0;
		_subArea1 = _lerpArea1;
		_subVol0 = _lerpVol0;
		_subVol1 = _lerpVol1;
		int faceCount = _mesh.FaceCount;
		_threadFor.Run(faceCount, (int start, int stop) =>
		{
			for (int j = start; j <= stop; j++)
			{
				_lerpFlow0[j] = Lerp(_flow.FaceFlowOld[j], _flow.FaceFlowNew[j], tau0);
				_lerpFlow1[j] = Lerp(_flow.FaceFlowOld[j], _flow.FaceFlowNew[j], tau1);
				_lerpArea0[j] = Lerp(_flow.FaceAreaOld[j], _flow.FaceAreaNew[j], tau0);
				_lerpArea1[j] = Lerp(_flow.FaceAreaOld[j], _flow.FaceAreaNew[j], tau1);
			}
		});
		int cellCount = _mesh.CellCount;
		_threadFor.Run(cellCount, (int start, int stop) =>
		{
			for (int j = start; j <= stop; j++)
			{
				_lerpVol0[j] = Lerp(_flow.CellVolumeOld[j], _flow.CellVolumeNew[j], tau0);
				_lerpVol1[j] = Lerp(_flow.CellVolumeOld[j], _flow.CellVolumeNew[j], tau1);
			}
		});
		for (int intervalIndex = 0; intervalIndex < _constituentCount; intervalIndex++)
		{
			double[] cellTemporalBetaOld = _cellTemporalBetaOld[intervalIndex];
			double[] cellTemporalBetaNew = _cellTemporalBetaNew[intervalIndex];
			if (cellTemporalBetaOld == null || cellTemporalBetaNew == null)
			{
				_subBeta0[intervalIndex] = null;
				_subBeta1[intervalIndex] = null;
				continue;
			}
			double[][] lerpBeta = _lerpBeta0;
			int ordinal = intervalIndex;
			double[] lerpBetaArray = lerpBeta[ordinal] ?? (lerpBeta[ordinal] = new double[cellCount]);
			lerpBeta = _lerpBeta1;
			ordinal = intervalIndex;
			double[] lerpBeta2 = lerpBeta[ordinal] ?? (lerpBeta[ordinal] = new double[cellCount]);
			for (int intervalIndex2 = 0; intervalIndex2 < cellCount; intervalIndex2++)
			{
				lerpBetaArray[intervalIndex2] = Lerp(cellTemporalBetaOld[intervalIndex2], cellTemporalBetaNew[intervalIndex2], tau0);
				lerpBeta2[intervalIndex2] = Lerp(cellTemporalBetaOld[intervalIndex2], cellTemporalBetaNew[intervalIndex2], tau1);
			}
			_subBeta0[intervalIndex] = lerpBetaArray;
			_subBeta1[intervalIndex] = lerpBeta2;
		}
	}

	private static double Lerp(double a, double b, double t)
	{
		return (1.0 - t) * a + t * b;
	}

	private bool SeedSubStep(int m, double dtSub, out int failureCell)
	{
		failureCell = -1;
		int cellCount = _mesh.CellCount;
		for (int i = 0; i < _constituentCount; i++)
		{
			Array.Copy((m == 0) ? _oldState.CellConcentration[i] : _state.CellConcentration[i], _cStart[i], cellCount);
		}
		if (!_control.DeriveVolumeChangeFromFlows)
		{
			Array.Copy(_subVol0, _volStart, cellCount);
			Array.Copy(_subVol1, _volPred, cellCount);
			Array.Copy(_subVol1, _volEnd, cellCount);
			return true;
		}
		if (m == 0)
		{
			Array.Copy(_flow.CellVolumeOld, _volStart, cellCount);
		}
		else
		{
			Array.Copy(_volEnd, _volStart, cellCount);
		}
		double[] flow0 = _subFlow0;
		double[] flow1 = _subFlow1;
		_threadFor.Run(cellCount, (int start, int stop) =>
		{
			int[] cellNeighborOffsets = _connectivity.CellNeighborOffsets;
			int[] cellNeighborFaces = _connectivity.CellNeighborFaces;
			int[] cellFaceOrientations = _connectivity.CellFaceOrientations;
			for (int j = start; j <= stop; j++)
			{
				double volStart = _volStart[j];
				if (!_flow.IsCellActive(j))
				{
					_volPred[j] = volStart;
					_volEnd[j] = volStart;
				}
				else
				{
					int cellNeighborOffsetStart = cellNeighborOffsets[j];
					int cellNeighborOffsetEnd = cellNeighborOffsets[j + 1];
					double computed = 0.0;
					double computed2 = 0.0;
					for (int k = cellNeighborOffsetStart; k < cellNeighborOffsetEnd; k++)
					{
						int cellNeighborFaceIndex = cellNeighborFaces[k];
						if (cellNeighborFaceIndex >= 0 && IsFaceActiveForCell(cellNeighborFaceIndex, j))
						{
							int cellFaceOrientation = cellFaceOrientations[k];
							computed += (double)cellFaceOrientation * flow0[cellNeighborFaceIndex];
							computed2 += (double)cellFaceOrientation * flow1[cellNeighborFaceIndex];
						}
					}
					double netFlow = _flow.CellWaterSources.GetNetFlow(j);
					_volPred[j] = volStart - dtSub * computed + dtSub * netFlow;
					_volEnd[j] = volStart - dtSub * 0.5 * (computed + computed2) + dtSub * netFlow;
				}
			}
		});
		for (int failureCell2 = 0; failureCell2 < cellCount; failureCell2++)
		{
			if (_flow.IsCellActive(failureCell2))
			{
				if (!double.IsFinite(_volPred[failureCell2]) || !double.IsFinite(_volEnd[failureCell2]))
				{
					failureCell = failureCell2;
					return false;
				}
				if (!TryNormalizeDerivedVolume(failureCell2, ref _volPred[failureCell2]) || !TryNormalizeDerivedVolume(failureCell2, ref _volEnd[failureCell2]))
				{
					failureCell = failureCell2;
					return false;
				}
			}
		}
		return true;
	}

	private double StageCombine(int k, int i, double dtSub, double r1, double r2, double tau0, double tau1, bool predictor)
	{
		double[] subBeta0 = _subBeta0[k];
		double[] subBeta1 = _subBeta1[k];
		double[] volPred = (predictor ? _volPred : _volEnd);
		double volStartSubBeta0Term = _volStart[i] / ((subBeta0 != null) ? subBeta0[i] : 1.0);
		double volPredSubBeta1Term = volPred[i] / ((subBeta1 != null) ? subBeta1[i] : 1.0);
		double cStart = _cStart[k][i];
		double cStartTerm = volStartSubBeta0Term * cStart;
		double reactionCoefficientAt = ReactionCoefficientAt(_cellSourceMassRateOld[k], _cellSourceMassRateNew[k], i, tau0);
		double reactionCoefficientAt2 = ReactionCoefficientAt(_cellSourceMassRateOld[k], _cellSourceMassRateNew[k], i, tau1);
		double reactionCoefficientAt3 = ReactionCoefficientAt(_cellSinkCoeffOld[k], _cellSinkCoeffNew[k], i, tau0);
		double reactionCoefficientAt4 = ReactionCoefficientAt(_cellSinkCoeffOld[k], _cellSinkCoeffNew[k], i, tau1);
		double r12 = (predictor ? r1 : (0.5 * (r1 + r2)));
		cStartTerm += dtSub * r12;
		cStartTerm += dtSub * (predictor ? reactionCoefficientAt : (0.5 * (reactionCoefficientAt + reactionCoefficientAt2)));
		double predictorExplicitTrapezoidalSinkTerm = ((!predictor && _control.ExplicitTrapezoidalSink) ? 0.5 : 0.0);
		if (predictorExplicitTrapezoidalSinkTerm > 0.0)
		{
			cStartTerm += dtSub * predictorExplicitTrapezoidalSinkTerm * reactionCoefficientAt3 * cStart;
		}
		double reactionCoefficientAt5 = (predictor ? reactionCoefficientAt3 : reactionCoefficientAt4);
		double dtSubReactionCoefficientAt5Term = volPredSubBeta1Term - dtSub * (1.0 - predictorExplicitTrapezoidalSinkTerm) * reactionCoefficientAt5;
		if (!(dtSubReactionCoefficientAt5Term > 0.0))
		{
			return cStart;
		}
		return cStartTerm / dtSubReactionCoefficientAt5Term;
	}

	private static double ReactionCoefficientAt(double[] oldLevel, double[] newLevel, int cell, double fraction)
	{
		double a = ((oldLevel != null) ? oldLevel[cell] : 0.0);
		double b = ((newLevel != null) ? newLevel[cell] : 0.0);
		return Lerp(a, b, fraction);
	}

	private bool TryComputeResidual(double[][] conc, double[] cellVolume, double[] faceFlow, double[] faceArea, double reactionTimeFraction, double[][] residual, out int failureCell)
	{
		failureCell = -1;
		int constituentCount = _constituentCount;
		int nCells = _mesh.CellCount;
		int nFaces = _mesh.FaceCount;
		int nNodes = _mesh.NodeCount;
		bool useTVD = _control.advectionScheme == AdvectionScheme.TVD;
		if (useTVD && _control.TVDGradientRatioMethod == TVDGradientRatioMethod.UpstreamVirtualPoint)
		{
			if (_TVDVirtualPointInterpolation == null)
			{
				_TVDVirtualPointInterpolation = _hmesh.CreateTVDVirtualPointInterpolationWorkspace();
			}
			if (!_TVDVirtualPointInterpolation.IsPrepared)
			{
				_TVDVirtualPointInterpolation.Prepare(cellVolume, faceArea, _flow.CellActive, _flow.FaceActive);
			}
			else if (!_TVDVirtualPointInterpolation.WetStatusMatches(cellVolume, faceArea, _flow.CellActive, _flow.FaceActive))
			{
				if (_TVDVirtualPointInterpolationAlternate == null)
				{
					_TVDVirtualPointInterpolationAlternate = _hmesh.CreateTVDVirtualPointInterpolationWorkspace();
				}
				if (!_TVDVirtualPointInterpolationAlternate.WetStatusMatches(cellVolume, faceArea, _flow.CellActive, _flow.FaceActive))
				{
					_TVDVirtualPointInterpolationAlternate.Prepare(cellVolume, faceArea, _flow.CellActive, _flow.FaceActive);
				}
				HydraulicMeshComputer.TVDVirtualPointInterpolationWorkspace tVDVirtualPointInterpolation = _TVDVirtualPointInterpolation;
				_TVDVirtualPointInterpolation = _TVDVirtualPointInterpolationAlternate;
				_TVDVirtualPointInterpolationAlternate = tVDVirtualPointInterpolation;
			}
		}
		bool useTVD2 = useTVD && _control.TVDGradientRatioMethod == TVDGradientRatioMethod.CasulliFaceFlowWeighted;
		if (useTVD2)
		{
			EnsureTVDCasulliBuffers();
			_TVDCasulliInterpolation.Prepare(cellVolume, faceFlow, faceArea, _flow.CellActive, _flow.FaceActive);
		}
		bool condition = false;
		for (int i = 0; i < constituentCount; i++)
		{
			condition |= NeedGradient(i, useTVD);
			if (!TryConfigureStageBoundaryReconstruction(i, faceFlow, reactionTimeFraction, out failureCell))
			{
				return false;
			}
		}
		long timestamp = Stopwatch.GetTimestamp();
		if (condition)
		{
			if (_gradientWorkspace == null)
			{
				_gradientWorkspace = _hmesh.CreateScalarGradientWlsWorkspace();
			}
			_threadFor.Run(nCells, (int start, int stop) =>
			{
				ReadOnlySpan<double> cellVolumes = cellVolume;
				ReadOnlySpan<double> faceAreas = faceArea;
				for (int j = start; j <= stop; j++)
				{
					_gradientWorkspace.PrepareCell(j, cellVolumes, faceAreas, _flow.CellActive, _flow.FaceActive);
				}
			});
			_threadFor.Run(constituentCount * nCells, (int start, int stop) =>
			{
				int ordinal = -1;
				double[] concArray = null;
				Vector[] gradArray = null;
				double[] cellGradientLimiter = null;
				bool needGradient = false;
				bool needsDiffusionGradient = false;
				for (int j = start; j <= stop; j++)
				{
					int nCellsTerm = j / nCells;
					if (nCellsTerm != ordinal)
					{
						ordinal = nCellsTerm;
						concArray = conc[ordinal];
						gradArray = _grad[ordinal];
						needGradient = NeedGradient(ordinal, useTVD);
						needsDiffusionGradient = NeedsDiffusionGradient(ordinal);
						cellGradientLimiter = _cellGradientLimiter[ordinal];
					}
					if (needGradient)
					{
						int nCellsTerm2 = j - ordinal * nCells;
						Vector gradient = (gradArray[nCellsTerm2] = _gradientWorkspace.ScalarGradient(nCellsTerm2, concArray));
						if (needsDiffusionGradient)
						{
							cellGradientLimiter[nCellsTerm2] = _gradientWorkspace.ScalarGradientLimiter(nCellsTerm2, gradient, concArray, _control.multidimensionalSlopeLimiter);
						}
					}
				}
			});
			for (int intervalIndex = 0; intervalIndex < constituentCount; intervalIndex++)
			{
				if (NeedGradient(intervalIndex, useTVD))
				{
					if (NeedsDiffusionGradient(intervalIndex))
					{
						_boundaryReconstruction[intervalIndex].ApplyCellGradientAndLimiterOverrides(_gradientWorkspace, conc[intervalIndex], cellVolume, _grad[intervalIndex], _cellGradientLimiter[intervalIndex], _control.multidimensionalSlopeLimiter);
					}
					else
					{
						_boundaryReconstruction[intervalIndex].ApplyCellGradientOverrides(_gradientWorkspace, conc[intervalIndex], cellVolume, _grad[intervalIndex], HydraulicMeshComputer.MultidimensionalSlopeLimiter.None);
					}
				}
			}
		}
		if (useTVD2)
		{
			_threadFor.Run(constituentCount * nCells, (int start, int stop) =>
			{
				for (int j = start; j <= stop; j++)
				{
					int nCellsTerm3 = j / nCells;
					int nCellsTerm4 = j - nCellsTerm3 * nCells;
					_TVDCasulliUpstreamValues[nCellsTerm3][nCellsTerm4] = (_TVDCasulliInterpolation.TryInterpolateUpstreamValueUnchecked(nCellsTerm4, conc[nCellsTerm3], _boundaryReconstruction[nCellsTerm3], out var upstreamValue) ? upstreamValue : double.NaN);
				}
			});
		}
		if (_control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased)
		{
			_nodeFaceInterpolation.Prepare(faceArea);
			_threadFor.Run(constituentCount * nNodes, (int start, int stop) =>
			{
				for (int j = start; j <= stop; j++)
				{
					int nNodesTerm = j / nNodes;
					if (NeedsDiffusionNodeValues(nNodesTerm))
					{
						int nodeIndex = j - nNodesTerm * nNodes;
						_hmesh.NodeComputes.InterpolateNodeFaceValues(nodeIndex, _nodeFaceInterpolation, conc[nNodesTerm], _nodeFaceValues[nNodesTerm]);
					}
				}
			});
			for (int intervalIndex2 = 0; intervalIndex2 < constituentCount; intervalIndex2++)
			{
				if (NeedsDiffusionNodeValues(intervalIndex2))
				{
					_boundaryReconstruction[intervalIndex2].ApplyNodeFaceValueOverrides(conc[intervalIndex2], cellVolume, _nodeFaceInterpolation, _nodeFaceValues[intervalIndex2]);
				}
			}
		}
		AddPhase(TransportPhase.Gradients, timestamp);
		timestamp = Stopwatch.GetTimestamp();
		_threadFor.Run(constituentCount * nFaces, (int start, int stop) =>
		{
			int[] faceCellB = _connectivity.FaceCellB;
			int ordinal2 = -1;
			double[] c = null;
			Vector[] grad = null;
			double[] gradientLimiters = null;
			double[] faceFlux = null;
			for (int j = start; j <= stop; j++)
			{
				int nFacesTerm = j / nFaces;
				if (nFacesTerm != ordinal2)
				{
					ordinal2 = nFacesTerm;
					c = conc[ordinal2];
					grad = _grad[ordinal2];
					gradientLimiters = _cellGradientLimiter[ordinal2];
					faceFlux = _faceFlux[ordinal2];
				}
				int nFacesTerm2 = j - ordinal2 * nFaces;
				int cIdx = _connectivity.FaceCellA[nFacesTerm2];
				int faceCellB2 = faceCellB[nFacesTerm2];
				faceFlux[nFacesTerm2] = ((faceCellB2 < 0 || !_flow.IsFaceActive(nFacesTerm2) || !_flow.IsCellActive(cIdx) || !_flow.IsCellActive(faceCellB2)) ? 0.0 : InteriorFaceMass(ordinal2, nFacesTerm2, c, grad, gradientLimiters, _nodeFaceValues[ordinal2], faceFlow[nFacesTerm2], faceArea[nFacesTerm2], useTVD));
			}
		});
		AddPhase(TransportPhase.Assembly, timestamp);
		timestamp = Stopwatch.GetTimestamp();
		int nb = _boundaryFaces.Length;
		int boundaryFailureCell = nCells;
		if (nb > 0)
		{
			_threadFor.Run(constituentCount * nb, (int start, int stop) =>
			{
				for (int j = start; j <= stop; j++)
				{
					int nbTerm = j / nb;
					int nbTerm2 = j - nbTerm * nb;
					int boundaryFaceIndex = _boundaryFaces[nbTerm2];
					int cIdx = _connectivity.FaceCellA[boundaryFaceIndex];
					if (!_flow.IsFaceActive(boundaryFaceIndex) || !_flow.IsCellActive(cIdx))
					{
						_faceFlux[nbTerm][boundaryFaceIndex] = 0.0;
					}
					else if (!TryBoundaryFaceMass(nbTerm, boundaryFaceIndex, conc[nbTerm], _grad[nbTerm], _cellGradientLimiter[nbTerm], _nodeFaceValues[nbTerm], faceFlow[boundaryFaceIndex], faceArea[boundaryFaceIndex], reactionTimeFraction, out _faceFlux[nbTerm][boundaryFaceIndex]))
					{
						TransportSolver.SetMinimum(ref boundaryFailureCell, _connectivity.FaceCellA[boundaryFaceIndex]);
					}
				}
			});
		}
		AddPhase(TransportPhase.Boundary, timestamp);
		if (boundaryFailureCell < nCells)
		{
			failureCell = boundaryFailureCell;
			return false;
		}
		timestamp = Stopwatch.GetTimestamp();
		_threadFor.Run(constituentCount * nCells, (int start, int stop) =>
		{
			int[] cellNeighborOffsets = _connectivity.CellNeighborOffsets;
			int[] cellNeighborFaces = _connectivity.CellNeighborFaces;
			int[] cellFaceOrientations = _connectivity.CellFaceOrientations;
			int ordinal3 = -1;
			double[] faceFluxArray = null;
			double[] residualArray = null;
			for (int j = start; j <= stop; j++)
			{
				int nCellsTerm5 = j / nCells;
				if (nCellsTerm5 != ordinal3)
				{
					ordinal3 = nCellsTerm5;
					faceFluxArray = _faceFlux[ordinal3];
					residualArray = residual[ordinal3];
				}
				int nCellsTerm6 = j - ordinal3 * nCells;
				if (!_flow.IsCellActive(nCellsTerm6))
				{
					residualArray[nCellsTerm6] = 0.0;
				}
				else
				{
					double residualArray2 = 0.0;
					int cellNeighborOffsetStart = cellNeighborOffsets[nCellsTerm6];
					int cellNeighborOffsetEnd = cellNeighborOffsets[nCellsTerm6 + 1];
					for (int k = cellNeighborOffsetStart; k < cellNeighborOffsetEnd; k++)
					{
						int cellNeighborFaceIndex = cellNeighborFaces[k];
						if (cellNeighborFaceIndex >= 0)
						{
							residualArray2 -= (double)cellFaceOrientations[k] * faceFluxArray[cellNeighborFaceIndex];
						}
					}
					CellWaterSourceSnapshot cellWaterSources = _flow.CellWaterSources;
					residualArray2 -= cellWaterSources.GetAdvectiveOutflow(nCellsTerm6) * conc[ordinal3][nCellsTerm6];
					ReadOnlySpan<CellWaterExternalInflow> externalInflows = cellWaterSources.ExternalInflows;
					ReadOnlySpan<int> incomingExternalIndices = cellWaterSources.GetIncomingExternalIndices(nCellsTerm6);
					for (int l = 0; l < incomingExternalIndices.Length; l++)
					{
						int index = incomingExternalIndices[l];
						CellWaterExternalInflow cellWaterExternalInflow = externalInflows[index];
						residualArray2 += cellWaterExternalInflow.Flow * _externalSourceConcentration[ordinal3][cellWaterExternalInflow.SourceId.Value];
					}
					ReadOnlySpan<CellWaterTransfer> transfers = cellWaterSources.Transfers;
					ReadOnlySpan<int> incomingTransferIndices = cellWaterSources.GetIncomingTransferIndices(nCellsTerm6);
					for (int l = 0; l < incomingTransferIndices.Length; l++)
					{
						int index2 = incomingTransferIndices[l];
						CellWaterTransfer cellWaterTransfer = transfers[index2];
						residualArray2 += cellWaterTransfer.Flow * conc[ordinal3][cellWaterTransfer.DonorCell];
					}
					residualArray[nCellsTerm6] = residualArray2;
				}
			}
		});
		AddPhase(TransportPhase.Update, timestamp);
		failureCell = -1;
		return true;
	}

	private bool TryConfigureStageBoundaryReconstruction(int constituent, double[] faceFlow, double reactionTimeFraction, out int failureCell)
	{
		HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace scalarBoundaryReconstructionWorkspace = _boundaryReconstruction[constituent];
		ConfigureBoundaryReconstruction(scalarBoundaryReconstructionWorkspace, constituent, _boundaryFaces, _bcLookup, _bcFaceSlot, faceFlow, reverseFlowUsesEquilibrium: false, oldLevel: false);
		int[] boundaryFaces = _boundaryFaces;
		foreach (int boundaryFaceIndex in boundaryFaces)
		{
			if ((_bcLookup[boundaryFaceIndex]?.Type ?? TransportBCType.ZeroGradientOutflow) == TransportBCType.ZeroGradientOutflow && !(faceFlow[boundaryFaceIndex] >= -1E-08))
			{
				int faceCellA = _connectivity.FaceCellA[boundaryFaceIndex];
				if (!TryStageReactionEquilibriumConcentration(constituent, faceCellA, reactionTimeFraction, out var concentration))
				{
					failureCell = faceCellA;
					return false;
				}
				scalarBoundaryReconstructionWorkspace.SetValue(boundaryFaceIndex, concentration);
			}
		}
		failureCell = -1;
		return true;
	}

	private bool NeedGradient(int k, bool useTVD)
	{
		if (!useTVD || _control.TVDGradientRatioMethod != TVDGradientRatioMethod.DarwishMoukalledCellGradient)
		{
			return NeedsDiffusionGradient(k);
		}
		return true;
	}

	private void EnsureTVDCasulliBuffers()
	{
		if (_TVDCasulliInterpolation == null)
		{
			_TVDCasulliInterpolation = new CasulliFaceFlowInterpolationWorkspace(_connectivity);
		}
		if (_TVDCasulliUpstreamValues == null)
		{
			_TVDCasulliUpstreamValues = new double[_constituentCount][];
			for (int i = 0; i < _constituentCount; i++)
			{
				_TVDCasulliUpstreamValues[i] = new double[_mesh.CellCount];
			}
		}
	}

	private double InteriorFaceMass(int k, int f, double[] C, Vector[] grad, double[] gradientLimiters, double[] nodeFaceValues, double faceFlow, double faceArea, bool useTVD)
	{
		return AdvectiveFaceMass(k, f, C, grad, faceFlow, useTVD) + DiffusiveFaceMass(f, C, grad, gradientLimiters, nodeFaceValues, faceArea, DiffusivityAt(k, f));
	}

	private double AdvectiveFaceMass(int k, int f, double[] C, Vector[] grad, double faceFlow, bool useTVD)
	{
		double advectionAlphaAt = AdvectionAlphaAt(k, f);
		if (faceFlow == 0.0 || advectionAlphaAt == 0.0)
		{
			return 0.0;
		}
		int faceCellA = _connectivity.FaceCellA[f];
		int faceCellB = _connectivity.FaceCellB[f];
		bool faceFlowTerm = faceFlow > 0.0;
		int faceCellA2 = (faceFlowTerm ? faceCellA : faceCellB);
		double computeTVDDeferredCorrectionTerm = C[faceCellA2];
		if (useTVD)
		{
			double computed = computeTVDDeferredCorrectionTerm;
			bool cellAIsUpwind = faceFlowTerm;
			HydraulicMeshComputer.TVDVirtualPointInterpolationWorkspace tVDVirtualPointInterpolation = _TVDVirtualPointInterpolation;
			CasulliFaceFlowInterpolationWorkspace tVDCasulliInterpolation = _TVDCasulliInterpolation;
			double[][] tVDCasulliUpstreamValues = _TVDCasulliUpstreamValues;
			computeTVDDeferredCorrectionTerm = computed + ComputeTVDDeferredCorrection(f, cellAIsUpwind, C, grad, tVDVirtualPointInterpolation, tVDCasulliInterpolation, (tVDCasulliUpstreamValues != null) ? tVDCasulliUpstreamValues[k] : null);
		}
		return advectionAlphaAt * faceFlow * computeTVDDeferredCorrectionTerm;
	}

	private double DiffusiveFaceMass(int f, double[] C, Vector[] grad, double[] gradientLimiters, double[] nodeFaceValues, double faceArea, double diffusivity)
	{
		if (diffusivity <= 0.0)
		{
			return 0.0;
		}
		double diffusivityTimesFaceArea = diffusivity * faceArea;
		int faceCellA = _connectivity.FaceCellA[f];
		int faceCellB = _connectivity.FaceCellB[f];
		if (_control.faceNormalGradientMethod == FaceNormalGradientMethod.TwoPoint)
		{
			return diffusivityTimesFaceArea * Math.Max(_gPerp[f], 0.0) * (C[faceCellA] - C[faceCellB]);
		}
		double faceNormalGradient = ComputeFaceNormalGradient(f, C, grad, gradientLimiters, nodeFaceValues, _nodeFaceInterpolation);
		return (0.0 - diffusivityTimesFaceArea) * faceNormalGradient;
	}

	private bool TryBoundaryFaceMass(int k, int f, double[] C, Vector[] gradients, double[] gradientLimiters, double[] nodeFaceValues, double faceFlow, double faceArea, double reactionTimeFraction, out double mass)
	{
		TransportBoundaryCondition transportBoundaryCondition = _bcLookup[f];
		TransportBCType transportBCType = transportBoundaryCondition?.Type ?? TransportBCType.ZeroGradientOutflow;
		if (transportBCType == TransportBCType.NoFluxWall)
		{
			mass = 0.0;
			return true;
		}
		int faceCellA = _connectivity.FaceCellA[f];
		double advectionAlphaAtFaceFlowTerm = AdvectionAlphaAt(k, f) * faceFlow;
		if (transportBCType == TransportBCType.ZeroGradientOutflow && faceFlow >= -1E-08)
		{
			mass = advectionAlphaAtFaceFlowTerm * C[faceCellA];
			return true;
		}
		double concentration;
		if (transportBCType == TransportBCType.ZeroGradientOutflow)
		{
			if (!TryStageReactionEquilibriumConcentration(k, faceCellA, reactionTimeFraction, out concentration))
			{
				mass = 0.0;
				return false;
			}
		}
		else
		{
			concentration = transportBoundaryCondition.ConcentrationAt(k, _bcFaceSlot[f]);
		}
		if (transportBCType == TransportBCType.PrescribedMassFlux)
		{
			mass = advectionAlphaAtFaceFlowTerm * concentration;
			return true;
		}
		bool faceFlowIsNegative = faceFlow < 0.0;
		mass = (faceFlowIsNegative ? (advectionAlphaAtFaceFlowTerm * concentration) : (advectionAlphaAtFaceFlowTerm * C[faceCellA]));
		if ((transportBCType == TransportBCType.DirichletInflow) | faceFlowIsNegative)
		{
			double diffusivityAt = DiffusivityAt(k, f);
			if (diffusivityAt > 0.0)
			{
				double faceNormalGradient = _boundaryReconstruction[k].FaceNormalGradient(f, _control.faceNormalGradientMethod, C, gradients, gradientLimiters, nodeFaceValues, _nodeFaceInterpolation, BoundaryPerpInverseDistance(f));
				mass -= diffusivityAt * faceArea * faceNormalGradient;
			}
		}
		return true;
	}

	private bool TryStageReactionEquilibriumConcentration(int k, int c, double reactionTimeFraction, out double concentration)
	{
		double[] cellSourceMassRateOld = _cellSourceMassRateOld[k];
		double[] cellSourceMassRateNew = _cellSourceMassRateNew[k];
		double[] cellSinkCoeffOld = _cellSinkCoeffOld[k];
		double[] cellSinkCoeffNew = _cellSinkCoeffNew[k];
		double a = ((cellSourceMassRateOld != null) ? cellSourceMassRateOld[c] : 0.0);
		double b = ((cellSourceMassRateNew != null) ? cellSourceMassRateNew[c] : 0.0);
		double a2 = ((cellSinkCoeffOld != null) ? cellSinkCoeffOld[c] : 0.0);
		double b2 = ((cellSinkCoeffNew != null) ? cellSinkCoeffNew[c] : 0.0);
		double sourceMassRate = Lerp(a, b, reactionTimeFraction);
		double sinkCoefficient = Lerp(a2, b2, reactionTimeFraction);
		return TryReactionEquilibriumConcentration(k, sourceMassRate, sinkCoefficient, out concentration);
	}
}
