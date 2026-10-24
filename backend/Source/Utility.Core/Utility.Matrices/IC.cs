using System;
using System.Numerics;
using Utility.Logging;

namespace Utility.Matrices;

public class IC<T> : IPreconditioner<T> where T : unmanaged, IFloatingPoint<T>, IRootFunctions<T>
{
	public SparseMatrix<T> M;

	private T[] _y;

	private T[] _invDiag;

	private int[] _structureRowPointers;

	private int[] _structureColumnIndices;

	private int _structureRowCount = -1;

	private bool _structureChanged;

	public bool IsPreconditioner
	{
		get
		{
			return true;
		}
		set
		{
		}
	}

	public virtual string PrecondName => "IC";

	protected Span<T> InverseDiagonal => _invDiag;

	public virtual OperationResult Prepare(SparseMatrix<T> A)
	{
		if (A == null)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetNullMatrix(PrecondName));
		}
		if (A.RowCount <= 0)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetInvalidMatrixDimension(PrecondName, A.RowCount, A.NonZeroCount));
		}
		if (!A.HasSortedIndices)
		{
			A.Sort();
		}
		A.ComputeDiagonalPointers();
		EnsureCapacity(A);
		if (_structureChanged && !HasValidBasicStructure(A))
		{
			return FactorizationFailure();
		}
		M.ComputeTransposePointers();
		if (_structureChanged)
		{
			if (!HasValidSymmetricStructure())
			{
				return FactorizationFailure();
			}
			SnapshotStructure(A);
		}
		if (!Factorize(A))
		{
			return FactorizationFailure();
		}
		return OperationResult.Success();
	}

	protected virtual bool Factorize(SparseMatrix<T> A)
	{
		return FactorizeShifted(A);
	}

	private OperationResult FactorizationFailure()
	{
		return OperationResult.Fail(MatrixSolverMessageCodes.GetPreconditionerError(PrecondName, "Factorization failed"));
	}

	public void EnsureCapacity(SparseMatrix<T> A)
	{
		if (M == null)
		{
			M = new SparseMatrix<T>();
		}
		_structureChanged = !HasSameStructureAsSnapshot(A);
		M.CopyStructureFromSparse(A);
		M.HasSortedIndices = A.HasSortedIndices;
		if (_structureChanged)
		{
			M.UpToDateTransposePointers = false;
		}
		M.EnsureValuesCapacity(A.NonZeroCount, A.MaxNonZero);
		BufferCapacity.EnsureArrayCapacity(ref _y, A.RowCount, A.MaxRows);
		BufferCapacity.EnsureArrayCapacity(ref _invDiag, A.RowCount, A.MaxRows);
	}

	private bool HasSameStructureAsSnapshot(SparseMatrix<T> A)
	{
		int rowCount = A.RowCount;
		if (rowCount != _structureRowCount || _structureRowPointers == null)
		{
			return false;
		}
		int nonZeroCount = A.NonZeroCount;
		if (((ReadOnlySpan<int>)A.RowPointers.AsSpan(0, rowCount + 1)).SequenceEqual((ReadOnlySpan<int>)_structureRowPointers.AsSpan(0, rowCount + 1)))
		{
			return ((ReadOnlySpan<int>)A.ColumnIndices.AsSpan(0, nonZeroCount)).SequenceEqual((ReadOnlySpan<int>)_structureColumnIndices.AsSpan(0, nonZeroCount));
		}
		return false;
	}

	private void SnapshotStructure(SparseMatrix<T> A)
	{
		int rowCount = A.RowCount;
		int nonZeroCount = A.NonZeroCount;
		BufferCapacity.EnsureArrayCapacity(ref _structureRowPointers, rowCount + 1, A.MaxRows + 1);
		BufferCapacity.EnsureArrayCapacity(ref _structureColumnIndices, nonZeroCount, A.MaxNonZero);
		Array.Copy(A.RowPointers, _structureRowPointers, rowCount + 1);
		Array.Copy(A.ColumnIndices, _structureColumnIndices, nonZeroCount);
		_structureRowCount = rowCount;
	}

	private static bool HasValidBasicStructure(SparseMatrix<T> A)
	{
		int rowCount = A.RowCount;
		int nonZeroCount = A.NonZeroCount;
		if (A.RowPointers == null || A.RowPointers.Length < rowCount + 1)
		{
			return false;
		}
		if (A.ColumnIndices == null || A.ColumnIndices.Length < nonZeroCount)
		{
			return false;
		}
		if (A.DiagonalPointers == null || A.DiagonalPointers.Length < rowCount)
		{
			return false;
		}
		if (A.RowPointers[0] != 0 || A.RowPointers[rowCount] != nonZeroCount)
		{
			return false;
		}
		for (int i = 0; i < rowCount; i++)
		{
			int rowPointerStart = A.RowPointers[i];
			int rowPointerEnd = A.RowPointers[i + 1];
			if (rowPointerStart < 0 || rowPointerStart > rowPointerEnd || rowPointerEnd > nonZeroCount)
			{
				return false;
			}
			int columnIndexTerm = -1;
			for (int j = rowPointerStart; j < rowPointerEnd; j++)
			{
				int columnIndex = A.ColumnIndices[j];
				if (columnIndex < 0 || columnIndex >= rowCount || columnIndex <= columnIndexTerm)
				{
					return false;
				}
				columnIndexTerm = columnIndex;
			}
			int diagonalPointer = A.DiagonalPointers[i];
			if (diagonalPointer < rowPointerStart || diagonalPointer >= rowPointerEnd || A.ColumnIndices[diagonalPointer] != i)
			{
				return false;
			}
		}
		return true;
	}

	private bool HasValidSymmetricStructure()
	{
		int rowCount = M.RowCount;
		int nonZeroCount = M.NonZeroCount;
		if (M.TransposePointers == null || M.TransposePointers.Length < nonZeroCount)
		{
			return false;
		}
		for (int i = 0; i < rowCount; i++)
		{
			for (int j = M.RowPointers[i]; j < M.RowPointers[i + 1]; j++)
			{
				int transposePointer = M.TransposePointers[j];
				if (transposePointer < 0 || transposePointer >= nonZeroCount)
				{
					return false;
				}
				if (M.ColumnIndices[transposePointer] != i)
				{
					return false;
				}
			}
		}
		return true;
	}

	private static bool TryComputeDiagonalDominanceAlpha(SparseMatrix<T> A, out T alpha)
	{
		alpha = T.Zero;
		int rowCount = A.RowCount;
		T[] values = A.Values;
		int[] rowPointers = A.RowPointers;
		int[] diagonalPointers = A.DiagonalPointers;
		for (int i = 0; i < rowCount; i++)
		{
			T zero = T.Zero;
			for (int j = rowPointers[i]; j < rowPointers[i + 1]; j++)
			{
				zero += T.Abs(values[j]);
			}
			T diagonalPointersTerm = T.Abs(values[diagonalPointers[i]]);
			if (!T.IsFinite(diagonalPointersTerm) || diagonalPointersTerm <= T.Zero)
			{
				return false;
			}
			T zeroTerm = zero - diagonalPointersTerm;
			if (diagonalPointersTerm < zeroTerm)
			{
				alpha = T.Max(alpha, zeroTerm / diagonalPointersTerm - T.One);
			}
		}
		return T.IsFinite(alpha);
	}

	private bool FactorizeShifted(SparseMatrix<T> A)
	{
		if (!TryComputeDiagonalDominanceAlpha(A, out var alpha))
		{
			return false;
		}
		int rowCount = M.RowCount;
		T alphaPlusOne = alpha + T.One;
		T[] values = M.Values;
		T[] values2 = A.Values;
		int[] diagonalPointers = M.DiagonalPointers;
		int[] rowPointers = M.RowPointers;
		int[] columnIndices = M.ColumnIndices;
		int[] transposePointers = M.TransposePointers;
		for (int i = 0; i < rowCount; i++)
		{
			int diagonalPointer = diagonalPointers[i];
			int rowPointer = rowPointers[i];
			int rowPointer2 = rowPointers[i + 1];
			T values2DiagonalPointerTerm = values2[diagonalPointer] * alphaPlusOne;
			for (int j = rowPointer; j < diagonalPointer; j++)
			{
				T localValue = values[j];
				values2DiagonalPointerTerm -= localValue * localValue;
			}
			if (!T.IsFinite(values2DiagonalPointerTerm) || values2DiagonalPointerTerm <= T.Zero)
			{
				return false;
			}
			T localValue2 = T.Sqrt(values2DiagonalPointerTerm);
			if (!T.IsFinite(localValue2) || localValue2 <= T.Zero)
			{
				return false;
			}
			values[diagonalPointer] = localValue2;
			T invDiag = T.One / localValue2;
			_invDiag[i] = invDiag;
			for (int k = diagonalPointer + 1; k < rowPointer2; k++)
			{
				int columnIndex = columnIndices[k];
				int transposePointer = transposePointers[k];
				T values22 = values2[transposePointer];
				int rowPointer3 = rowPointer;
				int rowPointer4 = rowPointers[columnIndex];
				int diagonalPointer2 = diagonalPointers[columnIndex];
				while (rowPointer3 < diagonalPointer && rowPointer4 < diagonalPointer2)
				{
					int columnIndex2 = columnIndices[rowPointer3];
					int columnIndex3 = columnIndices[rowPointer4];
					if (columnIndex2 == columnIndex3)
					{
						values22 -= values[rowPointer3] * values[rowPointer4];
						rowPointer3++;
						rowPointer4++;
					}
					else if (columnIndex3 < columnIndex2)
					{
						rowPointer4++;
					}
					else
					{
						rowPointer3++;
					}
				}
				T values22TimesInvDiag = values22 * invDiag;
				if (!T.IsFinite(values22TimesInvDiag))
				{
					return false;
				}
				values[k] = values22TimesInvDiag;
				values[transposePointer] = values22TimesInvDiag;
			}
		}
		return true;
	}

	public virtual void Apply(T[] destination, T[] rhs)
	{
		if (M == null)
		{
			throw new InvalidOperationException("Preconditioner has not been prepared.");
		}
		int rowCount = M.RowCount;
		if (destination.Length < rowCount)
		{
			throw new ArgumentException("Destination is shorter than the prepared matrix.", "destination");
		}
		if (rhs.Length < rowCount)
		{
			throw new ArgumentException("Right-hand side is shorter than the prepared matrix.", "rhs");
		}
		SparseMatrix<T>.SolveLower(M, _y.AsSpan(0, rowCount), rhs.AsSpan(0, rowCount), _invDiag.AsSpan(0, rowCount));
		SparseMatrix<T>.SolveUpper(M, destination.AsSpan(0, rowCount), _y.AsSpan(0, rowCount), _invDiag.AsSpan(0, rowCount));
	}
}
