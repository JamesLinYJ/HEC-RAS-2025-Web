using System;
using Geospatial.Vectors.Meshes;

namespace Ras.Engine;

internal sealed class CasulliFaceFlowInterpolationWorkspace
{
	private readonly MeshConnectivity _connectivity;

	private readonly double[] _incomingFaceFlows;

	private readonly bool[] _faceConnected;

	private bool _prepared;

	public CasulliFaceFlowInterpolationWorkspace(MeshConnectivity connectivity)
	{
		ArgumentNullException.ThrowIfNull(connectivity, "connectivity");
		_connectivity = connectivity;
		_incomingFaceFlows = new double[connectivity.CellNeighborFaces.Length];
		_faceConnected = new bool[connectivity.NumFaces];
	}

	public void Prepare(ReadOnlySpan<double> cellVolumes, ReadOnlySpan<double> faceFlows, ReadOnlySpan<double> faceAreas, bool[] cellActive = null, bool[] faceActive = null)
	{
		ValidateState(cellVolumes, faceFlows, faceAreas, cellActive, faceActive);
		int[] faceCellA = _connectivity.FaceCellA;
		int[] faceCellB = _connectivity.FaceCellB;
		for (int i = 0; i < _connectivity.NumFaces; i++)
		{
			int index = faceCellA[i];
			int faceCellB2 = faceCellB[i];
			_faceConnected[i] = IsWet(faceAreas[i], faceActive, i) && IsWet(cellVolumes[index], cellActive, index) && (faceCellB2 < 0 || IsWet(cellVolumes[faceCellB2], cellActive, faceCellB2)) && double.IsFinite(faceFlows[i]);
		}
		int[] cellNeighborFaces = _connectivity.CellNeighborFaces;
		int[] cellFaceOrientations = _connectivity.CellFaceOrientations;
		for (int j = 0; j < cellNeighborFaces.Length; j++)
		{
			int cellNeighborFaceIndex = cellNeighborFaces[j];
			if (cellNeighborFaceIndex < 0 || !_faceConnected[cellNeighborFaceIndex])
			{
				_incomingFaceFlows[j] = 0.0;
				continue;
			}
			double cellFaceOrientationsFaceFlowsTerm = (double)cellFaceOrientations[j] * faceFlows[cellNeighborFaceIndex];
			_incomingFaceFlows[j] = ((cellFaceOrientationsFaceFlowsTerm < 0.0) ? (0.0 - cellFaceOrientationsFaceFlowsTerm) : 0.0);
		}
		_prepared = true;
	}

	public bool TryInterpolateUpstreamValue(int cell, ReadOnlySpan<double> cellValues, HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace boundaryReconstruction, out double upstreamValue)
	{
		if (!_prepared)
		{
			throw new InvalidOperationException("Casulli face-flow interpolation is not prepared.");
		}
		if (cell < 0 || cell >= _connectivity.NumCells)
		{
			throw new ArgumentOutOfRangeException("cell");
		}
		if (cellValues.Length != _connectivity.NumCells)
		{
			throw new ArgumentException("Cell values must match the mesh cell count.", "cellValues");
		}
		return TryInterpolateUpstreamValueUnchecked(cell, cellValues, boundaryReconstruction, out upstreamValue);
	}

	internal bool TryInterpolateUpstreamValueUnchecked(int cell, ReadOnlySpan<double> cellValues, HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace boundaryReconstruction, out double upstreamValue)
	{
		int[] cellNeighborOffsets = _connectivity.CellNeighborOffsets;
		int[] cellNeighborCells = _connectivity.CellNeighborCells;
		int[] cellNeighborFaces = _connectivity.CellNeighborFaces;
		double incomingFaceFlowTimesCellValueSum = 0.0;
		double incomingFaceFlowSum = 0.0;
		for (int i = cellNeighborOffsets[cell]; i < cellNeighborOffsets[cell + 1]; i++)
		{
			double incomingFaceFlow = _incomingFaceFlows[i];
			if (!(incomingFaceFlow > 0.0))
			{
				continue;
			}
			int cellNeighborCellIndex = cellNeighborCells[i];
			double cellValue;
			if (cellNeighborCellIndex >= 0)
			{
				cellValue = cellValues[cellNeighborCellIndex];
			}
			else
			{
				int face = cellNeighborFaces[i];
				if (boundaryReconstruction == null || !boundaryReconstruction.TryGetCondition(face, out var type, out cellValue) || type != ScalarBoundaryConditionType.Value)
				{
					continue;
				}
			}
			if (!double.IsFinite(cellValue))
			{
				upstreamValue = 0.0;
				return false;
			}
			incomingFaceFlowTimesCellValueSum += incomingFaceFlow * cellValue;
			incomingFaceFlowSum += incomingFaceFlow;
		}
		if (!(incomingFaceFlowSum > 0.0) || !double.IsFinite(incomingFaceFlowSum) || !double.IsFinite(incomingFaceFlowTimesCellValueSum))
		{
			upstreamValue = 0.0;
			return false;
		}
		upstreamValue = incomingFaceFlowTimesCellValueSum / incomingFaceFlowSum;
		return double.IsFinite(upstreamValue);
	}

	internal bool IsFaceConnectedUnchecked(int face)
	{
		return _faceConnected[face];
	}

	private static bool IsWet(double magnitude, bool[] active, int index)
	{
		if (magnitude > 0.0 && double.IsFinite(magnitude))
		{
			if (active != null)
			{
				return active[index];
			}
			return true;
		}
		return false;
	}

	private void ValidateState(ReadOnlySpan<double> cellVolumes, ReadOnlySpan<double> faceFlows, ReadOnlySpan<double> faceAreas, bool[] cellActive, bool[] faceActive)
	{
		if (cellVolumes.Length != _connectivity.NumCells)
		{
			throw new ArgumentException("Cell volumes must match the mesh cell count.", "cellVolumes");
		}
		if (faceFlows.Length != _connectivity.NumFaces)
		{
			throw new ArgumentException("Face flows must match the mesh face count.", "faceFlows");
		}
		if (faceAreas.Length != _connectivity.NumFaces)
		{
			throw new ArgumentException("Face areas must match the mesh face count.", "faceAreas");
		}
		if (cellActive != null && cellActive.Length != _connectivity.NumCells)
		{
			throw new ArgumentException("Cell active flags must match the mesh cell count.", "cellActive");
		}
		if (faceActive != null && faceActive.Length != _connectivity.NumFaces)
		{
			throw new ArgumentException("Face active flags must match the mesh face count.", "faceActive");
		}
	}
}
