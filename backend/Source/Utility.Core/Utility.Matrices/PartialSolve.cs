using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Utility.Matrices;

public class PartialSolve
{
	public int[] FullToPart;

	public List<int> PartToFull;

	public int[] BlockOffsets;

	public int BlockCount;

	public int[] RegionIds;

	private int[] _blockModePartToFull;

	private int _blockModeCount;

	public int NumFull => FullToPart?.Length ?? 0;

	public int NumPart
	{
		get
		{
			if (_blockModePartToFull == null)
			{
				return PartToFull?.Count ?? 0;
			}
			return _blockModeCount;
		}
	}

	private ReadOnlySpan<int> PartToFullSpan => (_blockModePartToFull != null) ? _blockModePartToFull.AsSpan(0, _blockModeCount) : CollectionsMarshal.AsSpan(PartToFull);

	public PartialSolve()
	{
		FullToPart = null;
		PartToFull = null;
	}

	public PartialSolve(int[] fullToPart, List<int> partToFull)
	{
		FullToPart = fullToPart;
		PartToFull = partToFull;
	}

	private void Allocate(int numFull)
	{
		if (FullToPart == null)
		{
			FullToPart = new int[numFull];
		}
		if (PartToFull == null)
		{
			PartToFull = new List<int>();
		}
	}

	public bool Compute(bool[] keep)
	{
		if (NumFull < keep.Length)
		{
			Allocate(keep.Length);
		}
		BlockCount = 0;
		_blockModePartToFull = null;
		_blockModeCount = 0;
		UpdateFromKeep(keep);
		return true;
	}

	public bool Compute(int[] fullToPart)
	{
		FullToPart = fullToPart;
		BlockCount = 0;
		_blockModePartToFull = null;
		_blockModeCount = 0;
		UpdateFromFullToPart();
		return true;
	}

	private void UpdateFromKeep(bool[] keep)
	{
		if (PartToFull == null)
		{
			PartToFull = new List<int>();
		}
		PartToFull.Clear();
		for (int i = 0; i < NumFull; i++)
		{
			if (keep[i])
			{
				FullToPart[i] = PartToFull.Count;
				PartToFull.Add(i);
			}
			else
			{
				FullToPart[i] = -1;
			}
		}
	}

	private void UpdateFromFullToPart()
	{
		if (PartToFull == null)
		{
			PartToFull = new List<int>();
		}
		PartToFull.Clear();
		for (int i = 0; i < NumFull; i++)
		{
			if (FullToPart[i] >= 0)
			{
				PartToFull.Add(i);
			}
		}
	}

	public bool ComputeFromPartialGrid(int[] permutation, int permLength, ReadOnlySpan<int> blockOffsets, ReadOnlySpan<int> regionIds, int meshCellCount)
	{
		if (permutation == null)
		{
			return false;
		}
		if (permLength < 0 || permLength > permutation.Length)
		{
			return false;
		}
		if (blockOffsets.Length < 1)
		{
			return false;
		}
		int blockCount = blockOffsets.Length - 1;
		if (regionIds.Length < blockCount)
		{
			return false;
		}
		if (blockOffsets[blockCount] != permLength)
		{
			return false;
		}
		if (FullToPart == null || FullToPart.Length < meshCellCount)
		{
			FullToPart = new int[meshCellCount];
		}
		Span<int> fullToPart = FullToPart.AsSpan(0, meshCellCount);
		fullToPart.Fill(-1);
		ReadOnlySpan<int> permutationSpan = permutation.AsSpan(0, permLength);
		for (int i = 0; i < permLength; i++)
		{
			fullToPart[permutationSpan[i]] = i;
		}
		_blockModePartToFull = permutation;
		_blockModeCount = permLength;
		EnsureCapacity(ref BlockOffsets, blockCount + 1);
		for (int j = 0; j <= blockCount; j++)
		{
			BlockOffsets[j] = blockOffsets[j];
		}
		EnsureCapacity(ref RegionIds, Math.Max(1, blockCount));
		for (int k = 0; k < blockCount; k++)
		{
			RegionIds[k] = regionIds[k];
		}
		BlockCount = blockCount;
		return true;
	}

	public int BlockSize(int blockIdx)
	{
		return BlockOffsets[blockIdx + 1] - BlockOffsets[blockIdx];
	}

	public void GetBlockRange(int blockIdx, out int packedStart, out int packedEnd)
	{
		packedStart = BlockOffsets[blockIdx];
		packedEnd = BlockOffsets[blockIdx + 1];
	}

