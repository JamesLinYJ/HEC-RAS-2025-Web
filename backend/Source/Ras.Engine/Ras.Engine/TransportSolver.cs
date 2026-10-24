using System;
using System.Collections.Generic;
using System.Threading;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Ras.Engine.Engine;
using Utility;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Engine;

public abstract class TransportSolver : ITransport
{
	protected const double BoundaryFlowTolerance = 1E-08;

	private static readonly ILogger _logger = LogHelper.GetLogger("TransportSolver");

	protected readonly Mesh _mesh;

	protected readonly HydraulicMeshComputer _hmesh;

	protected MeshConnectivity _connectivity;

	protected readonly Vector[] _TVDFaceCellCenterDeltas;

	protected readonly TransportControl _control;

	protected TransportFlowField _flow;

	protected readonly TransportBoundaryConditionCollection _bcs;

	protected readonly SolverConvergence _convergence;

	protected readonly int _constituentCount;

	protected TransportState _state;

	protected TransportState _oldState;

	private bool _prepared;

	private bool _canResolve;

	private bool _checkpointRequested;

	private bool _checkpointAvailable;

	private double _lastStepTime;

	private double _lastStepDt;

	private double[] _boundaryPerpInverseDistances;

	private TransportBoundaryCondition[] _maximumTimestepBoundaryLookup;

	private int[] _maximumTimestepBoundaryFaceSlot;

	private readonly int[] _clampedDerivedVolumeCells;

	private int _clampedDerivedVolumeCount;

	private int _worstClampedDerivedVolumeCell = -1;

	private double _worstClampedDerivedVolume;

	protected readonly double[] _diffusivity;

	protected readonly double[][] _faceDiffusivity;

	protected readonly double[][] _faceAdvectionAlpha;

	protected readonly double[][] _cellTemporalBeta;

	protected readonly double[][] _cellTemporalBetaNew;

	protected readonly double[][] _cellTemporalBetaOld;

	protected readonly double[][] _cellSourceMassRate;

	protected readonly double[][] _cellSourceMassRateNew;

	protected readonly double[][] _cellSourceMassRateOld;

	protected readonly double[][] _cellSinkCoeff;

	protected readonly double[][] _cellSinkCoeffNew;

	protected readonly double[][] _cellSinkCoeffOld;

	protected double[][] _externalSourceConcentration;

	private readonly Dictionary<long, double> _externalSourceConcentrations = new Dictionary<long, double>();

	private bool _externalSourceConcentrationsDirty = true;

	protected ProgressReporter _reporter;

	protected ThreadFor _threadFor = new ThreadFor(1);

	public readonly double[] PhaseSeconds = new double[Enum.GetValues<TransportPhase>().Length];

	public int ThreadCount
	{
		get
		{
			if (_control.numCores <= 0)
			{
				return Environment.ProcessorCount;
			}
			return _control.numCores;
		}
	}

	public int ConstituentCount => _constituentCount;

	public TransportControl Control => _control;

	public double CurrentTime => _state.Time;

	public LoopConvergence.ConvStatus OuterConvergenceStatus => _convergence.OuterConv.Status;

	public TransportFlowField FlowField => _flow;

	public TransportBoundaryConditionCollection BoundaryConditions => _bcs;

	protected bool UsesCellGradientCorrection
	{
		get
		{
			if (_control.faceNormalGradientMethod != FaceNormalGradientMethod.OverRelaxation)
			{
				return _control.faceNormalGradientMethod == FaceNormalGradientMethod.VirtualNode;
			}
			return true;
		}
	}

	public void ClearPhaseTimers()
	{
		Array.Clear(PhaseSeconds, 0, PhaseSeconds.Length);
	}

