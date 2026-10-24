using System;
using System.Numerics;
using Utility.Logging;

namespace Utility.Matrices;

public class ILU<T> : IPreconditioner<T> where T : unmanaged, IFloatingPoint<T>, IRootFunctions<T>
{
	private SparseMatrix<T> _A;

	public SparseMatrix<T> M;

	private int[] _columnToIndex;

	private T[] _temp;

	private T[] _invDiag;

	public string PrecondName => "ILU";

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
		_A = A;
		A.ComputeDiagonalPointers();
		if (M == null)
		{
			M = new SparseMatrix<T>(A);
		}
		int maxRows = A.MaxRows;
		if (_temp == null || _temp.Length < maxRows)
		{
			_temp = new T[maxRows];
		}
		if (_invDiag == null || _invDiag.Length < maxRows)
		{
			_invDiag = new T[maxRows];
		}
		if (_columnToIndex == null || _columnToIndex.Length < maxRows)
		{
			_columnToIndex = new int[maxRows];
			Array.Fill(_columnToIndex, -1);
		}
		if (!PrepareInternal())
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetPreconditionerError(PrecondName, "Factorization failed"));
		}
		return OperationResult.Success();
	}

	private bool PrepareInternal()
	{
		int rowCount = _A.RowCount;
		int nonZeroCount = _A.NonZeroCount;
		int[] rowPointers = _A.RowPointers;
		int[] columnIndices = _A.ColumnIndices;
		T[] values = _A.Values;
		int[] diagonalPointers = _A.DiagonalPointers;
		T[] values2 = M.Values;
		int[] columnToIndex = _columnToIndex;
		Array.Copy(values, 0, values2, 0, nonZeroCount);
		for (int i = 0; i < rowCount; i++)
		{
			int rowPointerStart = rowPointers[i];
			int rowPointerEnd = rowPointers[i + 1];
			for (int j = rowPointerStart; j < rowPointerEnd; j++)
			{
				columnToIndex[columnIndices[j]] = j;
			}
			for (int k = rowPointerStart; k < rowPointerEnd; k++)
			{
				int columnIndex = columnIndices[k];
				if (columnIndex >= i)
				{
					break;
				}
				T values2DiagonalPointersTerm = (values2[k] *= values2[diagonalPointers[columnIndex]]);
				int diagonalPointer = diagonalPointers[columnIndex];
				int rowPointer = rowPointers[columnIndex + 1];
				for (int l = diagonalPointer + 1; l < rowPointer; l++)
				{
					int columnToIndex2 = columnToIndex[columnIndices[l]];
					if (columnToIndex2 != -1)
					{
						T[] values2Array = values2;
						int columnToIndex3 = columnToIndex2;
						values2Array[columnToIndex3] -= values2DiagonalPointersTerm * values2[l];
					}
				}
			}
			int diagonalPointer2 = diagonalPointers[i];
			if (diagonalPointer2 >= rowPointerEnd || columnIndices[diagonalPointer2] != i)
			{
				return false;
			}
			if (values2[diagonalPointer2] == T.Zero)
			{
				return false;
			}
			_temp[i] = values2[diagonalPointer2];
			T invDiag = (values2[diagonalPointer2] = T.One / values2[diagonalPointer2]);
			_invDiag[i] = invDiag;
			for (int m = rowPointerStart; m < rowPointerEnd; m++)
			{
				columnToIndex[columnIndices[m]] = -1;
			}
		}
		for (int n = 0; n < rowCount; n++)
		{
			values2[diagonalPointers[n]] = _temp[n];
		}
		return true;
	}

	public virtual void Apply(T[] solution, T[] rhs)
	{
		if (_A == null)
		{
			throw new InvalidOperationException("Preconditioner has not been prepared.");
		}
		if (solution.Length < _A.RowCount)
		{
			throw new ArgumentException("Destination is shorter than the prepared matrix.", "solution");
		}
		if (rhs.Length < _A.RowCount)
		{
			throw new ArgumentException("Right-hand side is shorter than the prepared matrix.", "rhs");
		}
		SolveInternal(solution, rhs);
	}

	private void SolveInternal(Span<T> solution, ReadOnlySpan<T> rhs)
	{
		int rowCount = _A.RowCount;
		SparseMatrix<T>.SolveUnitLower(M, _temp.AsSpan(0, rowCount), rhs.Slice(0, rowCount));
		SparseMatrix<T>.SolveUpper(M, solution.Slice(0, rowCount), _temp.AsSpan(0, rowCount), _invDiag.AsSpan(0, rowCount));
	}
}