	public void ExtractBlock<T>(SparseMatrix<T> full, int blockIdx, ref SparseMatrix<T> blockOut, ref int[] positionMap) where T : unmanaged, INumber<T>
	{
		int blockOffset = BlockOffsets[blockIdx];
		int blockOffset2 = BlockOffsets[blockIdx + 1];
		int rowCount = blockOffset2 - blockOffset;
		ReadOnlySpan<int> fullToPart = FullToPart.AsSpan();
		ReadOnlySpan<int> rowPointers = full.RowPointers.AsSpan();
		ReadOnlySpan<int> columnIndices = full.ColumnIndices.AsSpan();
		ReadOnlySpan<int> partToFullSpan = PartToFullSpan;
		int ordinal = 0;
		for (int i = 0; i < rowCount; i++)
		{
			int partToFull = partToFullSpan[blockOffset + i];
			int rowPointerStart = rowPointers[partToFull];
			int rowPointerEnd = rowPointers[partToFull + 1];
			for (int j = rowPointerStart; j < rowPointerEnd; j++)
			{
				int fullToPart2 = fullToPart[columnIndices[j]];
				if (fullToPart2 >= blockOffset && fullToPart2 < blockOffset2)
				{
					ordinal++;
				}
			}
		}
		if (blockOut == null)
		{
			blockOut = new SparseMatrix<T>();
		}
		blockOut.EnsureCapacity(Math.Max(1, rowCount), Math.Max(1, ordinal), full.MaxRows, full.MaxNonZero);
		BufferCapacity.EnsureArrayCapacity(ref positionMap, Math.Max(1, ordinal), full.MaxNonZero);
		blockOut.RowCount = rowCount;
		Span<int> rowPointersSpan = blockOut.RowPointers.AsSpan();
		Span<int> columnIndicesSpan = blockOut.ColumnIndices.AsSpan();
		Span<T> buffer = blockOut.Values.AsSpan();
		ReadOnlySpan<T> bufferSpan = full.Values.AsSpan();
		rowPointersSpan[0] = 0;
		int rowPointersSpan2 = 0;
		for (int k = 0; k < rowCount; k++)
		{
			int partToFull2 = partToFullSpan[blockOffset + k];
			int rowPointerStart2 = rowPointers[partToFull2];
			int rowPointerEnd2 = rowPointers[partToFull2 + 1];
			for (int l = rowPointerStart2; l < rowPointerEnd2; l++)
			{
				int fullToPart3 = fullToPart[columnIndices[l]];
				if (fullToPart3 >= blockOffset && fullToPart3 < blockOffset2)
				{
					columnIndicesSpan[rowPointersSpan2] = fullToPart3 - blockOffset;
					buffer[rowPointersSpan2] = bufferSpan[l];
					positionMap[rowPointersSpan2] = l;
					rowPointersSpan2++;
				}
			}
			rowPointersSpan[k + 1] = rowPointersSpan2;
		}
		blockOut.HasSortedIndices = false;
		blockOut.UpToDateColumnPointers = false;
		blockOut.UpToDateDiagonalPointers = false;
		blockOut.UpToDateTransposePointers = false;
		blockOut.Sort(positionMap, skipColumnPointers: true);
	}

	public void MapBlockData<T>(SparseMatrix<T> full, SparseMatrix<T> blockOut, int[] positionMap) where T : unmanaged, INumber<T>
	{
		int nonZeroCount = blockOut.NonZeroCount;
		ReadOnlySpan<T> buffer = full.Values.AsSpan();
		Span<T> bufferSpan = blockOut.Values.AsSpan();
		for (int i = 0; i < nonZeroCount; i++)
		{
			bufferSpan[i] = buffer[positionMap[i]];
		}
	}

	public void GatherBlock<T>(int blockIdx, ReadOnlySpan<T> xFull, ReadOnlySpan<T> bFull, Span<T> xBlock, Span<T> bBlock) where T : unmanaged
	{
		int blockOffsetStart = BlockOffsets[blockIdx];
		int blockOffsetEnd = BlockOffsets[blockIdx + 1];
		ReadOnlySpan<int> partToFullSpan = PartToFullSpan;
		for (int i = blockOffsetStart; i < blockOffsetEnd; i++)
		{
			int index = partToFullSpan[i];
			xBlock[i - blockOffsetStart] = xFull[index];
			bBlock[i - blockOffsetStart] = bFull[index];
		}
	}

	public void ScatterBlock<T>(int blockIdx, ReadOnlySpan<T> xBlock, Span<T> xFull) where T : unmanaged
	{
		int blockOffsetStart = BlockOffsets[blockIdx];
		int blockOffsetEnd = BlockOffsets[blockIdx + 1];
		ReadOnlySpan<int> partToFullSpan = PartToFullSpan;
		for (int i = blockOffsetStart; i < blockOffsetEnd; i++)
		{
			xFull[partToFullSpan[i]] = xBlock[i - blockOffsetStart];
		}
	}

	private static void EnsureCapacity(ref int[] buffer, int required)
	{
		if (buffer == null || buffer.Length < required)
		{
			int count = buffer?.Length ?? 1;
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
}