	protected TransportSolver(Mesh mesh, TransportControl control, TransportFlowField flow, int constituentCount, TransportBoundaryConditionCollection bcs = null, SolverConvergence convergence = null)
	{
		ArgumentNullException.ThrowIfNull(mesh, "mesh");
		ArgumentNullException.ThrowIfNull(control, "control");
		ArgumentNullException.ThrowIfNull(flow, "flow");
		if (constituentCount <= 0)
		{
			throw new ArgumentOutOfRangeException("constituentCount");
		}
		if (flow.CellCount != mesh.CellCount || flow.FaceCount != mesh.FaceCount)
		{
			throw new ArgumentException("Flow-field dimensions must match the mesh.", "flow");
		}
		_mesh = mesh;
		_hmesh = mesh.MetadataCache;
		_TVDFaceCellCenterDeltas = new Vector[mesh.FaceCount];
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			Face face = mesh.Faces[i];
			if (face.cellB >= 0)
			{
				Point point = mesh.CellCenters[face.cellA];
				Point point2 = mesh.CellCenters[face.cellB];
				_TVDFaceCellCenterDeltas[i] = new Vector(point2.X - point.X, point2.Y - point.Y);
			}
		}
		_control = control;
		_control.EnsureConstituentRanges(constituentCount);
		_flow = flow;
		_constituentCount = constituentCount;
		_bcs = bcs ?? new TransportBoundaryConditionCollection();
		_convergence = convergence ?? new SolverConvergence();
		_state = new TransportState(constituentCount, mesh.CellCount);
		_oldState = new TransportState(constituentCount, mesh.CellCount);
		_diffusivity = new double[constituentCount];
		_faceDiffusivity = new double[constituentCount][];
		_faceAdvectionAlpha = new double[constituentCount][];
		_cellTemporalBeta = new double[constituentCount][];
		_cellTemporalBetaNew = new double[constituentCount][];
		_cellTemporalBetaOld = new double[constituentCount][];
		_cellSourceMassRate = new double[constituentCount][];
		_cellSourceMassRateNew = new double[constituentCount][];
		_cellSourceMassRateOld = new double[constituentCount][];
		_cellSinkCoeff = new double[constituentCount][];
		_cellSinkCoeffNew = new double[constituentCount][];
		_cellSinkCoeffOld = new double[constituentCount][];
		_clampedDerivedVolumeCells = new int[mesh.CellCount];
	}

	public Mesh GetMesh()
	{
		return _mesh;
	}

	public ReadOnlySpan<double> GetCellConcentration(int constituent)
	{
		ValidateConstituent(constituent);
		return _state.CellConcentration[constituent];
	}

	public void SetCellConcentration(int constituent, ReadOnlySpan<double> values)
	{
		ValidateConstituent(constituent);
		if (values.Length != _mesh.CellCount)
		{
			throw new ArgumentException("Concentration length must equal Mesh.CellCount.");
		}
		values.CopyTo(_state.CellConcentration[constituent]);
		values.CopyTo(_oldState.CellConcentration[constituent]);
	}

	public void SetExternalSourceConcentration(CellWaterSourceId source, int constituent, double concentration)
	{
		if ((uint)constituent >= (uint)_constituentCount)
		{
			throw new ArgumentOutOfRangeException("constituent");
		}
		IReadOnlyList<CellWaterSourceInfo> sources = _flow.CellWaterSources.Sources;
		if (!source.IsValid || source.Value >= sources.Count || sources[source.Value].Id != source)
		{
			throw new ArgumentOutOfRangeException("source");
		}
		if (!double.IsFinite(concentration))
		{
			throw new ArgumentOutOfRangeException("concentration");
		}
		_externalSourceConcentrations[ExternalSourceConcentrationKey(source, constituent)] = concentration;
		_externalSourceConcentrationsDirty = true;
	}

	public void ClampCellConcentration(int constituent, double minimum)
	{
		ValidateConstituent(constituent);
		if (!double.IsFinite(minimum))
		{
			throw new ArgumentOutOfRangeException("minimum");
		}
		double[] cellConcentration = _state.CellConcentration[constituent];
		for (int i = 0; i < cellConcentration.Length; i++)
		{
			if (cellConcentration[i] < minimum)
			{
				cellConcentration[i] = minimum;
			}
		}
	}

	public void SetDiffusivity(int constituent, double d)
	{
		ValidateConstituent(constituent);
		if (!double.IsFinite(d) || d < 0.0)
		{
			throw new ArgumentOutOfRangeException("d");
		}
		_diffusivity[constituent] = d;
	}

	public void SetDiffusivityAll(double d)
	{
		if (!double.IsFinite(d) || d < 0.0)
		{
			throw new ArgumentOutOfRangeException("d");
		}
		for (int i = 0; i < _constituentCount; i++)
		{
			_diffusivity[i] = d;
		}
	}

	public double GetDiffusivity(int constituent)
	{
		ValidateConstituent(constituent);
		return _diffusivity[constituent];
	}

	public void SetFaceDiffusivity(int constituent, double[] faceDiffusivity)
	{
		ValidateConstituent(constituent);
		ValidateOptionalArray(faceDiffusivity, _mesh.FaceCount, "faceDiffusivity", nonnegative: true);
		_faceDiffusivity[constituent] = faceDiffusivity;
	}

	public double[] GetFaceDiffusivity(int constituent)
	{
		ValidateConstituent(constituent);
		return _faceDiffusivity[constituent];
	}

	protected bool HasDiffusion(int k)
	{
		if (_faceDiffusivity[k] == null)
		{
			return _diffusivity[k] > 0.0;
		}
		return true;
	}

	protected double DiffusivityAt(int k, int f)
	{
		double[] faceDiffusivity = _faceDiffusivity[k];
		if (faceDiffusivity == null)
		{
			return _diffusivity[k];
		}
		return faceDiffusivity[f];
	}

	public void SetFaceAdvectionAlpha(int constituent, double[] faceAlpha)
	{
		ValidateConstituent(constituent);
		ValidateOptionalArray(faceAlpha, _mesh.FaceCount, "faceAlpha", nonnegative: true);
		_faceAdvectionAlpha[constituent] = faceAlpha;
	}

	public double[] GetFaceAdvectionAlpha(int constituent)
	{
		ValidateConstituent(constituent);
		return _faceAdvectionAlpha[constituent];
	}

	protected double AdvectionAlphaAt(int k, int f)
	{
		double[] faceAdvectionAlpha = _faceAdvectionAlpha[k];
		if (faceAdvectionAlpha == null)
		{
			return 1.0;
		}
		return faceAdvectionAlpha[f];
	}

	public void SetCellTemporalBeta(int constituent, double[] beta)
	{
		ValidateConstituent(constituent);
		if (beta != null)
		{
			if (beta.Length != _mesh.CellCount)
			{
				throw new ArgumentException("Temporal beta length must equal Mesh.CellCount.");
			}
			for (int i = 0; i < beta.Length; i++)
			{
				if (!(beta[i] > 0.0) || !(beta[i] <= 1.0))
				{
					throw new ArgumentException($"Temporal beta must lie in (0, 1]; cell {i} is {beta[i]}.");
				}
			}
		}
		bool cellTemporalBetaIsNull = _cellTemporalBeta[constituent] == null;
		_cellTemporalBeta[constituent] = beta;
		if (beta != null && cellTemporalBetaIsNull)
		{
			_cellTemporalBetaNew[constituent] = (double[])beta.Clone();
			_cellTemporalBetaOld[constituent] = (double[])beta.Clone();
		}
	}

	public double[] GetCellTemporalBeta(int constituent)
	{
		ValidateConstituent(constituent);
		return _cellTemporalBeta[constituent];
	}

	protected double MaxTemporalBetaAt(int i)
	{
		double cellTemporalBeta = 0.0;
		for (int j = 0; j < _constituentCount; j++)
		{
			double[] cellTemporalBetaArray = _cellTemporalBeta[j];
			double cellTemporalBeta2 = ((cellTemporalBetaArray != null) ? cellTemporalBetaArray[i] : 1.0);
			if (cellTemporalBeta2 > cellTemporalBeta)
			{
				cellTemporalBeta = cellTemporalBeta2;
			}
		}
		return cellTemporalBeta;
	}

	private void CaptureTemporalBeta(bool roll)
	{
		for (int i = 0; i < _constituentCount; i++)
		{
			if (roll)
			{
				double[] cellTemporalBetaOld = _cellTemporalBetaOld[i];
				_cellTemporalBetaOld[i] = _cellTemporalBetaNew[i];
				_cellTemporalBetaNew[i] = cellTemporalBetaOld;
			}
			double[] cellTemporalBetaNew = _cellTemporalBeta[i];
			if (cellTemporalBetaNew == null)
			{
				_cellTemporalBetaNew[i] = null;
				continue;
			}
			double[] cellTemporalBetaNewArray = _cellTemporalBetaNew[i];
			if (cellTemporalBetaNewArray == null || cellTemporalBetaNewArray.Length != cellTemporalBetaNew.Length)
			{
				cellTemporalBetaNewArray = (_cellTemporalBetaNew[i] = new double[cellTemporalBetaNew.Length]);
			}
			Array.Copy(cellTemporalBetaNew, cellTemporalBetaNewArray, cellTemporalBetaNew.Length);
		}
	}

	public void SetCellSourceMassRate(int constituent, double[] sourceMassRate)
	{
		ValidateConstituent(constituent);
		ValidateOptionalArray(sourceMassRate, _mesh.CellCount, "sourceMassRate", nonnegative: false);
		bool introducing = _cellSourceMassRate[constituent] == null;
		_cellSourceMassRate[constituent] = sourceMassRate;
		SeedLevelsOnIntroduction(sourceMassRate, constituent, introducing, _cellSourceMassRateNew, _cellSourceMassRateOld);
	}

	public void SetCellSinkCoefficient(int constituent, double[] sinkCoeff)
	{
		ValidateConstituent(constituent);
		ValidateOptionalArray(sinkCoeff, _mesh.CellCount, "sinkCoeff", nonnegative: false, nonpositive: true);
		bool introducing = _cellSinkCoeff[constituent] == null;
		_cellSinkCoeff[constituent] = sinkCoeff;
		SeedLevelsOnIntroduction(sinkCoeff, constituent, introducing, _cellSinkCoeffNew, _cellSinkCoeffOld);
	}

	private static void SeedLevelsOnIntroduction(double[] live, int constituent, bool introducing, double[][] newLevels, double[][] oldLevels)
	{
		if (live != null && introducing)
		{
			newLevels[constituent] = (double[])live.Clone();
			oldLevels[constituent] = (double[])live.Clone();
		}
	}

	public void SetFlowField(TransportFlowField flow)
	{
		ArgumentNullException.ThrowIfNull(flow, "flow");
		if (flow.CellCount != _mesh.CellCount || flow.FaceCount != _mesh.FaceCount)
		{
			throw new ArgumentException("Flow-field dimensions must match the mesh.", "flow");
		}
		if (flow.CellWaterSources == null || flow.CellWaterSources.CellCount != _mesh.CellCount)
		{
			throw new ArgumentException("Cell-water source dimensions must match the mesh.", "flow");
		}
		if (_flow.CellWaterSources != flow.CellWaterSources)
		{
			_externalSourceConcentrations.Clear();
		}
		_flow = flow;
		_externalSourceConcentrationsDirty = true;
	}

	public virtual void Prepare(ProgressReporter pr = null)
	{
		if (_prepared)
		{
			Cleanup();
		}
		ValidateConfiguration();
		_reporter = pr;
		_connectivity = new MeshConnectivity(_mesh);
		_convergence.Prepare(_control.OuterControl, _mesh.CellCount);
		_ = _hmesh.FaceNormal;
		_ = _hmesh.FaceInvPerpDistances;
		_ = _hmesh.CellAreas;
		_boundaryPerpInverseDistances = new double[_mesh.FaceCount];
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			if (_connectivity.FaceCellB[i] < 0)
			{
				int faceCellA = _connectivity.FaceCellA[i];
				Point point = _mesh.CellCenters[faceCellA];
				Point point2 = _mesh.FaceSegment(i).Midpoint();
				Vector vector = _hmesh.FaceNormal[i];
				double point2PointTerm = Math.Abs((point2.X - point.X) * vector.DeltaX + (point2.Y - point.Y) * vector.DeltaY);
				_boundaryPerpInverseDistances[i] = 1.0 / Math.Max(point2PointTerm, 1E-12);
			}
		}
		_maximumTimestepBoundaryLookup = new TransportBoundaryCondition[_mesh.FaceCount];
		_maximumTimestepBoundaryFaceSlot = new int[_mesh.FaceCount];
		RefreshBoundaryLookup(_maximumTimestepBoundaryLookup, _maximumTimestepBoundaryFaceSlot);
		_threadFor?.Dispose();
		_threadFor = new ThreadFor(ThreadCount);
		int count = _flow.CellWaterSources.Sources.Count;
		_externalSourceConcentration = new double[_constituentCount][];
		for (int j = 0; j < _constituentCount; j++)
		{
			_externalSourceConcentration[j] = new double[count];
		}
		_externalSourceConcentrationsDirty = true;
		PrepareInternal();
		_prepared = true;
		_canResolve = false;
	}

	protected abstract void PrepareInternal();

	protected void EnsurePrepared()
	{
		if (!_prepared)
		{
			throw new InvalidOperationException("Prepare must be called first.");
		}
	}

	protected void RefreshBoundaryLookup(TransportBoundaryCondition[] lookup, int[] faceSlot)
	{
		_bcs.FillFaceLookup(lookup, faceSlot);
	}

	protected bool IsFaceActiveForCell(int face, int cell)
	{
		if (!_flow.IsFaceActive(face))
		{
			return false;
		}
		int faceCellB = ((_connectivity.FaceCellA[face] == cell) ? _connectivity.FaceCellB[face] : _connectivity.FaceCellA[face]);
		if (faceCellB >= 0)
		{
			return _flow.IsCellActive(faceCellB);
		}
		return true;
	}

	protected bool NeedsDiffusionGradient(int constituent)
	{
		if (UsesCellGradientCorrection)
		{
			return HasDiffusion(constituent);
		}
		return false;
	}

	protected bool NeedsDiffusionNodeValues(int constituent)
	{
		if (_control.faceNormalGradientMethod == FaceNormalGradientMethod.CompactFaceBased)
		{
			return HasDiffusion(constituent);
		}
		return false;
	}

	protected double ComputeFaceNormalGradientCorrection(int face, ReadOnlySpan<double> cellValues, Vector[] cellGradients, double[] cellGradientLimiters, double[] nodeFaceValues, HydraulicMeshComputer.NodeFaceInterpolationWorkspace nodeInterpolation, double inversePerpendicularDistance)
	{
		return _hmesh.FaceComputes.FaceNormalGradientDeferredCorrection(_control.faceNormalGradientMethod, face, cellValues, cellGradients, cellGradientLimiters, nodeFaceValues, nodeInterpolation, inversePerpendicularDistance);
	}

	protected double ComputeFaceNormalGradient(int face, ReadOnlySpan<double> cellValues, Vector[] cellGradients, double[] cellGradientLimiters, double[] nodeFaceValues, HydraulicMeshComputer.NodeFaceInterpolationWorkspace nodeInterpolation)
	{
		return _hmesh.FaceComputes.FaceNormalGradient(_control.faceNormalGradientMethod, face, cellValues, cellGradients, cellGradientLimiters, nodeFaceValues, nodeInterpolation);
	}

	protected void ConfigureBoundaryReconstruction(HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace workspace, int constituent, int[] boundaryFaces, TransportBoundaryCondition[] lookup, int[] faceSlots, double[] faceFlow, bool reverseFlowUsesEquilibrium, bool oldLevel)
	{
		workspace.Clear();
		foreach (int boundaryFaceIndex in boundaryFaces)
		{
			TransportBoundaryCondition transportBoundaryCondition = lookup[boundaryFaceIndex];
			TransportBCType transportBCType = transportBoundaryCondition?.Type ?? TransportBCType.ZeroGradientOutflow;
			bool faceFlowIsNegative = faceFlow[boundaryFaceIndex] < -1E-08;
			switch (transportBCType)
			{
			case TransportBCType.DirichletInflow:
				if (faceFlowIsNegative || DiffusivityAt(constituent, boundaryFaceIndex) > 0.0)
				{
					workspace.SetValue(boundaryFaceIndex, transportBoundaryCondition.ConcentrationAt(constituent, faceSlots[boundaryFaceIndex]));
				}
				else
				{
					workspace.SetNormalGradient(boundaryFaceIndex, 0.0);
				}
				break;
			case TransportBCType.EquilibriumInflow:
				if (faceFlowIsNegative)
				{
					workspace.SetValue(boundaryFaceIndex, transportBoundaryCondition.ConcentrationAt(constituent, faceSlots[boundaryFaceIndex]));
				}
				else
				{
					workspace.SetNormalGradient(boundaryFaceIndex, 0.0);
				}
				break;
			case TransportBCType.ZeroGradientOutflow:
			{
				if ((reverseFlowUsesEquilibrium & faceFlowIsNegative) && TryEquilibriumConcentration(constituent, _connectivity.FaceCellA[boundaryFaceIndex], oldLevel, out var concentration))
				{
					workspace.SetValue(boundaryFaceIndex, concentration);
				}
				else
				{
					workspace.SetNormalGradient(boundaryFaceIndex, 0.0);
				}
				break;
			}
			case TransportBCType.NoFluxWall:
			case TransportBCType.PrescribedMassFlux:
				workspace.SetNormalGradient(boundaryFaceIndex, 0.0);
				break;
			default:
				throw new ArgumentOutOfRangeException("type");
			}
		}
	}

	protected bool TryEquilibriumConcentration(int constituent, int cell, bool oldLevel, out double concentration)
	{
		double[] cellSourceMassRateOld = (oldLevel ? _cellSourceMassRateOld[constituent] : _cellSourceMassRateNew[constituent]);
		double[] cellSinkCoeffOld = (oldLevel ? _cellSinkCoeffOld[constituent] : _cellSinkCoeffNew[constituent]);
		double sourceMassRate = ((cellSourceMassRateOld != null) ? cellSourceMassRateOld[cell] : 0.0);
		double sinkCoefficient = ((cellSinkCoeffOld != null) ? cellSinkCoeffOld[cell] : 0.0);
		return TryReactionEquilibriumConcentration(constituent, sourceMassRate, sinkCoefficient, out concentration);
	}

	public ITransport.Status SolveStep(double absTime, double dt, out int failureCell)
	{
		ValidateStep(absTime, dt);
		failureCell = -1;
		_canResolve = false;
		BeginDerivedVolumeDiagnostics();
		_oldState.UpdateFrom(_state);
		_checkpointAvailable = _checkpointRequested;
		_checkpointRequested = false;
		CaptureTemporalBeta(roll: true);
		CaptureReactionCoefficients(roll: true);
		RefreshExternalSourceConcentrations();
		ITransport.Status status = SolveStepInternal(absTime, dt, out failureCell);
		if (status == ITransport.Status.OK)
		{
			_state.Time = absTime + dt;
			_canResolve = true;
			_lastStepTime = absTime;
			_lastStepDt = dt;
			ReportDerivedVolumeDiagnostics();
			return status;
		}
		RollbackStep();
		return status;
	}

	public ITransport.Status ResolveStep(double absTime, double dt, out int failureCell)
	{
		ValidateStep(absTime, dt);
		if (!_canResolve || absTime != _lastStepTime || dt != _lastStepDt)
		{
			throw new InvalidOperationException("ResolveStep is only valid immediately after a successful matching SolveStep.");
		}
		CaptureTemporalBeta(roll: false);
		CaptureReactionCoefficients(roll: false);
		RefreshExternalSourceConcentrations();
		ITransport.Status status = SolveStepInternal(absTime, dt, out failureCell);
		if (status == ITransport.Status.OK)
		{
			_state.Time = absTime + dt;
			return status;
		}
		RollbackStep();
		return status;
	}

	private void RollbackStep()
	{
		_state.UpdateFrom(_oldState);
		CopyLevels(_cellTemporalBetaOld, _cellTemporalBetaNew);
		CopyLevels(_cellSourceMassRateOld, _cellSourceMassRateNew);
		CopyLevels(_cellSinkCoeffOld, _cellSinkCoeffNew);
		_canResolve = false;
	}

	private void CaptureReactionCoefficients(bool roll)
	{
		CaptureLevels(_cellSourceMassRate, _cellSourceMassRateNew, _cellSourceMassRateOld, roll);
		CaptureLevels(_cellSinkCoeff, _cellSinkCoeffNew, _cellSinkCoeffOld, roll);
	}

	private static void CaptureLevels(double[][] liveLevels, double[][] newLevels, double[][] oldLevels, bool roll)
	{
		for (int i = 0; i < liveLevels.Length; i++)
		{
			if (roll)
			{
				double[] oldLevel = oldLevels[i];
				oldLevels[i] = newLevels[i];
				newLevels[i] = oldLevel;
			}
			double[] newLevel = liveLevels[i];
			if (newLevel == null)
			{
				newLevels[i] = null;
				continue;
			}
			if (newLevels[i] == null || newLevels[i].Length != newLevel.Length)
			{
				newLevels[i] = new double[newLevel.Length];
			}
			Array.Copy(newLevel, newLevels[i], newLevel.Length);
		}
	}

	protected abstract ITransport.Status SolveStepInternal(double absTime, double dt, out int failureCell);

	public virtual double MaximumTimestep(out int limitingCell)
	{
		EnsurePrepared();
		limitingCell = -1;
		double current = double.PositiveInfinity;
		double thetaImplicitOffset = 1.0 - _control.ThetaImplicit;
		TransportBoundaryCondition[] bcLookup = null;
		if (thetaImplicitOffset > 0.0)
		{
			RefreshBoundaryLookup(_maximumTimestepBoundaryLookup, _maximumTimestepBoundaryFaceSlot);
			bcLookup = _maximumTimestepBoundaryLookup;
		}
		for (int i = 0; i < _constituentCount; i++)
		{
			for (int j = 0; j < _mesh.CellCount; j++)
			{
				if (!_flow.IsCellActive(j) || _flow.CellVolumeNew[j] <= 0.0)
				{
					continue;
				}
				double computed = 0.0;
				Span<int> faces = _mesh.Cells[j].Faces.Span;
				for (int k = 0; k < faces.Length; k++)
				{
					int faceIndex = faces[k];
					if (IsFaceActiveForCell(faceIndex, j))
					{
						double orientationConnectivityTerm = (double)_connectivity.Orientation(faceIndex, j) * _flow.FaceFlowNew[faceIndex] * AdvectionAlphaAt(i, faceIndex);
						if (orientationConnectivityTerm > 0.0)
						{
							computed += orientationConnectivityTerm;
						}
					}
				}
				computed += _flow.CellWaterSources.GetAdvectiveOutflow(j);
				if (computed > 0.0)
				{
					double[] cellTemporalBeta = _cellTemporalBeta[i];
					double cellTemporalBeta2 = ((cellTemporalBeta != null) ? cellTemporalBeta[j] : 1.0);
					UpdateTimestepLimit(_control.MaxCourant * _flow.CellVolumeNew[j] / (cellTemporalBeta2 * computed), j, ref current, ref limitingCell);
				}
				if (!(thetaImplicitOffset <= 0.0) && !(_flow.CellVolumeOld[j] <= 0.0))
				{
					double explicitRemovalRate = ExplicitRemovalRate(i, j, bcLookup);
					if (!(explicitRemovalRate <= 0.0))
					{
						double[] cellTemporalBetaOld = _cellTemporalBetaOld[i];
						UpdateTimestepLimit(_flow.CellVolumeOld[j] / ((cellTemporalBetaOld != null) ? cellTemporalBetaOld[j] : 1.0) / (thetaImplicitOffset * explicitRemovalRate), j, ref current, ref limitingCell);
					}
				}
			}
		}
		return current;
	}

	private double ExplicitRemovalRate(int k, int c, TransportBoundaryCondition[] bcLookup)
	{
		double advectiveOutflow = _flow.CellWaterSources.GetAdvectiveOutflow(c);
		Span<int> faces = _mesh.Cells[c].Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			if (!IsFaceActiveForCell(faceIndex, c))
			{
				continue;
			}
			int faceCellB = ((_connectivity.FaceCellA[faceIndex] == c) ? _connectivity.FaceCellB[faceIndex] : _connectivity.FaceCellA[faceIndex]);
			double orientationConnectivityTerm = (double)_connectivity.Orientation(faceIndex, c) * _flow.FaceFlowOld[faceIndex];
			double advectionAlphaAtFaceIndexTerm = orientationConnectivityTerm * AdvectionAlphaAt(k, faceIndex);
			double diffusivityAt = DiffusivityAt(k, faceIndex);
			if (faceCellB >= 0)
			{
				advectiveOutflow += Math.Max(advectionAlphaAtFaceIndexTerm, 0.0);
				if (diffusivityAt > 0.0)
				{
					advectiveOutflow += diffusivityAt * _flow.FaceAreaOld[faceIndex] * Math.Max(_hmesh.FaceInvPerpDistances[faceIndex], 0.0);
				}
				continue;
			}
			TransportBCType transportBCType = bcLookup[faceIndex]?.Type ?? TransportBCType.ZeroGradientOutflow;
			if (transportBCType == TransportBCType.ZeroGradientOutflow || transportBCType == TransportBCType.DirichletInflow || transportBCType == TransportBCType.EquilibriumInflow)
			{
				advectiveOutflow += Math.Max(advectionAlphaAtFaceIndexTerm, 0.0);
			}
			if ((transportBCType == TransportBCType.DirichletInflow || ((transportBCType == TransportBCType.EquilibriumInflow || transportBCType == TransportBCType.ZeroGradientOutflow) && orientationConnectivityTerm < -1E-08)) && diffusivityAt > 0.0)
			{
				double boundaryPerpDistance = BoundaryPerpDistance(faceIndex, c);
				advectiveOutflow += diffusivityAt * _flow.FaceAreaOld[faceIndex] / boundaryPerpDistance;
			}
		}
		if (_control.sourceTimeIntegration == SourceTimeIntegration.ThetaWeighted)
		{
			double[] cellSinkCoeffOld = _cellSinkCoeffOld[k];
			if (cellSinkCoeffOld != null && cellSinkCoeffOld[c] < 0.0)
			{
				advectiveOutflow -= cellSinkCoeffOld[c];
			}
		}
		return advectiveOutflow;
	}

	protected double BoundaryPerpDistance(int f, int c)
	{
		double boundaryPerpInverseDistance = _boundaryPerpInverseDistances[f];
		if (!(boundaryPerpInverseDistance > 0.0))
		{
			return 1E-12;
		}
		return 1.0 / boundaryPerpInverseDistance;
	}

	protected double BoundaryPerpInverseDistance(int face)
	{
		return _boundaryPerpInverseDistances[face];
	}

	private void RefreshExternalSourceConcentrations()
	{
		IReadOnlyList<CellWaterSourceInfo> sources = _flow.CellWaterSources.Sources;
		if (_externalSourceConcentration.Length == 0 || _externalSourceConcentration[0].Length != sources.Count)
		{
			_externalSourceConcentration = new double[_constituentCount][];
			for (int i = 0; i < _constituentCount; i++)
			{
				_externalSourceConcentration[i] = new double[sources.Count];
			}
			_externalSourceConcentrationsDirty = true;
		}
		if (!_externalSourceConcentrationsDirty)
		{
			return;
		}
		for (int j = 0; j < _constituentCount; j++)
		{
			double[] externalSourceConcentration = _externalSourceConcentration[j];
			for (int k = 0; k < sources.Count; k++)
			{
				CellWaterSourceId id = sources[k].Id;
				if (id.Value != k)
				{
					throw new InvalidOperationException("Cell-water source IDs must be contiguous and match source order.");
				}
				_externalSourceConcentrations.TryGetValue(ExternalSourceConcentrationKey(id, j), out externalSourceConcentration[k]);
			}
		}
		_externalSourceConcentrationsDirty = false;
	}

	private static long ExternalSourceConcentrationKey(CellWaterSourceId source, int constituent)
	{
		return ((long)constituent << 32) | (uint)source.Value;
	}

	protected bool TryReactionEquilibriumConcentration(int constituent, double sourceMassRate, double sinkCoefficient, out double concentration)
	{
		concentration = ((sinkCoefficient < 0.0) ? ((0.0 - sourceMassRate) / sinkCoefficient) : double.NaN);
		if (double.IsFinite(concentration) && concentration >= _control.ConstituentMinimumValues[constituent])
		{
			return concentration <= _control.ConstituentMaximumValues[constituent];
		}
		return false;
	}

	private static void UpdateTimestepLimit(double candidate, int cell, ref double current, ref int limitingCell)
	{
		if (!(candidate >= current))
		{
			current = candidate;
			limitingCell = cell;
		}
	}

	protected double EffectiveNewVolume(int c, double dt)
	{
		if (!_control.DeriveVolumeChangeFromFlows)
		{
			return _flow.CellVolumeNew[c];
		}
		ReadOnlySpan<int> cellNeighborFaces = _connectivity.CellNeighborFaces.AsSpan();
		ReadOnlySpan<int> cellFaceOrientations = _connectivity.CellFaceOrientations.AsSpan();
		int[] cellNeighborOffsets = _connectivity.CellNeighborOffsets;
		int cellNeighborOffsetStart = cellNeighborOffsets[c];
		int cellNeighborOffsetEnd = cellNeighborOffsets[c + 1];
		double computed = 0.0;
		double thetaImplicit = _control.ThetaImplicit;
		double[] faceFlowNew = _flow.FaceFlowNew;
		if (thetaImplicit == 1.0)
		{
			for (int i = cellNeighborOffsetStart; i < cellNeighborOffsetEnd; i++)
			{
				int cellNeighborFaceIndex = cellNeighborFaces[i];
				if (IsFaceActiveForCell(cellNeighborFaceIndex, c))
				{
					int cellFaceOrientation = cellFaceOrientations[i];
					computed += (double)cellFaceOrientation * faceFlowNew[cellNeighborFaceIndex];
				}
			}
		}
		else
		{
			double thetaImplicitOffset = 1.0 - thetaImplicit;
			double[] faceFlowOld = _flow.FaceFlowOld;
			for (int j = cellNeighborOffsetStart; j < cellNeighborOffsetEnd; j++)
			{
				int cellNeighborFaceIndex2 = cellNeighborFaces[j];
				if (IsFaceActiveForCell(cellNeighborFaceIndex2, c))
				{
					int cellFaceOrientation2 = cellFaceOrientations[j];
					double thetaImplicitFaceFlowNewTerm = thetaImplicit * faceFlowNew[cellNeighborFaceIndex2] + thetaImplicitOffset * faceFlowOld[cellNeighborFaceIndex2];
					computed += (double)cellFaceOrientation2 * thetaImplicitFaceFlowNewTerm;
				}
			}
		}
		return _flow.CellVolumeOld[c] - dt * computed + dt * _flow.CellWaterSources.GetNetFlow(c);
	}

	protected bool TryNormalizeDerivedVolume(int cell, ref double volume)
	{
		if (volume >= 0.0)
		{
			return true;
		}
		if (!_control.DeriveVolumeChangeFromFlows)
		{
			return false;
		}
		double cellPolygonAreasHmeshTerm = _hmesh.CellPolygonAreas[cell] * _control.DerivedVolumeClampDepth;
		double worstClampedDerivedVolume = 0.0 - volume;
		if (worstClampedDerivedVolume > cellPolygonAreasHmeshTerm)
		{
			return false;
		}
		volume = 0.0;
		if (_clampedDerivedVolumeCells[cell] != 0)
		{
			return true;
		}
		_clampedDerivedVolumeCells[cell] = 1;
		_clampedDerivedVolumeCount++;
		if (worstClampedDerivedVolume > _worstClampedDerivedVolume)
		{
			_worstClampedDerivedVolume = worstClampedDerivedVolume;
			_worstClampedDerivedVolumeCell = cell;
		}
		return true;
	}

	private void BeginDerivedVolumeDiagnostics()
	{
		Array.Clear(_clampedDerivedVolumeCells);
		_clampedDerivedVolumeCount = 0;
		_worstClampedDerivedVolumeCell = -1;
		_worstClampedDerivedVolume = 0.0;
	}

	private void ReportDerivedVolumeDiagnostics()
	{
		if (_clampedDerivedVolumeCount != 0)
		{
			int worstClampedDerivedVolumeCell = _worstClampedDerivedVolumeCell;
			double worstClampedDerivedVolumeOverCellPolygonArea = _worstClampedDerivedVolume / _hmesh.CellPolygonAreas[worstClampedDerivedVolumeCell];
			string message = $"Transport clamped small negative derived volumes to zero in {_clampedDerivedVolumeCount} cells. Worst cell: {worstClampedDerivedVolumeCell}; volume deficit: {_worstClampedDerivedVolume:G17}; equivalent depth: {worstClampedDerivedVolumeOverCellPolygonArea:G17}.";
			if (_reporter != null)
			{
				_reporter.ReportWarning(message);
				return;
			}
			_logger.LogWarning("{Message}", message);
		}
	}

	private void ValidateStep(double absTime, double dt)
	{
		EnsurePrepared();
		if (!double.IsFinite(absTime))
		{
			throw new ArgumentOutOfRangeException("absTime");
		}
		if (!double.IsFinite(dt) || dt <= 0.0)
		{
			throw new ArgumentOutOfRangeException("dt");
		}
		ValidateConfiguration();
	}

	private void ValidateConfiguration()
	{
		if (!double.IsFinite(_control.ThetaImplicit) || _control.ThetaImplicit < 0.5 || _control.ThetaImplicit > 1.0)
		{
			throw new ArgumentOutOfRangeException("ThetaImplicit");
		}
		if (!double.IsFinite(_control.MaxCourant) || _control.MaxCourant <= 0.0)
		{
			throw new ArgumentOutOfRangeException("MaxCourant");
		}
		if (!double.IsFinite(_control.DerivedVolumeClampDepth) || _control.DerivedVolumeClampDepth < 0.0)
		{
			throw new ArgumentOutOfRangeException("DerivedVolumeClampDepth");
		}
		if (_control.OuterControl.IterMax <= 0)
		{
			throw new ArgumentOutOfRangeException("OuterControl");
		}
		if (!double.IsFinite(_control.OuterControl.tolMax) || _control.OuterControl.tolMax < 0.0 || !double.IsFinite(_control.OuterControl.tolConv) || _control.OuterControl.tolConv < 0.0 || !double.IsFinite(_control.OuterControl.tolRMS) || _control.OuterControl.tolRMS < 0.0)
		{
			throw new ArgumentOutOfRangeException("OuterControl");
		}
		if (!Enum.IsDefined(_control.advectionScheme))
		{
			throw new ArgumentOutOfRangeException("advectionScheme");
		}
		if (!Enum.IsDefined(_control.fluxLimiter))
		{
			throw new ArgumentOutOfRangeException("fluxLimiter");
		}
		if (!Enum.IsDefined(_control.TVDGradientRatioMethod))
		{
			throw new ArgumentOutOfRangeException("TVDGradientRatioMethod");
		}
		if (!Enum.IsDefined(_control.multidimensionalSlopeLimiter))
		{
			throw new ArgumentOutOfRangeException("multidimensionalSlopeLimiter");
		}
		if (!Enum.IsDefined(_control.faceNormalGradientMethod))
		{
			throw new ArgumentOutOfRangeException("faceNormalGradientMethod");
		}
		if (!Enum.IsDefined(_control.sourceTimeIntegration))
		{
			throw new ArgumentOutOfRangeException("sourceTimeIntegration");
		}
		ValidateArray(_flow.FaceFlowOld, _mesh.FaceCount, "FaceFlowOld", nonnegative: false);
		ValidateArray(_flow.FaceFlowNew, _mesh.FaceCount, "FaceFlowNew", nonnegative: false);
		ValidateArray(_flow.FaceAreaOld, _mesh.FaceCount, "FaceAreaOld", nonnegative: true);
		ValidateArray(_flow.FaceAreaNew, _mesh.FaceCount, "FaceAreaNew", nonnegative: true);
		ValidateArray(_flow.CellVolumeOld, _mesh.CellCount, "CellVolumeOld", nonnegative: true);
		ValidateArray(_flow.CellVolumeNew, _mesh.CellCount, "CellVolumeNew", nonnegative: true);
		if (_flow.CellWaterSources == null)
		{
			throw new ArgumentNullException("CellWaterSources");
		}
		if (_flow.CellWaterSources.CellCount != _mesh.CellCount)
		{
			throw new ArgumentException("Cell-water source dimensions must match the mesh.");
		}
		if (_flow.CellActive != null && _flow.CellActive.Length != _mesh.CellCount)
		{
			throw new ArgumentException("CellActive length must equal Mesh.CellCount.");
		}
		if (_flow.FaceActive != null && _flow.FaceActive.Length != _mesh.FaceCount)
		{
			throw new ArgumentException("FaceActive length must equal Mesh.FaceCount.");
		}
		if (_control.ConstituentMinimumValues == null || _control.ConstituentMinimumValues.Length < _constituentCount)
		{
			throw new ArgumentException("Constituent minimum values must cover every constituent.");
		}
		if (_control.ConstituentMaximumValues == null || _control.ConstituentMaximumValues.Length < _constituentCount)
		{
			throw new ArgumentException("Constituent maximum values must cover every constituent.");
		}
		if (_control.ConstituentRangeClampTolerances == null || _control.ConstituentRangeClampTolerances.Length < _constituentCount)
		{
			throw new ArgumentException("ConstituentRangeClampTolerances");
		}
		for (int i = 0; i < _constituentCount; i++)
		{
			double constituentMinimumValue = _control.ConstituentMinimumValues[i];
			double constituentMaximumValue = _control.ConstituentMaximumValues[i];
			if (!double.IsFinite(constituentMinimumValue) || !double.IsFinite(constituentMaximumValue) || constituentMinimumValue > constituentMaximumValue)
			{
				throw new ArgumentOutOfRangeException("ConstituentMinimumValues");
			}
			double constituentRangeClampTolerance = _control.ConstituentRangeClampTolerances[i];
			if (!double.IsFinite(constituentRangeClampTolerance) || constituentRangeClampTolerance < 0.0)
			{
				throw new ArgumentOutOfRangeException("ConstituentRangeClampTolerances");
			}
			if (!double.IsFinite(_diffusivity[i]) || _diffusivity[i] < 0.0)
			{
				throw new ArgumentOutOfRangeException("_diffusivity");
			}
			ValidateOptionalArray(_faceDiffusivity[i], _mesh.FaceCount, "_faceDiffusivity", nonnegative: true);
			ValidateOptionalArray(_faceAdvectionAlpha[i], _mesh.FaceCount, "_faceAdvectionAlpha", nonnegative: true);
			ValidateOptionalArray(_cellSourceMassRate[i], _mesh.CellCount, "_cellSourceMassRate", nonnegative: false);
			ValidateOptionalArray(_cellSinkCoeff[i], _mesh.CellCount, "_cellSinkCoeff", nonnegative: false, nonpositive: true);
			double[] cellTemporalBeta = _cellTemporalBeta[i];
			if (cellTemporalBeta == null)
			{
				continue;
			}
			if (cellTemporalBeta.Length != _mesh.CellCount)
			{
				throw new ArgumentException("Temporal beta length must equal Mesh.CellCount.");
			}
			for (int j = 0; j < cellTemporalBeta.Length; j++)
			{
				if (!double.IsFinite(cellTemporalBeta[j]) || cellTemporalBeta[j] <= 0.0 || cellTemporalBeta[j] > 1.0)
				{
					throw new ArgumentException($"Temporal beta contains an invalid value at index {j}.");
				}
			}
		}
		ValidateBoundaryConditions();
		ValidateDerivedConfiguration();
	}

	protected virtual void ValidateDerivedConfiguration()
	{
	}

	protected ITransport.Status ClampAndValidateStateRanges(bool[] activeMask, out int failureCell)
	{
		int nCells = _mesh.CellCount;
		int nonfiniteCell = nCells;
		int invalidRangeCell = nCells;
		_threadFor.Run(_constituentCount * nCells, (int start, int stop) =>
		{
			int nCells2 = nCells;
			int nCells3 = nCells;
			for (int i = start; i <= stop; i++)
			{
				int nCellsTerm = i / nCells;
				int nCellsTerm2 = i - nCellsTerm * nCells;
				if (activeMask == null || activeMask[nCellsTerm2])
				{
					double cellConcentration = _state.CellConcentration[nCellsTerm][nCellsTerm2];
					if (!double.IsFinite(cellConcentration))
					{
						nCells2 = Math.Min(nCells2, nCellsTerm2);
					}
					else
					{
						double constituentMinimumValue = _control.ConstituentMinimumValues[nCellsTerm];
						double constituentMaximumValue = _control.ConstituentMaximumValues[nCellsTerm];
						double constituentRangeClampTolerance = _control.ConstituentRangeClampTolerances[nCellsTerm];
						if (cellConcentration < constituentMinimumValue)
						{
							if (constituentMinimumValue - cellConcentration <= constituentRangeClampTolerance)
							{
								_state.CellConcentration[nCellsTerm][nCellsTerm2] = constituentMinimumValue;
							}
							else
							{
								nCells3 = Math.Min(nCells3, nCellsTerm2);
							}
						}
						else if (cellConcentration > constituentMaximumValue)
						{
							if (cellConcentration - constituentMaximumValue <= constituentRangeClampTolerance)
							{
								_state.CellConcentration[nCellsTerm][nCellsTerm2] = constituentMaximumValue;
							}
							else
							{
								nCells3 = Math.Min(nCells3, nCellsTerm2);
							}
						}
					}
				}
			}
			SetMinimum(ref nonfiniteCell, nCells2);
			SetMinimum(ref invalidRangeCell, nCells3);
		});
		if (nonfiniteCell < nCells)
		{
			failureCell = nonfiniteCell;
			return ITransport.Status.NAN_STATE;
		}
		if (invalidRangeCell < nCells)
		{
			failureCell = invalidRangeCell;
			return ITransport.Status.INVALID_RANGE;
		}
		failureCell = -1;
		return ITransport.Status.OK;
	}

	protected static void SetMinimum(ref int target, int minimum)
	{
		int target2 = target;
		while (minimum < target2)
		{
			int compareExchange = Interlocked.CompareExchange(ref target, minimum, target2);
			if (compareExchange == target2)
			{
				break;
			}
			target2 = compareExchange;
		}
	}

	private protected double ComputeTVDDeferredCorrection(int face, bool cellAIsUpwind, double[] concentration, Vector[] rawGradient, HydraulicMeshComputer.TVDVirtualPointInterpolationWorkspace TVDVirtualPointInterpolation, CasulliFaceFlowInterpolationWorkspace TVDCasulliInterpolation, double[] TVDCasulliUpstreamValues)
	{
		int faceCellA = _connectivity.FaceCellA[face];
		int faceCellB = _connectivity.FaceCellB[face];
		int faceCellA2 = (cellAIsUpwind ? faceCellA : faceCellB);
		int faceCellB2 = (cellAIsUpwind ? faceCellB : faceCellA);
		double concentration2 = concentration[faceCellA2];
		double concentration3 = concentration[faceCellB2];
		double concentration3AboveConcentration2 = concentration3 - concentration2;
		if (concentration3AboveConcentration2 == 0.0 || !double.IsFinite(concentration3AboveConcentration2))
		{
			return 0.0;
		}
		double r;
		switch (_control.TVDGradientRatioMethod)
		{
		case TVDGradientRatioMethod.DarwishMoukalledCellGradient:
		{
			Vector vector = rawGradient[faceCellA2];
			Vector vector2 = _TVDFaceCellCenterDeltas[face];
			r = FluxLimiters.ConvectiveRatio((cellAIsUpwind ? 1.0 : (-1.0)) * (vector.DeltaX * vector2.DeltaX + vector.DeltaY * vector2.DeltaY), concentration2, concentration3);
			break;
		}
		case TVDGradientRatioMethod.UpstreamVirtualPoint:
		{
			if (TVDVirtualPointInterpolation == null || !TVDVirtualPointInterpolation.TryInterpolateTVDVirtualPointUnchecked(face, cellAIsUpwind, concentration, out var TVDVirtualPointValue))
			{
				return 0.0;
			}
			r = FluxLimiters.TVDVirtualPointConvectiveRatio(TVDVirtualPointValue, concentration2, concentration3);
			break;
		}
		case TVDGradientRatioMethod.CasulliFaceFlowWeighted:
			if (TVDCasulliInterpolation == null || TVDCasulliUpstreamValues == null || !TVDCasulliInterpolation.IsFaceConnectedUnchecked(face))
			{
				return 0.0;
			}
			r = FluxLimiters.UpstreamValueConvectiveRatio(TVDCasulliUpstreamValues[faceCellA2], concentration2, concentration3);
			break;
		default:
			throw new ArgumentOutOfRangeException("TVDGradientRatioMethod");
		}
		double limit = FluxLimiters.Limit(_control.fluxLimiter, r);
		double limitTerm = 0.5 * limit * concentration3AboveConcentration2;
		if (!double.IsFinite(limitTerm))
		{
			return 0.0;
		}
		return limitTerm;
	}

	private void ValidateBoundaryConditions()
	{
		foreach (TransportBoundaryCondition transportBoundaryCondition in _bcs.Items)
		{
			if (transportBoundaryCondition == null)
			{
				throw new ArgumentException("Boundary-condition collection contains a null entry.");
			}
			if (!Enum.IsDefined(transportBoundaryCondition.Type))
			{
				throw new ArgumentOutOfRangeException("Type");
			}
			if (transportBoundaryCondition.Faces == null)
			{
				throw new ArgumentException("Boundary-condition faces cannot be null.");
			}
			for (int i = 0; i < transportBoundaryCondition.Faces.Length; i++)
			{
				int faceIndex = transportBoundaryCondition.Faces[i];
				if (faceIndex < 0 || faceIndex >= _mesh.FaceCount)
				{
					throw new ArgumentException($"Boundary-condition face index {faceIndex} is outside the mesh.");
				}
				if (_mesh.Faces[faceIndex].cellB >= 0)
				{
					throw new ArgumentException($"Boundary-condition face {faceIndex} is not a boundary face.");
				}
			}
			if (transportBoundaryCondition.Type != TransportBCType.DirichletInflow && transportBoundaryCondition.Type != TransportBCType.EquilibriumInflow && transportBoundaryCondition.Type != TransportBCType.PrescribedMassFlux)
			{
				continue;
			}
			if (transportBoundaryCondition.FaceConcentrationByConstituent != null && transportBoundaryCondition.FaceConcentrationByConstituent.Length != _constituentCount)
			{
				throw new ArgumentException("Per-face boundary concentrations must match ConstituentCount.");
			}
			if (transportBoundaryCondition.ConcentrationByConstituent != null && transportBoundaryCondition.ConcentrationByConstituent.Length != _constituentCount)
			{
				throw new ArgumentException("Boundary concentrations must match ConstituentCount.");
			}
			for (int j = 0; j < _constituentCount; j++)
			{
				double[][] faceConcentrationByConstituent = transportBoundaryCondition.FaceConcentrationByConstituent;
				double[] faceConcentrationByConstituentArray = ((faceConcentrationByConstituent != null) ? faceConcentrationByConstituent[j] : null);
				if (faceConcentrationByConstituentArray != null)
				{
					ValidateArray(faceConcentrationByConstituentArray, transportBoundaryCondition.Faces.Length, "FaceConcentrationByConstituent", nonnegative: false);
					continue;
				}
				if (transportBoundaryCondition.ConcentrationByConstituent == null)
				{
					throw new ArgumentException($"Boundary condition is missing concentration for constituent {j}.");
				}
				if (!double.IsFinite(transportBoundaryCondition.ConcentrationByConstituent[j]))
				{
					throw new ArgumentException($"Boundary concentration for constituent {j} is not finite.");
				}
			}
		}
	}

	private static void ValidateOptionalArray(double[] values, int length, string name, bool nonnegative, bool nonpositive = false)
	{
		if (values != null)
		{
			ValidateArray(values, length, name, nonnegative, nonpositive);
		}
	}

	private void ValidateConstituent(int constituent)
	{
		if ((uint)constituent >= (uint)_constituentCount)
		{
			throw new ArgumentOutOfRangeException("constituent");
		}
	}

	private static void ValidateArray(double[] values, int length, string name, bool nonnegative, bool nonpositive = false)
	{
		if (values == null || values.Length != length)
		{
			throw new ArgumentException($"{name} must have length {length}.");
		}
		for (int i = 0; i < values.Length; i++)
		{
			if (!double.IsFinite(values[i]) || (nonnegative && values[i] < 0.0) || (nonpositive && values[i] > 0.0))
			{
				throw new ArgumentException($"{name} contains an invalid value at index {i}.");
			}
		}
	}

	public void CheckpointState()
	{
		_checkpointRequested = true;
		_checkpointAvailable = false;
	}

	public void RevertState()
	{
		if (_checkpointAvailable)
		{
			RollbackStep();
		}
	}

	public void CommitState()
	{
		_checkpointRequested = false;
		_checkpointAvailable = false;
	}

	private static void CopyLevels(double[][] source, double[][] target)
	{
		for (int i = 0; i < target.Length; i++)
		{
			double[] targetArray = source[i];
			if (targetArray == null)
			{
				target[i] = null;
				continue;
			}
			if (target[i] == null || target[i].Length != targetArray.Length)
			{
				target[i] = new double[targetArray.Length];
			}
			Array.Copy(targetArray, target[i], targetArray.Length);
		}
	}

	public virtual void Cleanup()
	{
		_canResolve = false;
		_checkpointRequested = false;
		_checkpointAvailable = false;
		_prepared = false;
		_threadFor?.Dispose();
		_threadFor = new ThreadFor(1);
	}
}
