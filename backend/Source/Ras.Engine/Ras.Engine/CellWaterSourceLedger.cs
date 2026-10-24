using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Ras.Engine.Structures;

namespace Ras.Engine;

public sealed class CellWaterSourceLedger
{
	private readonly Solver _solver;

	private readonly int _cellCount;

	private readonly double[] _currentNetFlow;

	private readonly int[] _activeNetSourceCells;

	private int _activeNetSourceCellCount;

	private double[] _currentAdvectiveOutflow;

	private double[] _currentTransferNetFlow;

	private bool[] _currentSourceActive;

	private double[] _appliedNetVolume;

	private double[] _appliedAdvectiveOutflowVolume;

	private double[] _currentExternalInflow;

	private double[] _appliedExternalInflowVolume;

	private double[] _positiveInternalFlow;

	private double[] _negativeInternalFlowMagnitude;

	private int _precipitationEntryOffset = -1;

	private int[][] _internalEntryIndices;

	private Dictionary<int, int> _unclassifiedEntryByCell;

	private double[] _currentTransferFlow;

	private double[] _appliedTransferForwardVolume;

	private double[] _appliedTransferReverseVolume;

	private HydraulicStructure[] _transferStructures;

	private int[] _transferUpstreamCells;

	private int[] _transferDownstreamCells;

	private CellWaterSourceInfo[] _sources;

	private CellWaterExternalInflow[] _snapshotExternalInflows;

	private CellWaterTransfer[] _snapshotTransfers;

	private CellWaterSourceSnapshot _snapshot;

	private CellWaterSourceId _precipitationSourceId = CellWaterSourceId.Invalid;

	private CellWaterSourceId _unclassifiedHydraulicSourceId = CellWaterSourceId.Invalid;

	private bool _transportTrackingEnabled;

	private bool _stepStarted;

	private bool _currentRatesDirty = true;

	public bool HasCellSources => _currentNetFlow.Length != 0;

	public bool IsTransportTrackingEnabled => _transportTrackingEnabled;

	public CellWaterSourceId PrecipitationSourceId => _precipitationSourceId;

	public CellWaterSourceId UnclassifiedHydraulicSourceId => _unclassifiedHydraulicSourceId;

	public ReadOnlySpan<double> CurrentNetCellFlow
	{
		get
		{
			RefreshCurrentRates();
			return _currentNetFlow;
		}
	}

	internal double[] CurrentNetFlowArray => _currentNetFlow;

	internal int[] ActiveNetSourceCells => _activeNetSourceCells;

	internal int ActiveNetSourceCellCount => _activeNetSourceCellCount;

	internal CellWaterSourceLedger(Solver solver)
	{
		_solver = solver;
		_cellCount = solver.Mesh.CellCount;
		bool hasLandSurfaceOrHasInternalFlowsOrHasSourceSinks = solver.BoundaryConditions.HasLandSurface || solver.BoundaryConditions.HasInternalFlows || solver.HydraulicStructures.HasSourceSinks;
		_currentNetFlow = (hasLandSurfaceOrHasInternalFlowsOrHasSourceSinks ? new double[_cellCount] : Array.Empty<double>());
		_activeNetSourceCells = (hasLandSurfaceOrHasInternalFlowsOrHasSourceSinks ? new int[_cellCount] : Array.Empty<int>());
		if (solver.BoundaryConditions.HasPrecipitation)
		{
			solver.BoundaryConditions.Precipitation.WaterSourceId = CellWaterSourceId.Invalid;
		}
		foreach (BoundaryCondition internalFlowBoundaryCondition in solver.BoundaryConditions.InternalFlowBoundaryConditions)
		{
			internalFlowBoundaryCondition.WaterSourceId = CellWaterSourceId.Invalid;
		}
		if (solver.BoundaryConditions.HasLandSurface)
		{
			for (int i = 0; i < _cellCount; i++)
			{
				_activeNetSourceCells[i] = i;
			}
		}
	}

