using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Utility.Logging;

namespace Utility.Matrices;

public class Gauss<T> : MatrixSolver<T>, IMatrixSolver<T> where T : unmanaged, IFloatingPointIeee754<T>, IRootFunctions<T>
{
	private T[] _dense;

	private T[] _rhsCopy;

	private int _currentSize;

	public string SolverName => "Gauss";

	public bool IsDirectSolver => true;

	public OperationResult Configure(object options)
	{
		return OperationResult.Success();
	}

	public override OperationResult Prepare(SparseMatrix<T> A, IPreconditioner<T> precond = null, PartialSolve partSolve = null)
	{
		OperationResult operationResult = base.Prepare(A, precond, partSolve);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		EnsureDense(Matrix.RowCount);
		Matrix.ToDense(_dense);
		return operationResult;
	}

	public OperationResult PrepareAndSolve(SparseMatrix<T> A, T[] x, T[] b, IPreconditioner<T> precond = null, PartialSolve partSolve = null)
	{
		OperationResult operationResult = Prepare(A, precond, partSolve);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		return Solve(x, b);
	}

	public OperationResult Solve(SparseMatrix<T> A, T[] x, T[] b)
	{
		return PrepareAndSolve(A, x, b);
	}

	public OperationResult Solve(T[,] A, T[] x, T[] b)
	{
		int length = A.GetLength(0);
		EnsureDense(length);
		ReadOnlySpan<T> createReadOnlySpan = MemoryMarshal.CreateReadOnlySpan(in A[0, 0], A.Length);
		int length2 = A.GetLength(1);
		if (length2 == length)
		{
			createReadOnlySpan.Slice(0, length * length).CopyTo(_dense);
		}
		else
		{
			for (int i = 0; i < length; i++)
			{
				createReadOnlySpan.Slice(i * length2, length).CopyTo(_dense.AsSpan(i * length, length));
			}
		}
		if (_rhsCopy == null || _rhsCopy.Length < length)
		{
			_rhsCopy = new T[length];
		}
		Array.Copy(b, _rhsCopy, length);
		SolveDenseInPlace(_dense.AsSpan(0, length * length), length, x, _rhsCopy, length);
		return OperationResult.Success();
	}

	public override OperationResult Update(SparseMatrix<T> A)
	{
		EnsureDense(A.RowCount);
		A.ToDense(_dense);
		return OperationResult.Success();
	}

	public OperationResult Solve(T[] x, T[] b)
	{
		OperationResult operationResult = PreSolve(x, b);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		int currentSize = _currentSize;
		if (_rhsCopy == null || _rhsCopy.Length < currentSize)
		{
			_rhsCopy = new T[currentSize];
		}
		Array.Copy(RightHandSide, _rhsCopy, currentSize);
		SolveDenseInPlace(_dense.AsSpan(0, currentSize * currentSize), currentSize, Solution, _rhsCopy, currentSize);
		operationResult.MergeIn(PostSolve(x));
		return operationResult;
	}

	public OperationResult Destroy(SparseMatrix<T> A)
	{
		_dense = null;
		_rhsCopy = null;
		_currentSize = 0;
		return OperationResult.Success();
	}

	private void EnsureDense(int n)
	{
		_currentSize = n;
		if (_dense == null || _dense.Length < n * n)
		{
			_dense = new T[n * n];
		}
	}

	public static void SolveDenseInPlace(T[,] A, T[] x, T[] b, int n)
	{
		if (n > 0)
		{
			SolveDenseInPlace(MemoryMarshal.CreateSpan(ref A[0, 0], A.Length), A.GetLength(1), x, b, n);
		}
	}

	public static void SolveDenseInPlace(Span<T> A, int stride, T[] x, T[] b, int n)
	{
		T saturating = T.CreateSaturating(1E-30);
		for (int i = 0; i < n; i++)
		{
			int strideTerm = i * stride;
			T localValue = T.Abs(A[strideTerm + i]);
			int ordinal = i;
			for (int j = i + 1; j < n; j++)
			{
				T strideTerm2 = T.Abs(A[j * stride + i]);
				if (strideTerm2 > localValue)
				{
					localValue = strideTerm2;
					ordinal = j;
				}
			}
			if (ordinal != i)
			{
				Span<T> slice = A.Slice(strideTerm + i, n - i);
				Span<T> sliceSpan = A.Slice(ordinal * stride + i, n - i);
				T reference22;
				T reference3;
				for (int k = 0; k < slice.Length; k++)
				{
					ref T reference = ref slice[k];
					ref T reference2 = ref sliceSpan[k];
					reference3 = sliceSpan[k];
					reference22 = slice[k];
					reference = reference3;
					reference2 = reference22;
				}
				T[] values = b;
				int ordinal2 = i;
				int ordinal3 = ordinal;
				reference22 = b[ordinal];
				reference3 = b[i];
				values[ordinal2] = reference22;
				b[ordinal3] = reference3;
			}
			T localValue2 = A[strideTerm + i];
			if (T.Abs(localValue2) < saturating)
			{
				continue;
			}
			int count = n - i - 1;
			ReadOnlySpan<T> slice2 = A.Slice(strideTerm + i + 1, count);
			for (int l = i + 1; l < n; l++)
			{
				int strideTerm3 = l * stride;
				T localValue3 = A[strideTerm3 + i] / localValue2;
				if (localValue3 == T.Zero)
				{
					continue;
				}
				A[strideTerm3 + i] = T.Zero;
				Span<T> slice3 = A.Slice(strideTerm3 + i + 1, count);
				int m = 0;
				if (Vector.IsHardwareAccelerated && Vector<T>.IsSupported)
				{
					Vector<T> vector = new Vector<T>(localValue3);
					for (; m <= count - Vector<T>.Count; m += Vector<T>.Count)
					{
						Vector<T> vector2 = new Vector<T>(slice2.Slice(m));
						(new Vector<T>((ReadOnlySpan<T>)slice3.Slice(m)) - vector * vector2).CopyTo(slice3.Slice(m));
					}
				}
				for (; m < count; m++)
				{
					slice3[m] -= localValue3 * slice2[m];
				}
				T[] valuesArray = b;
				int ordinal4 = l;
				valuesArray[ordinal4] -= localValue3 * b[i];
			}
		}
		for (int ordinal5 = n - 1; ordinal5 >= 0; ordinal5--)
		{
			int strideTerm4 = ordinal5 * stride;
			T localValue4 = b[ordinal5];
			for (int ordinal6 = ordinal5 + 1; ordinal6 < n; ordinal6++)
			{
				localValue4 -= A[strideTerm4 + ordinal6] * x[ordinal6];
			}
			T localValue5 = A[strideTerm4 + ordinal5];
			x[ordinal5] = ((localValue5 != T.Zero) ? (localValue4 / localValue5) : T.Zero);
		}
	}
}
