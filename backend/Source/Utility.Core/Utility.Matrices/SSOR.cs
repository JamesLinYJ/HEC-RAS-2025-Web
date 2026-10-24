using System;
using System.Numerics;
using Utility.Logging;

namespace Utility.Matrices;

public class SSOR<T> : MatrixSolver<T>, IMatrixSolver<T>, IPreconditioner<T> where T : unmanaged, IFloatingPointIeee754<T>, IRootFunctions<T>
{
	public SparseMatrix<T> M;

	public T[] r;

	private T[] _array;

	public T Relaxation = T.CreateSaturating(1.2);

	private int _m;

	private int _n;

	public string SolverName => "SSOR";

	public string PrecondName => "SSOR";

	public bool IsDirectSolver => false;

	public bool IsPreconditioner { get; set; }

	public SSOR()
	{
	}

	public SSOR(MatrixConvergence<T> convergence)
		: base(convergence)
	{
	}

	public OperationResult Configure(object options)
	{
		return OperationResult.Success();
	}

	public override OperationResult Prepare(SparseMatrix<T> A, IPreconditioner<T> precond, PartialSolve partSolve = null)
	{
		OperationResult operationResult = MatrixSolver<T>.ValidateMatrix(A, SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult = base.Prepare(A, precond, partSolve);
		if (!operationResult)
		{
			return operationResult;
		}
		IsPreconditioner = false;
		Initialize(A);
		return OperationResult.Success();
	}

	public virtual OperationResult Prepare(SparseMatrix<T> A)
	{
		if (A == null)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetNullMatrix(SolverName));
		}
		if (A.RowCount <= 0)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetInvalidMatrixDimension(SolverName, A.RowCount, A.NonZeroCount));
		}
		Convergence.Prepare();
		IsPreconditioner = true;
		Convergence.CheckConvergence = false;
		Convergence.MaxIterations = Convergence.PrecondIterations;
		Initialize(A);
		return OperationResult.Success();
	}

	private void Initialize(SparseMatrix<T> A)
	{
		M = A;
		if (r == null || _m < M.RowCount)
		{
			_m = M.MaxRows;
			r = new T[_m];
			_array = new T[_m];
		}
		_n = A.RowCount;
		for (int i = 0; i < _n; i++)
		{
			_array[i] = Relaxation / M.GetDataDiagonalRow(i);
		}
	}

	public OperationResult PrepareAndSolve(SparseMatrix<T> A, T[] x, T[] b, IPreconditioner<T> precond = null, PartialSolve partSolve = null)
	{
		OperationResult operationResult = MatrixSolver<T>.ValidateMatrix(A, SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult = MatrixSolver<T>.ValidateArray(x, "solution", A.RowCount, SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult = MatrixSolver<T>.ValidateArray(b, "right-hand-side", A.RowCount, SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult.MergeIn(base.Prepare(A, precond, partSolve));
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult.MergeIn(Solve(A, x, b));
		return operationResult;
	}

	public OperationResult Solve(SparseMatrix<T> A, T[] x, T[] b)
	{
		OperationResult operationResult = ValidatePrepared(SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult = MatrixSolver<T>.ValidateArray(x, "solution", M.RowCount, SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult = MatrixSolver<T>.ValidateArray(b, "right-hand-side", M.RowCount, SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		PreSolve(x, b);
		operationResult = SolveInternal();
		PostSolve(x);
		return operationResult;
	}

	public OperationResult Solve(T[] x, T[] b)
	{
		if (M == null)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverNotPrepared(SolverName));
		}
		if (x == null)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetNullArray(SolverName, "solution"));
		}
		if (b == null)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetNullArray(SolverName, "right-hand-side"));
		}
		if (x.Length < M.RowCount)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetArrayLengthMismatch(SolverName, "solution", x.Length, M.RowCount));
		}
		if (b.Length < M.RowCount)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetArrayLengthMismatch(SolverName, "right-hand-side", b.Length, M.RowCount));
		}
		if (!IsPreconditioner)
		{
			return Solve(Matrix, x, b);
		}
		Solution = x;
		RightHandSide = b;
		return SolveInternal();
	}

	public void Apply(T[] destination, T[] rhs)
	{
		if (M == null)
		{
			throw new InvalidOperationException("Preconditioner has not been prepared.");
		}
		if (destination.Length < _n)
		{
			throw new ArgumentException("Destination is shorter than the prepared matrix.", "destination");
		}
		if (rhs.Length < _n)
		{
			throw new ArgumentException("Right-hand side is shorter than the prepared matrix.", "rhs");
		}
		int[] rowPointers = M.RowPointers;
		int[] columnIndices = M.ColumnIndices;
		T[] values = M.Values;
		for (int i = 0; i < Convergence.MaxIterations; i++)
		{
			for (int j = 0; j < _n; j++)
			{
				T zero = T.Zero;
				for (int k = rowPointers[j]; k < rowPointers[j + 1]; k++)
				{
					zero += values[k] * destination[columnIndices[k]];
				}
				T[] destinationArray = destination;
				int ordinal = j;
				destinationArray[ordinal] += _array[j] * (rhs[j] - zero);
			}
			for (int ordinal2 = _n - 1; ordinal2 >= 0; ordinal2--)
			{
				T zero2 = T.Zero;
				for (int l = rowPointers[ordinal2]; l < rowPointers[ordinal2 + 1]; l++)
				{
					zero2 += values[l] * destination[columnIndices[l]];
				}
				T[] destination2 = destination;
				int ordinal3 = ordinal2;
				destination2[ordinal3] += _array[ordinal2] * (rhs[ordinal2] - zero2);
			}
		}
	}

	private OperationResult SolveInternal()
	{
		if (Convergence.CheckConvergence)
		{
			Convergence.Reset();
			SparseMatrix<T>.Residuals(M, Solution, RightHandSide, r, SolverParallelOptions);
			if (ComputeConvergence(r))
			{
				return OperationResult.Success();
			}
		}
		Convergence.Iterations = 1;
		while (Convergence.Iterations <= Convergence.MaxIterations)
		{
			for (int i = 0; i < _n; i++)
			{
				T[] solution = Solution;
				int ordinal = i;
				solution[ordinal] += _array[i] * (RightHandSide[i] - SparseMatrix<T>.ProductRow(M, Solution, i));
			}
			for (int ordinal2 = _n - 1; ordinal2 >= 0; ordinal2--)
			{
				T[] solution = Solution;
				int ordinal3 = ordinal2;
				solution[ordinal3] += _array[ordinal2] * (RightHandSide[ordinal2] - SparseMatrix<T>.ProductRow(M, Solution, ordinal2));
			}
			if (Convergence.CheckConvergence)
			{
				SparseMatrix<T>.Residuals(M, Solution, RightHandSide, r, SolverParallelOptions);
				if (ComputeConvergence(r))
				{
					break;
				}
			}
			Convergence.Iterations++;
		}
		return GetConvergenceResult();
	}

	private OperationResult GetConvergenceResult()
	{
		if (IsPreconditioner || !Convergence.CheckConvergence)
		{
			return OperationResult.Success();
		}
		switch (Convergence.Status)
		{
		case ConvergenceStatus.Converged:
		case ConvergenceStatus.SmallNorm:
			return OperationResult.Success();
		case ConvergenceStatus.MaxIterations:
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverMaxIterations(SolverName, Convergence.Iterations, Convergence.ErrorMax));
		case ConvergenceStatus.Divergent:
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverDiverged(SolverName, Convergence.Iterations, Convergence.ErrorMax));
		case ConvergenceStatus.Crashed:
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverCrashed(SolverName, Convergence.Iterations));
		default:
			return OperationResult.Success();
		}
	}

	public OperationResult Destroy(SparseMatrix<T> A)
	{
		return OperationResult.Success();
	}
}