	public CellWaterSourceSnapshot EnableTransportTracking()
	{
		if (_transportTrackingEnabled)
		{
			return _snapshot;
		}
		if (_stepStarted)
		{
			throw new InvalidOperationException("Transport source tracking must be enabled before the first hydraulic step.");
		}
		_currentAdvectiveOutflow = new double[_cellCount];
		_currentTransferNetFlow = new double[_cellCount];
		_currentSourceActive = new bool[_cellCount];
		_appliedNetVolume = new double[_cellCount];
		_appliedAdvectiveOutflowVolume = new double[_cellCount];
		PrepareExternalInflows();
		PrepareTransfers();
		_snapshot = new CellWaterSourceSnapshot(_cellCount)
		{
			Sources = _sources
		};
		_snapshot.SetExternalInflows(_snapshotExternalInflows);
		_snapshot.SetTransfers(_snapshotTransfers);
		_transportTrackingEnabled = true;
		_currentRatesDirty = true;
		RefreshCurrentRates();
		return _snapshot;
	}

	public double GetCurrentNetFlow(int cell)
	{
		RefreshCurrentRates();
		if (!HasCellSources)
		{
			return 0.0;
		}
		return _currentNetFlow[cell];
	}

	internal double GetExplicitTimestepFlow(int cell)
	{
		double currentNetFlow = _currentNetFlow[cell];
		BoundaryConditionCollection boundaryConditions = _solver.BoundaryConditions;
		if (boundaryConditions.HasInternalFlows)
		{
			currentNetFlow -= boundaryConditions.CellInternalFlowLimited[cell];
		}
		if (boundaryConditions.HasLandSurface && boundaryConditions.CellExcessFlow[cell] < 0.0)
		{
			currentNetFlow -= boundaryConditions.CellExcessFlow[cell];
		}
		return currentNetFlow;
	}

	internal void BeginStep()
	{
		_stepStarted = true;
		ClearAppliedVolumes();
	}

	internal void RevertStep()
	{
		ClearAppliedVolumes();
	}

	private void ClearAppliedVolumes()
	{
		if (_transportTrackingEnabled)
		{
			Array.Clear(_appliedNetVolume);
			Array.Clear(_appliedAdvectiveOutflowVolume);
			Array.Clear(_appliedExternalInflowVolume);
			Array.Clear(_appliedTransferForwardVolume);
			Array.Clear(_appliedTransferReverseVolume);
		}
	}

	internal void MarkCurrentRatesDirty()
	{
		_currentRatesDirty = true;
	}

