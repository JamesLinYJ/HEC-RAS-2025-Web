using System;
using System.Runtime.CompilerServices;
using Geospatial.Vectors;

namespace Ras.Engine;

public class MeshConnectivity
{
	public int NumCells { get; }

	public int NumFaces { get; }

	public int[] CellNeighborOffsets { get; }

	public int[] CellNeighborCells { get; }

	public int[] CellNeighborFaces { get; }

	public int[] CellFaceOrientations { get; }

	public int[] FaceCellA { get; }

	public int[] FaceCellB { get; }

	public MeshConnectivity(Mesh mesh)
	{
		NumCells = mesh.CellCount;
		NumFaces = mesh.FaceCount;
		mesh.ComputeCellAdjacencyMatrix(out var rowPointers, out var columnIndices, addDiagonal: false, excludeBoundaryCells: false);
		CellNeighborOffsets = rowPointers;
		CellNeighborCells = columnIndices;
		CellNeighborFaces = new int[columnIndices.Length];
		Array.Fill(CellNeighborFaces, -1);
		CellFaceOrientations = new int[columnIndices.Length];
		FaceCellA = new int[NumFaces];
		FaceCellB = new int[NumFaces];
		for (int i = 0; i < NumFaces; i++)
		{
			Face face = mesh.Faces[i];
			FaceCellA[i] = face.cellA;
			FaceCellB[i] = face.cellB;
		}
		for (int j = 0; j < NumCells; j++)
		{
			int cellNeighborOffsetStart = CellNeighborOffsets[j];
			int cellNeighborOffsetEnd = CellNeighborOffsets[j + 1];
			ReadOnlySpan<int> faces = mesh.Cells[j].Faces.Span;
			for (int k = 0; k < faces.Length; k++)
			{
				int faceIndex = faces[k];
				int faceCellA = FaceCellA[faceIndex];
				int faceCellB = FaceCellB[faceIndex];
				int neighborCell;
				bool sign;
				if (faceCellA != j)
				{
					neighborCell = faceCellA;
					sign = false;
				}
				else
				{
					neighborCell = faceCellB;
					sign = true;
				}
				for (int l = cellNeighborOffsetStart; l < cellNeighborOffsetEnd; l++)
				{
					if (CellNeighborCells[l] == neighborCell && CellNeighborFaces[l] == -1)
					{
						CellNeighborFaces[l] = faceIndex;
						CellFaceOrientations[l] = (sign ? 1 : (-1));
						break;
					}
				}
			}
		}
	}

	public bool TryGetFace(int cIdx, int neighborCell, out int faceIdx, out int sgn)
	{
		int cellNeighborOffsetStart = CellNeighborOffsets[cIdx];
		int cellNeighborOffsetEnd = CellNeighborOffsets[cIdx + 1];
		for (int i = cellNeighborOffsetStart; i < cellNeighborOffsetEnd; i++)
		{
			if (CellNeighborCells[i] == neighborCell)
			{
				faceIdx = CellNeighborFaces[i];
				sgn = CellFaceOrientations[i];
				return true;
			}
		}
		faceIdx = -1;
		sgn = 0;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Orientation(int fIdx, int cIdx)
	{
		if (FaceCellA[fIdx] != cIdx)
		{
			return -1;
		}
		return 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetFaceUpwindCell(int fIdx, double fVelocity)
	{
		if (!(fVelocity > 0.0))
		{
			return FaceCellB[fIdx];
		}
		return FaceCellA[fIdx];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetFaceUpwindCell(int fIdx, double[] faceVelocity)
	{
		if (!(faceVelocity[fIdx] > 0.0))
		{
			return FaceCellB[fIdx];
		}
		return FaceCellA[fIdx];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ReadOnlySpan<int> GetCellNeighbors(int cellIndex)
	{
		int cellNeighborOffset = CellNeighborOffsets[cellIndex];
		int cellNeighborOffset2 = CellNeighborOffsets[cellIndex + 1];
		Span<int> cellNeighborCells = CellNeighborCells.AsSpan();
		int cellNeighborOffset3 = cellNeighborOffset;
		return cellNeighborCells.Slice(cellNeighborOffset3, cellNeighborOffset2 - cellNeighborOffset3);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ReadOnlySpan<int> GetCellFaces(int cellIndex)
	{
		int cellNeighborOffset = CellNeighborOffsets[cellIndex];
		int cellNeighborOffset2 = CellNeighborOffsets[cellIndex + 1];
		Span<int> cellNeighborFaces = CellNeighborFaces.AsSpan();
		int cellNeighborOffset3 = cellNeighborOffset;
		return cellNeighborFaces.Slice(cellNeighborOffset3, cellNeighborOffset2 - cellNeighborOffset3);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ReadOnlySpan<int> GetCellFaceOrientations(int cellIndex)
	{
		int cellNeighborOffset = CellNeighborOffsets[cellIndex];
		int cellNeighborOffset2 = CellNeighborOffsets[cellIndex + 1];
		Span<int> cellFaceOrientations = CellFaceOrientations.AsSpan();
		int cellNeighborOffset3 = cellNeighborOffset;
		return cellFaceOrientations.Slice(cellNeighborOffset3, cellNeighborOffset2 - cellNeighborOffset3);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void GetCellConnectivity(int cellIndex, out ReadOnlySpan<int> faces, out ReadOnlySpan<int> neighbors, out ReadOnlySpan<int> orientations)
	{
		int cellNeighborOffset = CellNeighborOffsets[cellIndex];
		int length = CellNeighborOffsets[cellIndex + 1] - cellNeighborOffset;
		faces = CellNeighborFaces.AsSpan(cellNeighborOffset, length);
		neighbors = CellNeighborCells.AsSpan(cellNeighborOffset, length);
		orientations = CellFaceOrientations.AsSpan(cellNeighborOffset, length);
	}
}
