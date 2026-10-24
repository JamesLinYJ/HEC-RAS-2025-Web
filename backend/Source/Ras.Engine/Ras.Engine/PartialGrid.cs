#define TRACE
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine.Structures;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Engine;

public class PartialGrid
{
	public enum Ticker
	{
		PARTIALGRID_PREPARE,
		PARTIALGRID_UPDATE,
		PARTIALGRID_MARKWETCELLS,
		PARTIALGRID_BUFFER,
		PARTIALGRID_CELLMAP,
		PARTIALGRID_FACEMAP,
		PARTIALGRID_CHECK,
		PARTIALGRID_BLOCKORDER
	}

	private const float InactiveElev = -1E+30f;

	private int _cellCountOld = -1;

	private int _wetCountOld = -1;

	private readonly int _maxCellCountPartial;

	private readonly int _maxFaceCountPartial;

	private List<int> _faceIndicesFull;

	private List<int> _cellIndicesFull;

	private int _activeCellCount;

	private List<int> _cellIndicesAdded;

	private List<int> _faceIndicesAdded;

	private List<int> _openBoundaryFaces;

	private int _cellsRemovedThisStep;

	private bool[] _cellRemovedFlag;

	private List<int> _cellsFlaggedThisStep;

	private bool[] _faceAddedFlag;

	private int[] _permutation;

	private int[] _blockOffsets;

	private int[] _priorCellRegionId;

	private int _priorIdHighWatermark;

	private int[] _activeDegree;

	private bool[] _visited;

	private int[] _bfsQueue;

	private int[] _regionStart;

	private int[] _regionSize;

	private int[] _regionClaimedPriorId;

	private int[] _regionCells;

	private ThreadLocal<int[]> _bfsQueueLocal;

	private ThreadLocal<int[]> _neighborScratchLocal;

	private ThreadLocal<int[]> _globalToLocalLocal;

	private int[] _regionFinalId;

	private readonly Dictionary<int, int> _priorIdVoteCount = new Dictionary<int, int>();

	private readonly List<int> _freeIdPool = new List<int>();

	private int[] _regionSortBuffer;

	private readonly HashSet<int> _keptThisStepIds = new HashSet<int>();

	private readonly List<int> _priorIssuedIds = new List<int>();

	private float[] _cellElevBuffer;

	private List<int> _cellIndicesBufferFront;

	private List<int> _cellIndicesBufferFrontNext;

	protected Mesh _mesh;

	protected BoundaryConditionCollection _bcc;

	private bool[] _faceOpenBC;

	private int[] _rowPointers;

	private int[] _columnIndices;

	private int[] _stencilFaces;

	private readonly ProgressReporter _reporter;

	private readonly bool _runCheck;

	public long[] Ticks;

	public bool Enabled { get; private set; }

	public bool Changed { get; set; } = true;

	public double ActivePercent => 100.0 * (double)_activeCellCount / (double)_mesh.CellCount;

	public int WetCount { get; private set; }

	public int CellStart { get; private set; }

	public int CellStop { get; private set; }

	public double CellRangePercent => 100.0 * (double)(CellStop - CellStart + 1) / (double)_mesh.CellCount;

	public int FaceStart { get; private set; }

	public int FaceStop { get; private set; }

	public double FaceRangePercent => 100.0 * (double)(FaceStop - FaceStart + 1) / (double)_mesh.FaceCount;

	public int FaceCount
	{
		get
		{
			if (!Enabled)
			{
				return _mesh.FaceCount;
			}
			return FaceIndicesPartial.Count;
		}
	}

	public int CellCount
	{
		get
		{
			if (!Enabled)
			{
				return _mesh.CellCount;
			}
			return CellIndicesPartial.Count;
		}
	}

	public List<int> FaceIndices
	{
		get
		{
			if (!Enabled)
			{
				return _faceIndicesFull;
			}
			return FaceIndicesPartial;
		}
	}

	public List<int> CellIndices
	{
		get
		{
			if (!Enabled)
			{
				return _cellIndicesFull;
			}
			return CellIndicesPartial;
		}
	}

	public bool[] CellActive { get; private set; }

	public List<int> CellIndicesPartial { get; private set; }

	public int[] CellToPartialCell { get; private set; }

	public List<int> CellIndicesRemoved { get; private set; }

	public List<int> FaceIndicesPartial { get; private set; }

	public int[] FaceToPartialFace { get; private set; }

	public List<int> FaceIndicesRemoved { get; private set; }

	public int[] Permutation => _permutation;

	public int[] BlockOffsets => _blockOffsets;

	public int RegionCount { get; private set; }

	public int[] CellRegionId { get; private set; }

	public int[] RegionIds => _regionFinalId;

	public PartialGrid(Mesh mesh, BoundaryConditionCollection bcc, bool usePartialGrid, double tolerancePartial, ProgressReporter reporter = null, bool RunCheck = false)
	{
		_mesh = mesh;
		_bcc = bcc;
		_maxCellCountPartial = (int)(tolerancePartial * (double)_mesh.CellCount / 100.0);
		_maxFaceCountPartial = (int)(1.2 * tolerancePartial * (double)_mesh.FaceCount / 100.0);
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		_reporter = reporter;
		_runCheck = RunCheck;
		Ticks = new long[Enum.GetValues<Ticker>().Length];
		Prepare(usePartialGrid);
	}