	internal void RefreshCurrentRates()
	{
		if (!_currentRatesDirty)
		{
			return;
		}
		if (!HasCellSources)
		{
			_currentRatesDirty = false;
			return;
		}
		BoundaryConditionCollection boundaryConditions = _solver.BoundaryConditions;
		if (!boundaryConditions.HasLandSurface)
		{
			for (int i = 0; i < _activeNetSourceCellCount; i++)
			{
				_currentNetFlow[_activeNetSourceCells[i]] = 0.0;
			}
		}
		_activeNetSourceCellCount = 0;
		if (_transportTrackingEnabled)
		{
			Array.Clear(_currentAdvectiveOutflow);
			Array.Clear(_currentTransferNetFlow);
			Array.Clear(_currentSourceActive);
			Array.Clear(_currentExternalInflow);
			Array.Clear(_currentTransferFlow);
		}
		if (boundaryConditions.HasLandSurface)
		{
			_activeNetSourceCellCount = _cellCount;
			if (!_transportTrackingEnabled)
			{
				Array.Copy(boundaryConditions.CellExcessFlow, _currentNetFlow, _cellCount);
			}
			else
			{
				double[] cellPrecipRate = (boundaryConditions.HasPrecipitation ? boundaryConditions.Precipitation.CellPrecipRate : null);
				double[] cellPolygonAreas = _solver.HydraulicMeshComputer.CellPolygonAreas;
				for (int j = 0; j < _cellCount; j++)
				{
					double currentExternalInflow = ((cellPrecipRate != null) ? (cellPrecipRate[j] * cellPolygonAreas[j]) : 0.0);
					_currentNetFlow[j] = boundaryConditions.CellExcessFlow[j];
					if (currentExternalInflow > 0.0)
					{
						_currentExternalInflow[_precipitationEntryOffset + j] = currentExternalInflow;
					}
					else if (currentExternalInflow < 0.0)
					{
						_currentAdvectiveOutflow[j] -= currentExternalInflow;
					}
				}
			}
		}
		if (boundaryConditions.HasInternalFlows)
		{
			double[] cellInternalFlowLimited = boundaryConditions.CellInternalFlowLimited;
			for (int k = 0; k < boundaryConditions.CellIndicesInternalFlows.Length; k++)
			{
				int cellIndicesInternalFlow = boundaryConditions.CellIndicesInternalFlows[k];
				double cellInternalFlowLimited2 = cellInternalFlowLimited[cellIndicesInternalFlow];
				_currentNetFlow[cellIndicesInternalFlow] += cellInternalFlowLimited2;
			}
			if (_transportTrackingEnabled)
			{
				RefreshInternalFlowTransportRates(boundaryConditions);
			}
		}
		foreach (KeyValuePair<int, (double, double, Vector)> sourceSink in _solver.HydraulicStructures.SourceSinks)
		{
			_currentNetFlow[sourceSink.Key] += sourceSink.Value.Item1;
		}
		if (_transportTrackingEnabled)
		{
			for (int l = 0; l < _transferStructures.Length; l++)
			{
				int transferUpstreamCellIndex = _transferUpstreamCells[l];
				int transferDownstreamCellIndex = _transferDownstreamCells[l];
				double cellWaterTransferFlow = _transferStructures[l].CellWaterTransferFlow;
				_currentTransferFlow[l] = cellWaterTransferFlow;
				_currentTransferNetFlow[transferUpstreamCellIndex] -= cellWaterTransferFlow;
				_currentTransferNetFlow[transferDownstreamCellIndex] += cellWaterTransferFlow;
				if (cellWaterTransferFlow > 0.0)
				{
					_currentAdvectiveOutflow[transferUpstreamCellIndex] += cellWaterTransferFlow;
				}
				else if (cellWaterTransferFlow < 0.0)
				{
					_currentAdvectiveOutflow[transferDownstreamCellIndex] -= cellWaterTransferFlow;
				}
			}
			foreach (KeyValuePair<int, (double, double, Vector)> sourceSink2 in _solver.HydraulicStructures.SourceSinks)
			{
				int key = sourceSink2.Key;
				double currentExternalInflow2 = sourceSink2.Value.Item1 - _currentTransferNetFlow[key];
				double sourceSink2CurrentTransferNetFlowTerm = Math.Max(Math.Abs(sourceSink2.Value.Item1), Math.Abs(_currentTransferNetFlow[key]));
				double computed = 1E-12 * Math.Max(1.0, sourceSink2CurrentTransferNetFlowTerm);
				if (!(Math.Abs(currentExternalInflow2) <= computed))
				{
					if (currentExternalInflow2 >= 0.0)
					{
						_currentExternalInflow[_unclassifiedEntryByCell[key]] = currentExternalInflow2;
					}
					else
					{
						_currentAdvectiveOutflow[key] -= currentExternalInflow2;
					}
				}
			}
		}
		BuildActiveNetSourceCells(boundaryConditions);
		if (_transportTrackingEnabled)
		{
			for (int m = 0; m < _cellCount; m++)
			{
				bool currentSourceActive = _currentNetFlow[m] != 0.0 || _currentAdvectiveOutflow[m] != 0.0;
				ReadOnlySpan<int> incomingExternalIndices = _snapshot.GetIncomingExternalIndices(m);
				for (int n = 0; n < incomingExternalIndices.Length; n++)
				{
					int incomingExternalIndex = incomingExternalIndices[n];
					if (_currentExternalInflow[incomingExternalIndex] != 0.0)
					{
						currentSourceActive = true;
						break;
					}
				}
				_currentSourceActive[m] = currentSourceActive;
			}
		}
		_currentRatesDirty = false;
	}

	internal void AccumulateCell(int cell, double duration)
	{
		if (!_transportTrackingEnabled || !_currentSourceActive[cell])
		{
			return;
		}
		_appliedNetVolume[cell] += duration * _currentNetFlow[cell];
		_appliedAdvectiveOutflowVolume[cell] += duration * _currentAdvectiveOutflow[cell];
		ReadOnlySpan<int> incomingExternalIndices = _snapshot.GetIncomingExternalIndices(cell);
		for (int i = 0; i < incomingExternalIndices.Length; i++)
		{
			int incomingExternalIndex = incomingExternalIndices[i];
			_appliedExternalInflowVolume[incomingExternalIndex] += duration * _currentExternalInflow[incomingExternalIndex];
		}
		ReadOnlySpan<int> outgoingTransferIndices = _snapshot.GetOutgoingTransferIndices(cell);
		for (int i = 0; i < outgoingTransferIndices.Length; i++)
		{
			int outgoingTransferIndex = outgoingTransferIndices[i];
			int scaledOutgoingTransferIndex = outgoingTransferIndex / 2;
			double currentTransferFlow = _currentTransferFlow[scaledOutgoingTransferIndex];
			if ((outgoingTransferIndex & 1) == 0)
			{
				if (currentTransferFlow > 0.0)
				{
					_appliedTransferForwardVolume[scaledOutgoingTransferIndex] += duration * currentTransferFlow;
				}
			}
			else if (currentTransferFlow < 0.0)
			{
				_appliedTransferReverseVolume[scaledOutgoingTransferIndex] -= duration * currentTransferFlow;
			}
		}
	}

	internal void AccumulateAll(double duration)
	{
		if (_transportTrackingEnabled && HasCellSources)
		{
			for (int i = 0; i < _cellCount; i++)
			{
				AccumulateCell(i, duration);
			}
		}
	}

	internal void CommitStep(double duration)
	{
		if (_transportTrackingEnabled && duration > 0.0)
		{
			double scaledDuration = 1.0 / duration;
			double[] writableNetCellFlow = _snapshot.WritableNetCellFlow;
			double[] writableAdvectiveOutflow = _snapshot.WritableAdvectiveOutflow;
			for (int i = 0; i < _cellCount; i++)
			{
				writableNetCellFlow[i] = _appliedNetVolume[i] * scaledDuration;
				writableAdvectiveOutflow[i] = _appliedAdvectiveOutflowVolume[i] * scaledDuration;
			}
			for (int j = 0; j < _snapshotExternalInflows.Length; j++)
			{
				CellWaterExternalInflow cellWaterExternalInflow = _snapshotExternalInflows[j];
				_snapshotExternalInflows[j] = new CellWaterExternalInflow(cellWaterExternalInflow.SourceId, cellWaterExternalInflow.Cell, _appliedExternalInflowVolume[j] * scaledDuration);
			}
			for (int k = 0; k < _transferStructures.Length; k++)
			{
				int transferUpstreamCellIndex = _transferUpstreamCells[k];
				int transferDownstreamCellIndex = _transferDownstreamCells[k];
				_snapshotTransfers[2 * k] = new CellWaterTransfer(transferUpstreamCellIndex, transferDownstreamCellIndex, _appliedTransferForwardVolume[k] * scaledDuration);
				_snapshotTransfers[2 * k + 1] = new CellWaterTransfer(transferDownstreamCellIndex, transferUpstreamCellIndex, _appliedTransferReverseVolume[k] * scaledDuration);
			}
			_snapshot.Duration = duration;
		}
	}