	private void Prepare(bool usePartialGrid)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int wetCount = (_activeCellCount = _mesh.CellCount);
		CellActive = new bool[wetCount];
		_cellIndicesFull = new List<int>(wetCount);
		CellIndicesPartial = new List<int>(_maxCellCountPartial);
		for (int i = 0; i < wetCount; i++)
		{
			CellActive[i] = true;
			_cellIndicesFull.Add(i);
		}
		int faceCount = _mesh.FaceCount;
		FaceIndicesPartial = new List<int>(_maxFaceCountPartial);
		_faceIndicesFull = new List<int>(faceCount);
		for (int j = 0; j < faceCount; j++)
		{
			FaceIndicesPartial.Add(j);
			_faceIndicesFull.Add(j);
		}
		WetCount = wetCount;
		CellStart = 0;
		CellStop = wetCount - 1;
		FaceStart = 0;
		FaceStop = faceCount - 1;
		CellRegionId = new int[wetCount];
		_priorCellRegionId = new int[wetCount];
		for (int k = 0; k < wetCount; k++)
		{
			CellRegionId[k] = -1;
			_priorCellRegionId[k] = -1;
		}
		_priorIdHighWatermark = -1;
		RegionCount = 0;
		if (!usePartialGrid)
		{
			return;
		}
		_mesh.ComputeCellAdjacencyMatrix(out _rowPointers, out _columnIndices, addDiagonal: false);
		int columnIndexLength = _columnIndices.Length;
		_stencilFaces = new int[columnIndexLength];
		for (int l = 0; l < _mesh.CellCount; l++)
		{
			int rowPointerStart = _rowPointers[l];
			int rowPointerEnd = _rowPointers[l + 1];
			for (int m = rowPointerStart; m < rowPointerEnd; m++)
			{
				int cB = _columnIndices[m];
				_stencilFaces[m] = _mesh.GetFaceBetweenCells(l, cB);
			}
		}
		_cellElevBuffer = new float[wetCount];
		int scaledWetCount = Math.Min(256, wetCount / 10);
		int capacity = 2 * scaledWetCount;
		_cellIndicesBufferFront = new List<int>(scaledWetCount);
		_cellIndicesBufferFrontNext = new List<int>(scaledWetCount);
		CellToPartialCell = new int[wetCount];
		_cellIndicesAdded = new List<int>(scaledWetCount);
		CellIndicesRemoved = new List<int>(scaledWetCount);
		_cellRemovedFlag = new bool[wetCount];
		_cellsFlaggedThisStep = new List<int>(scaledWetCount);
		for (int n = 0; n < wetCount; n++)
		{
			CellToPartialCell[n] = -1;
		}
		_faceIndicesAdded = new List<int>(capacity);
		FaceIndicesRemoved = new List<int>(capacity);
		FaceToPartialFace = new int[faceCount];
		_faceOpenBC = new bool[faceCount];
		_openBoundaryFaces = new List<int>();
		_faceAddedFlag = new bool[faceCount];
		for (int intervalIndex = 0; intervalIndex < faceCount; intervalIndex++)
		{
			FaceToPartialFace[intervalIndex] = -1;
			_faceOpenBC[intervalIndex] = false;
		}
		foreach (BoundaryCondition flowBoundaryCondition in _bcc.FlowBoundaryConditions)
		{
			SetFaceOpenBC(flowBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition normalDepthBoundaryCondition in _bcc.NormalDepthBoundaryConditions)
		{
			SetFaceOpenBC(normalDepthBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition stageBoundaryCondition in _bcc.StageBoundaryConditions)
		{
			SetFaceOpenBC(stageBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition ratingCurveBoundaryCondition in _bcc.RatingCurveBoundaryConditions)
		{
			SetFaceOpenBC(ratingCurveBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition variableFlowBoundaryCondition in _bcc.VariableFlowBoundaryConditions)
		{
			SetFaceOpenBC(variableFlowBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition variableStageBoundaryCondition in _bcc.VariableStageBoundaryConditions)
		{
			SetFaceOpenBC(variableStageBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition spatiallyVariableStageVelocityBoundaryCondition in _bcc.SpatiallyVariableStageVelocityBoundaryConditions)
		{
			SetFaceOpenBC(spatiallyVariableStageVelocityBoundaryCondition.Faces);
		}
		_activeDegree = new int[wetCount];
		_visited = new bool[wetCount];
		_bfsQueue = new int[Math.Max(1, _maxCellCountPartial)];
		_permutation = new int[Math.Max(1, _maxCellCountPartial)];
		_blockOffsets = new int[Math.Max(2, _maxCellCountPartial + 1)];
		_regionStart = new int[Math.Max(1, _maxCellCountPartial)];
		_regionSize = new int[Math.Max(1, _maxCellCountPartial)];
		_regionClaimedPriorId = new int[Math.Max(1, _maxCellCountPartial)];
		_regionFinalId = new int[Math.Max(1, _maxCellCountPartial)];
		_regionSortBuffer = new int[Math.Max(1, _maxCellCountPartial)];
		_regionCells = new int[Math.Max(1, _maxCellCountPartial)];
		int rowPointerStep = 0;
		for (int intervalIndex2 = 0; intervalIndex2 < wetCount; intervalIndex2++)
		{
			int rowPointerStep2 = _rowPointers[intervalIndex2 + 1] - _rowPointers[intervalIndex2];
			if (rowPointerStep2 > rowPointerStep)
			{
				rowPointerStep = rowPointerStep2;
			}
		}
		int maxRowDegLocal = Math.Max(1, rowPointerStep);
		_bfsQueueLocal = new ThreadLocal<int[]>(() => new int[64]);
		_neighborScratchLocal = new ThreadLocal<int[]>(() => new int[maxRowDegLocal]);
		int meshCellCountLocal = wetCount;
		_globalToLocalLocal = new ThreadLocal<int[]>(() =>
		{
			int[] intValues = new int[meshCellCountLocal];
			Array.Fill(intValues, -1);
			return intValues;
		});
		Ticks[0] += stopwatch.ElapsedTicks;
		void SetFaceOpenBC(int[] faces)
		{
			foreach (int faceIndex in faces)
			{
				if (!_faceOpenBC[faceIndex])
				{
					_faceOpenBC[faceIndex] = true;
					_openBoundaryFaces.Add(faceIndex);
				}
			}
		}
	}

	public bool Update(MeshWaterState ws, MeshPropertyTables props, BoundaryConditionCollection bcc, HydraulicStructureCollection hsc, int countBuffer, double maxElevChg, bool buildBlockOrdering = false)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool result = true;
		if (bcc.HasPrecipitation)
		{
			SetPartialGrid(isEnabled: false);
			RegionCount = 0;
			return result;
		}
		_cellCountOld = CellCount;
		_wetCountOld = WetCount;
		_cellIndicesAdded.Clear();
		ReadOnlySpan<int> faceIndicesAdded = CollectionsMarshal.AsSpan(_faceIndicesAdded);
		for (int i = 0; i < faceIndicesAdded.Length; i++)
		{
			_faceAddedFlag[faceIndicesAdded[i]] = false;
		}
		_faceIndicesAdded.Clear();
		_cellsRemovedThisStep = 0;
		ReadOnlySpan<int> cellsFlaggedThisStep = CollectionsMarshal.AsSpan(_cellsFlaggedThisStep);
		for (int j = 0; j < cellsFlaggedThisStep.Length; j++)
		{
			_cellRemovedFlag[cellsFlaggedThisStep[j]] = false;
		}
		_cellsFlaggedThisStep.Clear();
		ContractActiveCellsAndInitBufferFront(ws);
		AddCellsFromBoundaryConditions(ws, props, bcc, maxElevChg);
		MarkHydraulicStructureCells(ws, hsc);
		if (_activeCellCount == 0)
		{
			SetPartialGrid(isEnabled: true);
			if (buildBlockOrdering)
			{
				BuildBlockOrdering(ws, props, (float)maxElevChg);
			}
			else
			{
				RegionCount = 0;
			}
			return result;
		}
		if (_activeCellCount > _maxCellCountPartial)
		{
			SetPartialGrid(isEnabled: false);
			RegionCount = 0;
			return result;
		}
		ExpandBufferFront(ws, props, countBuffer, (float)maxElevChg);
		if (_activeCellCount > _maxCellCountPartial)
		{
			SetPartialGrid(isEnabled: false);
			RegionCount = 0;
			return result;
		}
		SetPartialGrid(isEnabled: true);
		bool check = true;
		if (_runCheck)
		{
			check = Check(ws, props);
			if (!check)
			{
				_reporter.ReportMessage("Partial grid update failed. Disabling partial grid solution.");
				SetPartialGrid(isEnabled: false);
			}
		}
		if (buildBlockOrdering && Enabled && Changed)
		{
			BuildBlockOrdering(ws, props, (float)maxElevChg);
		}
		else if (!buildBlockOrdering)
		{
			RegionCount = 0;
		}
		Ticks[1] += stopwatch.ElapsedTicks;
		return check;
	}

	private void SetPartialGrid(bool isEnabled)
	{
		if (isEnabled)
		{
			if (Enabled && _cellIndicesAdded.Count == 0 && _cellsRemovedThisStep == 0 && WetCount == _wetCountOld)
			{
				CellIndicesRemoved.Clear();
				FaceIndicesRemoved.Clear();
				Changed = false;
				return;
			}
			if (!BuildPartialCellMappings())
			{
				Enabled = false;
				Changed = false;
				return;
			}
			Changed = true;
			if (!BuildPartialFaceMappings())
			{
				Enabled = false;
				Changed = false;
			}
			else
			{
				Enabled = true;
			}
		}
		else
		{
			CellIndicesRemoved.Clear();
			FaceIndicesRemoved.Clear();
			Changed = Enabled;
			Enabled = false;
		}
	}

	private void ContractActiveCellsAndInitBufferFront(MeshWaterState ws)
	{
		ReadOnlySpan<double> cellElevation = ws.CellElevation.AsSpan();
		Span<float> cellElevBuffer = _cellElevBuffer.AsSpan();
		ReadOnlySpan<double> cellVolume = ws.CellVolume.AsSpan();
		Span<bool> cellActive = CellActive.AsSpan();
		ReadOnlySpan<int> rowPointers = _rowPointers.AsSpan();
		ReadOnlySpan<int> columnIndices = _columnIndices.AsSpan();
		ReadOnlySpan<int> cellIndices = CellIndices.AsSpan();
		_activeCellCount = 0;
		_cellIndicesBufferFront.Clear();
		for (int i = 0; i < _cellCountOld; i++)
		{
			int cellIndex = cellIndices[i];
			if (cellVolume[cellIndex] > 0.0)
			{
				cellElevBuffer[cellIndex] = (float)cellElevation[cellIndex];
				cellActive[cellIndex] = true;
				_activeCellCount++;
				int rowPointerStart = rowPointers[cellIndex];
				int rowPointerEnd = rowPointers[cellIndex + 1];
				for (int j = rowPointerStart; j < rowPointerEnd; j++)
				{
					int index = columnIndices[j];
					if (cellVolume[index] == 0.0)
					{
						_cellIndicesBufferFront.Add(cellIndex);
						break;
					}
				}
			}
			else
			{
				cellElevBuffer[cellIndex] = -1E+30f;
				cellActive[cellIndex] = false;
				_cellRemovedFlag[cellIndex] = true;
				_cellsFlaggedThisStep.Add(cellIndex);
				_cellsRemovedThisStep++;
			}
		}
		WetCount = _activeCellCount;
	}

	private void AddCellsFromBoundaryConditions(MeshWaterState ws, MeshPropertyTables props, BoundaryConditionCollection bcc, double maxElevChg)
	{
		float[] faceMinElev = props.FaceMinimumElevations;
		double[] cellElevations = ws.CellElevation;
		Face[] faces = _mesh.Faces;
		foreach (BoundaryCondition stageBoundaryCondition in bcc.StageBoundaryConditions)
		{
			AddCellsFromBoundaryStages(stageBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition variableStageBoundaryCondition in bcc.VariableStageBoundaryConditions)
		{
			AddCellsFromBoundaryStages(variableStageBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition spatiallyVariableStageVelocityBoundaryCondition in bcc.SpatiallyVariableStageVelocityBoundaryConditions)
		{
			AddCellsFromBoundaryStages(spatiallyVariableStageVelocityBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition flowBoundaryCondition in bcc.FlowBoundaryConditions)
		{
			AddCellsFromBoundaryFlows(flowBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition variableFlowBoundaryCondition in bcc.VariableFlowBoundaryConditions)
		{
			AddCellsFromBoundaryFlows(variableFlowBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition normalDepthBoundaryCondition in bcc.NormalDepthBoundaryConditions)
		{
			AddCellsFromBoundaryFlows(normalDepthBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition ratingCurveBoundaryCondition in bcc.RatingCurveBoundaryConditions)
		{
			AddCellsFromBoundaryFlows(ratingCurveBoundaryCondition.Faces);
		}
		foreach (BoundaryCondition internalFlowBoundaryCondition in bcc.InternalFlowBoundaryConditions)
		{
			AddCellsFromInternalBoundary(internalFlowBoundaryCondition.Cells);
		}
		void AddCellFromBoundaryFace(int fIdx)
		{
			int cellA = faces[fIdx].cellA;
			if (!CellActive[cellA])
			{
				AddCellToActiveAndBufferFront(cellA, cellElevations);
			}
		}
		void AddCellsFromBoundaryFlows(int[] fIdxs)
		{
			foreach (int fIdx2 in fIdxs)
			{
				if (Math.Abs(ws.FaceFlow[fIdx2]) > 0.0)
				{
					AddCellFromBoundaryFace(fIdx2);
				}
			}
		}
		void AddCellsFromBoundaryStages(int[] fIdxs)
		{
			foreach (int fIdx3 in fIdxs)
			{
				if (ws.PinnedFaceStage[fIdx3] + maxElevChg > (double)faceMinElev[fIdx3])
				{
					AddCellFromBoundaryFace(fIdx3);
				}
			}
		}
		void AddCellsFromInternalBoundary(int[] cIdxs)
		{
			foreach (int cIdx in cIdxs)
			{
				bcc.TryGetInternalFlow(cIdx, out var flow);
				if (Math.Abs(flow) > 0.0 && !CellActive[cIdx])
				{
					AddCellToActiveAndBufferFront(cIdx, cellElevations);
				}
			}
		}
	}

	private void MarkHydraulicStructureCells(MeshWaterState ws, HydraulicStructureCollection hsc)
	{
		double[] cellElevations = ws.CellElevation;
		foreach (HydraulicStructure allHydraulicStructure in hsc.AllHydraulicStructures)
		{
			AddHydraulicStructureCells(allHydraulicStructure.UpCells);
			AddHydraulicStructureCells(allHydraulicStructure.DnCells);
		}
		void AddHydraulicStructureCells(int[] cIdxs)
		{
			foreach (int cIdx in cIdxs)
			{
				if (!CellActive[cIdx])
				{
					AddCellToActiveAndBufferFront(cIdx, cellElevations);
				}
			}
		}
	}

	private void AddCellToActiveAndBufferFront(int cIdx, double[] cellElevations)
	{
		CellActive[cIdx] = true;
		_activeCellCount++;
		_cellElevBuffer[cIdx] = (float)cellElevations[cIdx];
		_cellIndicesBufferFront.Add(cIdx);
		bool cellRemovedFlag = _cellRemovedFlag[cIdx];
		if (cellRemovedFlag)
		{
			_cellRemovedFlag[cIdx] = false;
			_cellsRemovedThisStep--;
		}
		if (Enabled && !cellRemovedFlag && CellToPartialCell[cIdx] < 0)
		{
			_cellIndicesAdded.Add(cIdx);
			TrackFacesForAddedCell(cIdx);
		}
	}

	private void TrackFacesForAddedCell(int cIdx)
	{
		int rowPointerStart = _rowPointers[cIdx];
		int rowPointerEnd = _rowPointers[cIdx + 1];
		for (int i = rowPointerStart; i < rowPointerEnd; i++)
		{
			TrackFaceCandidate(_stencilFaces[i]);
		}
		ReadOnlySpan<int> openBoundaryFaces = CollectionsMarshal.AsSpan(_openBoundaryFaces);
		for (int j = 0; j < openBoundaryFaces.Length; j++)
		{
			int openBoundaryFaceIndex = openBoundaryFaces[j];
			if (_mesh.Faces[openBoundaryFaceIndex].cellA == cIdx)
			{
				TrackFaceCandidate(openBoundaryFaceIndex);
			}
		}
	}

	private void TrackFaceCandidate(int fIdx)
	{
		if (FaceToPartialFace[fIdx] < 0 && !_faceAddedFlag[fIdx])
		{
			_faceAddedFlag[fIdx] = true;
			_faceIndicesAdded.Add(fIdx);
		}
	}

	private void ExpandBufferFront(MeshWaterState ws, MeshPropertyTables props, int countBuffer, float maxElevChg)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		for (int i = 0; i < countBuffer; i++)
		{
			ExpandBufferFrontIteration(ws, props, i, maxElevChg);
			if (_cellIndicesBufferFrontNext.Count == 0)
			{
				break;
			}
		}
		ReadOnlySpan<int> faceIndicesAdded = CollectionsMarshal.AsSpan(_faceIndicesAdded);
		for (int j = 0; j < faceIndicesAdded.Length; j++)
		{
			_faceAddedFlag[faceIndicesAdded[j]] = false;
		}
		Ticks[3] += stopwatch.ElapsedTicks;
	}

	private void ExpandBufferFrontIteration(MeshWaterState ws, MeshPropertyTables props, int iteration, float maxElevChg)
	{
		if (iteration > 0)
		{
			List<int> cellIndicesBufferFrontNext = _cellIndicesBufferFrontNext;
			List<int> cellIndicesBufferFront = _cellIndicesBufferFront;
			_cellIndicesBufferFront = cellIndicesBufferFrontNext;
			_cellIndicesBufferFrontNext = cellIndicesBufferFront;
		}
		float[] faceMinimumElevations = props.FaceMinimumElevations;
		double[] cellVolume = ws.CellVolume;
		double[] faceArea = ws.FaceArea;
		_cellIndicesBufferFrontNext.Clear();
		int count = _cellIndicesBufferFront.Count;
		for (int i = 0; i < count; i++)
		{
			int cIdx = _cellIndicesBufferFront[i];
			ExpandBufferFrontAtCell(cIdx, cellVolume, faceArea, faceMinimumElevations, maxElevChg);
		}
	}

	private void ExpandBufferFrontAtCell(int cIdx, double[] cellVolume, double[] faceArea, float[] faceMinimumElevations, float maxElevChg)
	{
		int rowPointerStart = _rowPointers[cIdx];
		int rowPointerEnd = _rowPointers[cIdx + 1];
		for (int i = rowPointerStart; i < rowPointerEnd; i++)
		{
			int columnIndex = _columnIndices[i];
			if (!(cellVolume[columnIndex] > 0.0))
			{
				int fIdx = _stencilFaces[i];
				ExpandBufferFrontAtFace(cIdx, columnIndex, fIdx, faceArea, faceMinimumElevations, maxElevChg);
			}
		}
	}

	private void ExpandBufferFrontAtFace(int cIdx, int cNbr, int fIdx, double[] faceArea, float[] faceMinimumElevations, float maxElevChg)
	{
		float cellElevBuffer = _cellElevBuffer[cIdx];
		if (!BufferedEdge(faceArea[fIdx], cellElevBuffer, faceMinimumElevations[fIdx], maxElevChg))
		{
			return;
		}
		_cellElevBuffer[cNbr] = Math.Max(_cellElevBuffer[cNbr], cellElevBuffer);
		if (!CellActive[cNbr])
		{
			CellActive[cNbr] = true;
			_activeCellCount++;
			_cellIndicesBufferFrontNext.Add(cNbr);
			bool cellRemovedFlag = _cellRemovedFlag[cNbr];
			if (cellRemovedFlag)
			{
				_cellRemovedFlag[cNbr] = false;
				_cellsRemovedThisStep--;
			}
			if (Enabled && !cellRemovedFlag && CellToPartialCell[cNbr] < 0)
			{
				_cellIndicesAdded.Add(cNbr);
				TrackFacesForAddedCell(cNbr);
			}
		}
	}

	private bool BuildPartialCellMappings()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool result = true;
		int cellCount = _mesh.CellCount;
		(int, int) tuple = FindActiveStartStop(CellIndicesPartial, _cellIndicesAdded, cellCount);
		CellStart = tuple.Item1;
		CellStop = tuple.Item2;
		ReadOnlySpan<bool> cellActive = CellActive.AsSpan();
		Span<int> cellToPartialCell = CellToPartialCell.AsSpan();
		CellIndicesPartial.Clear();
		CellIndicesRemoved.Clear();
		bool enabled = Enabled;
		for (int i = CellStart; i <= CellStop; i++)
		{
			if (cellActive[i])
			{
				cellToPartialCell[i] = CellIndicesPartial.Count;
				CellIndicesPartial.Add(i);
			}
			else if (!enabled || cellToPartialCell[i] >= 0)
			{
				CellIndicesRemoved.Add(i);
				cellToPartialCell[i] = -1;
			}
		}
		if (CellIndicesPartial.Count + CellIndicesRemoved.Count - _cellIndicesAdded.Count - _cellCountOld != 0)
		{
			_reporter.ReportMessage("Problem building partial grid cell mapping");
			_reporter.ReportMessage(" Turning partial grid off.");
			result = false;
		}
		Ticks[4] += stopwatch.ElapsedTicks;
		return result;
	}

	private bool BuildPartialFaceMappings()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool result = true;
		Span<Face> faces = _mesh.Faces.AsSpan();
		int faceCount = _mesh.FaceCount;
		(int, int) tuple = FindActiveStartStop(FaceIndicesPartial, _faceIndicesAdded, faceCount);
		FaceStart = tuple.Item1;
		FaceStop = tuple.Item2;
		ReadOnlySpan<bool> cellActive = CellActive.AsSpan();
		ReadOnlySpan<bool> faceOpenBC = _faceOpenBC;
		Span<int> faceToPartialFace = FaceToPartialFace.AsSpan();
		bool enabled = Enabled;
		FaceIndicesPartial.Clear();
		FaceIndicesRemoved.Clear();
		int faceCountFaceStopTerm = faceCount - (FaceStop - FaceStart + 1);
		for (int i = FaceStart; i <= FaceStop; i++)
		{
			Face face = faces[i];
			int cellA = face.cellA;
			int cellB = face.cellB;
			if ((faceOpenBC[i] && cellActive[cellA]) || (cellActive[cellA] && cellB >= 0 && cellActive[cellB]))
			{
				faceToPartialFace[i] = FaceIndicesPartial.Count;
				FaceIndicesPartial.Add(i);
			}
			else if (!enabled || faceToPartialFace[i] >= 0)
			{
				FaceIndicesRemoved.Add(i);
				faceToPartialFace[i] = -1;
			}
			else
			{
				faceCountFaceStopTerm++;
			}
		}
		if (FaceIndicesPartial.Count + FaceIndicesRemoved.Count + faceCountFaceStopTerm - faceCount != 0)
		{
			_reporter.ReportMessage("Problem building partial grid face mapping");
			_reporter.ReportMessage(" Turning partial grid off.");
			result = false;
		}
		Ticks[5] += stopwatch.ElapsedTicks;
		return result;
	}

	private (int startIndex, int stopIndex) FindActiveStartStop(List<int> indicesPartial, List<int> indicesAdded, int count)
	{
		if (!Enabled)
		{
			return (startIndex: 0, stopIndex: count - 1);
		}
		int indicesPartial2 = indicesPartial[0];
		int indicesPartial3 = indicesPartial[indicesPartial.Count - 1];
		if (indicesAdded.Count > 0)
		{
			int count2 = count;
			int indicesAdded2Term = -1;
			ReadOnlySpan<int> indicesAddedSpan = indicesAdded.AsSpan();
			for (int i = 0; i < indicesAdded.Count; i++)
			{
				int indicesAdded2 = indicesAddedSpan[i];
				if (indicesAdded2 < count2)
				{
					count2 = indicesAdded2;
				}
				if (indicesAdded2 > indicesAdded2Term)
				{
					indicesAdded2Term = indicesAdded2;
				}
			}
			indicesPartial2 = Math.Min(indicesPartial2, count2);
			indicesPartial3 = Math.Max(indicesPartial3, indicesAdded2Term);
		}
		return (startIndex: indicesPartial2, stopIndex: indicesPartial3);
	}

	private bool Check(MeshWaterState ws, MeshPropertyTables props)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int faceCount = _mesh.FaceCount;
		Span<int> faceToPartialFace = FaceToPartialFace.AsSpan();
		Span<double> faceArea = ws.FaceArea.AsSpan();
		for (int i = 0; i < faceCount; i++)
		{
			if (faceToPartialFace[i] < 0 && faceArea[i] > 0.0)
			{
				int cellB = _mesh.Faces[i].cellB;
				if (cellB >= 0)
				{
					int cellA = _mesh.Faces[i].cellA;
					double cellElevation = ws.CellElevation[cellA];
					double cellElevation2 = ws.CellElevation[cellB];
					double faceElevation = ws.FaceElevation[i];
					float faceMinimumElevation = props.FaceMinimumElevations[i];
					_reporter.ReportMessage("Problem with partial grid solution at face ", i.ToString().InSquareBrackets(), ". ");
					_reporter.ReportMessage("  Cell A Water Level: ", cellElevation.ToString(), ",  Cell B Water Level: ", cellElevation2.ToString());
					_reporter.ReportMessage("  Face Water Level: ", faceElevation.ToString(), ",  Face Invert: ", faceMinimumElevation.ToString());
					return false;
				}
			}
		}
		Ticks[6] += stopwatch.ElapsedTicks;
		return true;
	}

	private static bool BufferedEdge(double fArea, float cWSE, float fMinZ, float maxElevChg)
	{
		if (!(fArea > 0.0))
		{
			return cWSE + maxElevChg > fMinZ;
		}
		return true;
	}

	private void BuildBlockOrdering(MeshWaterState ws, MeshPropertyTables props, float maxElevChg)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int activeCellCount = _activeCellCount;
		Span<int> cellRegionId = CellRegionId.AsSpan();
		ReadOnlySpan<int> cellIndicesRemoved = CollectionsMarshal.AsSpan(CellIndicesRemoved);
		for (int i = 0; i < cellIndicesRemoved.Length; i++)
		{
			cellRegionId[cellIndicesRemoved[i]] = -1;
		}
		if (activeCellCount == 0)
		{
			RegionCount = 0;
			_blockOffsets[0] = 0;
			ReadOnlySpan<int> cellIndicesRemovedSpan = CollectionsMarshal.AsSpan(CellIndicesRemoved);
			for (int j = 0; j < cellIndicesRemovedSpan.Length; j++)
			{
				_priorCellRegionId[cellIndicesRemovedSpan[j]] = -1;
			}
			_priorIssuedIds.Clear();
			Ticks[7] += stopwatch.ElapsedTicks;
			return;
		}
		EnsureCapacity(ref _permutation, activeCellCount);
		EnsureCapacity(ref _blockOffsets, activeCellCount + 1);
		EnsureCapacity(ref _regionStart, activeCellCount);
		EnsureCapacity(ref _regionSize, activeCellCount);
		EnsureCapacity(ref _regionClaimedPriorId, activeCellCount);
		EnsureCapacity(ref _regionFinalId, activeCellCount);
		EnsureCapacity(ref _regionSortBuffer, activeCellCount);
		EnsureCapacity(ref _bfsQueue, activeCellCount);
		EnsureCapacity(ref _regionCells, activeCellCount);
		ReadOnlySpan<int> cellIndicesPartial = CollectionsMarshal.AsSpan(CellIndicesPartial);
		Span<bool> visited = _visited.AsSpan();
		Span<int> activeDegree = _activeDegree.AsSpan();
		ReadOnlySpan<int> rowPointers = _rowPointers.AsSpan();
		ReadOnlySpan<int> columnIndices = _columnIndices.AsSpan();
		ReadOnlySpan<int> stencilFaces = _stencilFaces.AsSpan();
		ReadOnlySpan<bool> cellActive = CellActive.AsSpan();
		ReadOnlySpan<double> faceArea = ws.FaceArea.AsSpan();
		ReadOnlySpan<float> faceMinimumElevations = props.FaceMinimumElevations.AsSpan();
		ReadOnlySpan<float> cellElevBuffer = _cellElevBuffer.AsSpan();
		for (int k = 0; k < cellIndicesPartial.Length; k++)
		{
			int index = cellIndicesPartial[k];
			visited[index] = false;
			activeDegree[index] = 0;
		}
		for (int l = 0; l < cellIndicesPartial.Length; l++)
		{
			int cellIndicesPartial2 = cellIndicesPartial[l];
			float cWSE = cellElevBuffer[cellIndicesPartial2];
			int rowPointerStart = rowPointers[cellIndicesPartial2];
			int rowPointerEnd = rowPointers[cellIndicesPartial2 + 1];
			int activeDegree2 = 0;
			for (int m = rowPointerStart; m < rowPointerEnd; m++)
			{
				int index2 = columnIndices[m];
				if (cellActive[index2])
				{
					int index3 = stencilFaces[m];
					if (BufferedEdge(faceArea[index3], cWSE, faceMinimumElevations[index3], maxElevChg))
					{
						activeDegree2++;
					}
				}
			}
			activeDegree[cellIndicesPartial2] = activeDegree2;
		}
		Span<int> permutation = _permutation.AsSpan();
		Span<int> blockOffsets = _blockOffsets.AsSpan();
		Span<int> regionStart = _regionStart.AsSpan();
		Span<int> regionSize = _regionSize.AsSpan();
		Span<int> bfsQueue = _bfsQueue.AsSpan();
		Span<int> regionCells = _regionCells.AsSpan();
		int blockOffsets2 = 0;
		int regionCount2 = 0;
		for (int n = 0; n < cellIndicesPartial.Length; n++)
		{
			int cellIndicesPartial3 = cellIndicesPartial[n];
			if (visited[cellIndicesPartial3])
			{
				continue;
			}
			int ordinal = blockOffsets2;
			regionStart[regionCount2] = ordinal;
			blockOffsets[regionCount2] = ordinal;
			int ordinal2 = 0;
			int ordinal3 = 0;
			bfsQueue[ordinal3++] = cellIndicesPartial3;
			visited[cellIndicesPartial3] = true;
			while (ordinal2 < ordinal3)
			{
				int bfsQueue2 = bfsQueue[ordinal2++];
				regionCells[blockOffsets2++] = bfsQueue2;
				float cWSE2 = cellElevBuffer[bfsQueue2];
				int rowPointerStart2 = rowPointers[bfsQueue2];
				int rowPointerEnd2 = rowPointers[bfsQueue2 + 1];
				for (int rowPointer = rowPointerStart2; rowPointer < rowPointerEnd2; rowPointer++)
				{
					int columnIndex = columnIndices[rowPointer];
					if (cellActive[columnIndex] && !visited[columnIndex])
					{
						int index4 = stencilFaces[rowPointer];
						if (BufferedEdge(faceArea[index4], cWSE2, faceMinimumElevations[index4], maxElevChg))
						{
							visited[columnIndex] = true;
							bfsQueue[ordinal3++] = columnIndex;
						}
					}
				}
			}
			regionSize[regionCount2] = blockOffsets2 - ordinal;
			regionCount2++;
		}
		RegionCount = regionCount2;
		blockOffsets[regionCount2] = blockOffsets2;
		int regionCount = RegionCount;
		Parallel.For(0, regionCount, (int r) =>
		{
			RunRcmInRegion(r, maxElevChg, ws, props);
		});
		AssignRegionIds(activeCellCount);
		Span<int> regionFinalId = _regionFinalId.AsSpan();
		for (int intervalIndex = 0; intervalIndex < RegionCount; intervalIndex++)
		{
			int regionStart2 = regionStart[intervalIndex];
			int count = regionStart2 + regionSize[intervalIndex];
			int regionFinalId2 = regionFinalId[intervalIndex];
			for (int regionStart3 = regionStart2; regionStart3 < count; regionStart3++)
			{
				cellRegionId[permutation[regionStart3]] = regionFinalId2;
			}
		}
		for (int intervalIndex2 = 0; intervalIndex2 < cellIndicesPartial.Length; intervalIndex2++)
		{
			int cellIndicesPartial4 = cellIndicesPartial[intervalIndex2];
			_priorCellRegionId[cellIndicesPartial4] = cellRegionId[cellIndicesPartial4];
		}
		ReadOnlySpan<int> cellIndicesRemoved2 = CollectionsMarshal.AsSpan(CellIndicesRemoved);
		for (int intervalIndex3 = 0; intervalIndex3 < cellIndicesRemoved2.Length; intervalIndex3++)
		{
			_priorCellRegionId[cellIndicesRemoved2[intervalIndex3]] = -1;
		}
		Ticks[7] += stopwatch.ElapsedTicks;
	}

	private void AssignRegionIds(int activeCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(activeCount, "activeCount");
		Span<int> permutation = _permutation.AsSpan();
		Span<int> regionStart = _regionStart.AsSpan();
		Span<int> regionSize = _regionSize.AsSpan();
		Span<int> regionClaimedPriorId = _regionClaimedPriorId.AsSpan();
		Span<int> regionFinalId = _regionFinalId.AsSpan();
		ReadOnlySpan<int> priorCellRegionId = _priorCellRegionId.AsSpan();
		for (int i = 0; i < RegionCount; i++)
		{
			_priorIdVoteCount.Clear();
			int regionStart2 = regionStart[i];
			int count = regionStart2 + regionSize[i];
			for (int j = regionStart2; j < count; j++)
			{
				int priorCellRegionId2 = priorCellRegionId[permutation[j]];
				if (priorCellRegionId2 >= 0)
				{
					_priorIdVoteCount.TryGetValue(priorCellRegionId2, out var localValue);
					_priorIdVoteCount[priorCellRegionId2] = localValue + 1;
				}
			}
			int regionClaimedPriorId7 = -1;
			int priorIdVoteCountTerm = 0;
			foreach (KeyValuePair<int, int> priorIdVoteCount in _priorIdVoteCount)
			{
				if (priorIdVoteCount.Value > priorIdVoteCountTerm || (priorIdVoteCount.Value == priorIdVoteCountTerm && priorIdVoteCount.Key < regionClaimedPriorId7))
				{
					regionClaimedPriorId7 = priorIdVoteCount.Key;
					priorIdVoteCountTerm = priorIdVoteCount.Value;
				}
			}
			regionClaimedPriorId[i] = regionClaimedPriorId7;
		}
		Span<int> regionSortBuffer = _regionSortBuffer.AsSpan(0, RegionCount);
		for (int k = 0; k < RegionCount; k++)
		{
			regionSortBuffer[k] = k;
		}
		for (int l = 1; l < RegionCount; l++)
		{
			int regionSortBuffer2 = regionSortBuffer[l];
			int regionClaimedPriorId2 = regionClaimedPriorId[regionSortBuffer2];
			int regionSize2 = regionSize[regionSortBuffer2];
			int intervalIndex;
			for (intervalIndex = l - 1; intervalIndex >= 0; intervalIndex--)
			{
				int regionSortBuffer3 = regionSortBuffer[intervalIndex];
				int regionClaimedPriorId3 = regionClaimedPriorId[regionSortBuffer3];
				int regionSize3 = regionSize[regionSortBuffer3];
				if (regionClaimedPriorId3 <= regionClaimedPriorId2 && (regionClaimedPriorId3 != regionClaimedPriorId2 || regionSize3 >= regionSize2) && (regionClaimedPriorId3 != regionClaimedPriorId2 || regionSize3 != regionSize2 || regionSortBuffer3 <= regionSortBuffer2))
				{
					break;
				}
				regionSortBuffer[intervalIndex + 1] = regionSortBuffer[intervalIndex];
			}
			regionSortBuffer[intervalIndex + 1] = regionSortBuffer2;
		}
		int regionClaimedPriorId4Term = -1;
		for (int m = 0; m < RegionCount; m++)
		{
			int index = regionSortBuffer[m];
			int regionClaimedPriorId4 = regionClaimedPriorId[index];
			if (regionClaimedPriorId4 >= 0)
			{
				if (regionClaimedPriorId4 == regionClaimedPriorId4Term)
				{
					regionClaimedPriorId[index] = -1;
				}
				else
				{
					regionClaimedPriorId4Term = regionClaimedPriorId4;
				}
			}
		}
		_keptThisStepIds.Clear();
		int ordinal = 0;
		for (int n = 0; n < RegionCount; n++)
		{
			int regionClaimedPriorId5 = regionClaimedPriorId[n];
			if (regionClaimedPriorId5 >= 0)
			{
				_keptThisStepIds.Add(regionClaimedPriorId5);
			}
			else
			{
				ordinal++;
			}
		}
		if (ordinal == 0)
		{
			for (int intervalIndex2 = 0; intervalIndex2 < RegionCount; intervalIndex2++)
			{
				regionFinalId[intervalIndex2] = regionClaimedPriorId[intervalIndex2];
			}
		}
		else
		{
			_freeIdPool.Clear();
			int priorIssuedIdIndexTerm = -1;
			ReadOnlySpan<int> priorIssuedIds = CollectionsMarshal.AsSpan(_priorIssuedIds);
			for (int intervalIndex3 = 0; intervalIndex3 < priorIssuedIds.Length; intervalIndex3++)
			{
				if (_freeIdPool.Count >= ordinal)
				{
					break;
				}
				int priorIssuedIdIndex = priorIssuedIds[intervalIndex3];
				for (int ordinal2 = priorIssuedIdIndexTerm + 1; ordinal2 < priorIssuedIdIndex; ordinal2++)
				{
					if (_freeIdPool.Count >= ordinal)
					{
						break;
					}
					_freeIdPool.Add(ordinal2);
				}
				if (_freeIdPool.Count < ordinal && !_keptThisStepIds.Contains(priorIssuedIdIndex))
				{
					_freeIdPool.Add(priorIssuedIdIndex);
				}
				priorIssuedIdIndexTerm = priorIssuedIdIndex;
			}
			for (int ordinal3 = priorIssuedIdIndexTerm + 1; ordinal3 <= _priorIdHighWatermark; ordinal3++)
			{
				if (_freeIdPool.Count >= ordinal)
				{
					break;
				}
				_freeIdPool.Add(ordinal3);
			}
			int ordinal4 = 0;
			int priorIdHighWatermarkOffset = _priorIdHighWatermark + 1;
			for (int intervalIndex4 = 0; intervalIndex4 < RegionCount; intervalIndex4++)
			{
				int regionClaimedPriorId6 = regionClaimedPriorId[intervalIndex4];
				if (regionClaimedPriorId6 >= 0)
				{
					regionFinalId[intervalIndex4] = regionClaimedPriorId6;
				}
				else if (ordinal4 < _freeIdPool.Count)
				{
					regionFinalId[intervalIndex4] = _freeIdPool[ordinal4++];
				}
				else
				{
					regionFinalId[intervalIndex4] = priorIdHighWatermarkOffset++;
				}
				if (regionFinalId[intervalIndex4] > _priorIdHighWatermark)
				{
					_priorIdHighWatermark = regionFinalId[intervalIndex4];
				}
			}
		}
		_priorIssuedIds.Clear();
		for (int intervalIndex5 = 0; intervalIndex5 < RegionCount; intervalIndex5++)
		{
			_priorIssuedIds.Add(regionFinalId[intervalIndex5]);
		}
		_priorIssuedIds.Sort();
	}

	private void RunRcmInRegion(int r, float maxElevChg, MeshWaterState ws, MeshPropertyTables props)
	{
		int start = _regionStart[r];
		int regionSize = _regionSize[r];
		if (regionSize <= 0)
		{
			return;
		}
		Span<int> startSpan = _permutation.AsSpan(start, regionSize);
		ReadOnlySpan<int> start2 = _regionCells.AsSpan(start, regionSize);
		ReadOnlySpan<int> rowPointers = _rowPointers;
		ReadOnlySpan<int> columnIndices = _columnIndices;
		ReadOnlySpan<int> stencilFaces = _stencilFaces;
		ReadOnlySpan<double> faceAreas = ws.FaceArea;
		ReadOnlySpan<float> faceMinimumElevations = props.FaceMinimumElevations;
		ReadOnlySpan<float> cellElevBuffer = _cellElevBuffer;
		ReadOnlySpan<int> degrees = _activeDegree;
		int[] globalToLocalLocalTerm = _globalToLocalLocal.Value;
		int[] neighborScratchLocalTerm = _neighborScratchLocal.Value;
		int[] intValues = _bfsQueueLocal.Value;
		if (intValues.Length < regionSize)
		{
			int count;
			for (count = intValues.Length; count < regionSize; count *= 2)
			{
			}
			intValues = new int[count];
			_bfsQueueLocal.Value = intValues;
		}
		bool[] rent = ArrayPool<bool>.Shared.Rent(regionSize);
		Array.Clear(rent, 0, regionSize);
		try
		{
			int intervalIndex = 0;
			int maxValue = int.MaxValue;
			for (int i = 0; i < regionSize; i++)
			{
				int start3 = start2[i];
				globalToLocalLocalTerm[start3] = i;
				int degree = degrees[start3];
				if (degree < maxValue || (degree == maxValue && start3 < start2[intervalIndex]))
				{
					maxValue = degree;
					intervalIndex = i;
				}
			}
			int start4 = start2[intervalIndex];
			int ordinal = 0;
			int ordinal2 = 0;
			intValues[ordinal2++] = start4;
			rent[intervalIndex] = true;
			int ordinal3 = 0;
			while (ordinal < ordinal2)
			{
				int startSpan2 = intValues[ordinal++];
				startSpan[ordinal3++] = startSpan2;
				float cWSE = cellElevBuffer[startSpan2];
				int rowPointerStart = rowPointers[startSpan2];
				int rowPointerEnd = rowPointers[startSpan2 + 1];
				int count2 = 0;
				for (int j = rowPointerStart; j < rowPointerEnd; j++)
				{
					int columnIndex = columnIndices[j];
					int columnIndexTerm = globalToLocalLocalTerm[columnIndex];
					if (columnIndexTerm >= 0 && !rent[columnIndexTerm])
					{
						int index = stencilFaces[j];
						if (BufferedEdge(faceAreas[index], cWSE, faceMinimumElevations[index], maxElevChg))
						{
							rent[columnIndexTerm] = true;
							neighborScratchLocalTerm[count2++] = columnIndex;
						}
					}
				}
				if (count2 > 1)
				{
					SortNeighborsByDegree(neighborScratchLocalTerm.AsSpan(), count2, degrees);
				}
				for (int k = 0; k < count2; k++)
				{
					intValues[ordinal2++] = neighborScratchLocalTerm[k];
				}
			}
			int ordinal4 = 0;
			int regionSizeOffset = regionSize - 1;
			while (ordinal4 < regionSizeOffset)
			{
				ref int reference = ref startSpan[ordinal4];
				ref int reference2 = ref startSpan[regionSizeOffset];
				int start5 = startSpan[regionSizeOffset];
				int start6 = startSpan[ordinal4];
				reference = start5;
				reference2 = start6;
				ordinal4++;
				regionSizeOffset--;
			}
		}
		finally
		{
			for (int l = 0; l < regionSize; l++)
			{
				globalToLocalLocalTerm[start2[l]] = -1;
			}
			ArrayPool<bool>.Shared.Return(rent);
		}
	}

	private static void SortNeighborsByDegree(Span<int> neighbors, int count, ReadOnlySpan<int> degrees)
	{
		for (int i = 1; i < count; i++)
		{
			int neighbor = neighbors[i];
			int degree = degrees[neighbor];
			int intervalIndex;
			for (intervalIndex = i - 1; intervalIndex >= 0; intervalIndex--)
			{
				int neighbor2 = neighbors[intervalIndex];
				int degree2 = degrees[neighbor2];
				if (degree2 < degree || (degree2 == degree && neighbor2 < neighbor))
				{
					break;
				}
				neighbors[intervalIndex + 1] = neighbor2;
			}
			neighbors[intervalIndex + 1] = neighbor;
		}
	}

	private static void EnsureCapacity(ref int[] buffer, int required)
	{
		if (buffer.Length < required)
		{
			int count = buffer.Length;
			if (count < 1)
			{
				count = 1;
			}
			while (count < required)
			{
				count *= 2;
			}
			buffer = new int[count];
		}
	}

	public void WriteTicker()
	{
		Ticker[] values = Enum.GetValues<Ticker>();
		foreach (Ticker ticker in values)
		{
			int ticker2 = (int)ticker;
			if (ticker2 < Ticks.Length)
			{
				long ticksTicker2Term = Ticks[ticker2] / 10000;
				if (ticksTicker2Term > 0)
				{
					Trace.WriteLine($"{ticker}   " + ticksTicker2Term);
				}
				continue;
			}
			break;
		}
	}
}