	private void PrepareExternalInflows()
	{
		BoundaryConditionCollection boundaryConditions = _solver.BoundaryConditions;
		List<CellWaterSourceInfo> cellWaterSourceInfos = new List<CellWaterSourceInfo>();
		List<CellWaterExternalInflow> cellWaterExternalInflows = new List<CellWaterExternalInflow>();
		_internalEntryIndices = Array.Empty<int[]>();
		_unclassifiedEntryByCell = new Dictionary<int, int>();
		if (boundaryConditions.HasPrecipitation)
		{
			_precipitationSourceId = AddSource(cellWaterSourceInfos, "Precipitation");
			boundaryConditions.Precipitation.WaterSourceId = _precipitationSourceId;
			_precipitationEntryOffset = cellWaterExternalInflows.Count;
			for (int i = 0; i < _cellCount; i++)
			{
				cellWaterExternalInflows.Add(new CellWaterExternalInflow(_precipitationSourceId, i, 0.0));
			}
		}
		int count = boundaryConditions.InternalFlowBoundaryConditions.Count;
		if (count > 0)
		{
			_positiveInternalFlow = new double[_cellCount];
			_negativeInternalFlowMagnitude = new double[_cellCount];
			_internalEntryIndices = new int[count][];
			for (int j = 0; j < count; j++)
			{
				BoundaryCondition boundaryCondition = boundaryConditions.InternalFlowBoundaryConditions[j];
				CellWaterSourceId sourceId = (boundaryCondition.WaterSourceId = AddSource(cellWaterSourceInfos, $"Internal Flow {j + 1}"));
				int[] cells = boundaryCondition.Cells;
				int[] internalEntryIndices = new int[cells.Length];
				for (int k = 0; k < cells.Length; k++)
				{
					ValidateSourceCell(cells[k], cellWaterSourceInfos[sourceId.Value].Name);
					internalEntryIndices[k] = cellWaterExternalInflows.Count;
					cellWaterExternalInflows.Add(new CellWaterExternalInflow(sourceId, cells[k], 0.0));
				}
				_internalEntryIndices[j] = internalEntryIndices;
			}
		}
		if (_solver.HydraulicStructures.HasSourceSinks)
		{
			_unclassifiedHydraulicSourceId = AddSource(cellWaterSourceInfos, "Unclassified Hydraulic Cell Source");
			foreach (int key in _solver.HydraulicStructures.SourceSinks.Keys)
			{
				ValidateSourceCell(key, cellWaterSourceInfos[_unclassifiedHydraulicSourceId.Value].Name);
				_unclassifiedEntryByCell.Add(key, cellWaterExternalInflows.Count);
				cellWaterExternalInflows.Add(new CellWaterExternalInflow(_unclassifiedHydraulicSourceId, key, 0.0));
			}
		}
		_sources = cellWaterSourceInfos.ToArray();
		_snapshotExternalInflows = cellWaterExternalInflows.ToArray();
		_currentExternalInflow = new double[cellWaterExternalInflows.Count];
		_appliedExternalInflowVolume = new double[cellWaterExternalInflows.Count];
	}

	private void RefreshInternalFlowTransportRates(BoundaryConditionCollection conditions)
	{
		Array.Clear(_positiveInternalFlow);
		Array.Clear(_negativeInternalFlowMagnitude);
		for (int i = 0; i < conditions.InternalFlowBoundaryConditions.Count; i++)
		{
			BoundaryCondition boundaryCondition = conditions.InternalFlowBoundaryConditions[i];
			double[] cellValuesState = boundaryCondition.CellValuesState;
			if (cellValuesState == null)
			{
				continue;
			}
			for (int j = 0; j < boundaryCondition.Cells.Length; j++)
			{
				int cellIndex = boundaryCondition.Cells[j];
				double cellValuesState3 = cellValuesState[j];
				if (cellValuesState3 >= 0.0)
				{
					_positiveInternalFlow[cellIndex] += cellValuesState3;
				}
				else
				{
					_negativeInternalFlowMagnitude[cellIndex] -= cellValuesState3;
				}
			}
		}
		for (int k = 0; k < conditions.InternalFlowBoundaryConditions.Count; k++)
		{
			BoundaryCondition boundaryCondition2 = conditions.InternalFlowBoundaryConditions[k];
			double[] cellValuesState2 = boundaryCondition2.CellValuesState;
			if (cellValuesState2 == null)
			{
				continue;
			}
			int[] internalEntryIndex = _internalEntryIndices[k];
			for (int l = 0; l < boundaryCondition2.Cells.Length; l++)
			{
				int cellIndex2 = boundaryCondition2.Cells[l];
				double cellValuesState22 = cellValuesState2[l];
				if (cellValuesState22 >= 0.0)
				{
					_currentExternalInflow[internalEntryIndex[l]] = cellValuesState22;
					continue;
				}
				double negativeInternalFlowMagnitude = _negativeInternalFlowMagnitude[cellIndex2];
				double positiveInternalFlowCellIndex2Term = _positiveInternalFlow[cellIndex2] - conditions.CellInternalFlowLimited[cellIndex2];
				double negativeInternalFlowMagnitudeTerm = ((negativeInternalFlowMagnitude > 0.0) ? Math.Clamp(positiveInternalFlowCellIndex2Term / negativeInternalFlowMagnitude, 0.0, 1.0) : 0.0);
				_currentAdvectiveOutflow[cellIndex2] -= cellValuesState22 * negativeInternalFlowMagnitudeTerm;
			}
		}
	}

	private static CellWaterSourceId AddSource(List<CellWaterSourceInfo> sources, string name)
	{
		CellWaterSourceId cellWaterSourceId = new CellWaterSourceId(sources.Count);
		sources.Add(new CellWaterSourceInfo(cellWaterSourceId, name));
		return cellWaterSourceId;
	}

	private void BuildActiveNetSourceCells(BoundaryConditionCollection conditions)
	{
		if (conditions.HasLandSurface)
		{
			return;
		}
		if (conditions.HasInternalFlows)
		{
			int[] cellIndicesInternalFlows = conditions.CellIndicesInternalFlows;
			foreach (int cellIndicesInternalFlow in cellIndicesInternalFlows)
			{
				if (_currentNetFlow[cellIndicesInternalFlow] != 0.0)
				{
					_activeNetSourceCells[_activeNetSourceCellCount++] = cellIndicesInternalFlow;
				}
			}
		}
		foreach (int key in _solver.HydraulicStructures.SourceSinks.Keys)
		{
			if ((!conditions.HasInternalFlows || conditions.InternalFlowIndices[key].Count == 0) && _currentNetFlow[key] != 0.0)
			{
				_activeNetSourceCells[_activeNetSourceCellCount++] = key;
			}
		}
	}

	private void PrepareTransfers()
	{
		List<HydraulicStructure> hydraulicStructures = new List<HydraulicStructure>();
		foreach (HydraulicStructure allHydraulicStructure in _solver.HydraulicStructures.AllHydraulicStructures)
		{
			if (allHydraulicStructure.TryGetCellWaterTransferCells(out var _, out var _))
			{
				hydraulicStructures.Add(allHydraulicStructure);
			}
		}
		_transferStructures = hydraulicStructures.ToArray();
		int transferStructureLength = _transferStructures.Length;
		_currentTransferFlow = new double[transferStructureLength];
		_appliedTransferForwardVolume = new double[transferStructureLength];
		_appliedTransferReverseVolume = new double[transferStructureLength];
		_transferUpstreamCells = new int[transferStructureLength];
		_transferDownstreamCells = new int[transferStructureLength];
		_snapshotTransfers = new CellWaterTransfer[2 * transferStructureLength];
		for (int i = 0; i < transferStructureLength; i++)
		{
			HydraulicStructure hydraulicStructure = _transferStructures[i];
			if (!hydraulicStructure.TryGetCellWaterTransferCells(out var upstreamCell2, out var downstreamCell2))
			{
				throw new SolverException("Cell-water transfer topology is unavailable for structure " + hydraulicStructure.Name + ".");
			}
			ValidateTransferCell(upstreamCell2, hydraulicStructure);
			ValidateTransferCell(downstreamCell2, hydraulicStructure);
			_transferUpstreamCells[i] = upstreamCell2;
			_transferDownstreamCells[i] = downstreamCell2;
			_snapshotTransfers[2 * i] = new CellWaterTransfer(upstreamCell2, downstreamCell2, 0.0);
			_snapshotTransfers[2 * i + 1] = new CellWaterTransfer(downstreamCell2, upstreamCell2, 0.0);
		}
	}

	private void ValidateTransferCell(int cell, HydraulicStructure structure)
	{
		if ((uint)cell >= (uint)_cellCount)
		{
			throw new SolverException($"Structure {structure.Name} returned invalid cell-water transfer cell {cell}.");
		}
	}

	private void ValidateSourceCell(int cell, string sourceName)
	{
		if ((uint)cell >= (uint)_cellCount)
		{
			throw new SolverException($"Cell-water source {sourceName} returned invalid cell {cell}.");
		}
	}
}
