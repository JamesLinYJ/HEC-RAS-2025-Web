using System;
using System.Numerics;
using Utility.Logging;
using Utility.Progress;

namespace Utility.Matrices;

public class PCG<T> : MatrixSolver<T>, IMatrixSolver<T> where T : unmanaged, IFloatingPointIeee754<T>, IRootFunctions<T>
{
	private const int VectorLoopSize = 10000;

	private T[] _r;

	private T[] _z;

	private T[] _p;

	private T[] _t;

	public string SolverName => "PCG";

	public bool IsDirectSolver => false;

	public PCG()
	{
		Convergence.MaxIterations = 20;
	}

	public PCG(MatrixConvergence<T> convergence)
		: base(convergence)
	{
	}

	public OperationResult Configure(object options)
	{
		if (!(options is PcgManagedOptions pcgManagedOptions))
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverOptionsError(SolverName));
		}
		if (pcgManagedOptions.MatrixConv != null && pcgManagedOptions.MatrixConv is MatrixConvergence<T> convergence)
		{
			Convergence = convergence;
		}
		MatrixSolver<double>.ScalingType scalingType = pcgManagedOptions.ScalingType;
		if (scalingType is ScalingType)
		{
			ScalingType scaling = (ScalingType)scalingType;
			Scaling = scaling;
		}
		return OperationResult.Success();
	}

	public override OperationResult Prepare(SparseMatrix<T> A, IPreconditioner<T> precond, PartialSolve partSolve = null)
	{
		OperationResult operationResult = base.Prepare(A, precond, partSolve);
		if (!operationResult)
		{
			return operationResult;
		}
		EnsureCapacity(A.MaxRows, A.MaxRows);
		return OperationResult.Success();
	}

	public void EnsureCapacity(int requiredRows, int rowCap)
	{
		BufferCapacity.EnsureArrayCapacity(ref _r, requiredRows, rowCap);
		BufferCapacity.EnsureArrayCapacity(ref _z, requiredRows, rowCap);
		BufferCapacity.EnsureArrayCapacity(ref _p, requiredRows, rowCap);
		BufferCapacity.EnsureArrayCapacity(ref _t, requiredRows, rowCap);
	}

	public OperationResult PrepareAndSolve(SparseMatrix<T> A, T[] x, T[] b, IPreconditioner<T> precond = null, PartialSolve partSolve = null, ProgressReporter cancelReporter = null)
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
		operationResult = Prepare(A, precond, partSolve);
		if (!operationResult)
		{
			return operationResult;
		}
		return Solve(A, x, b, cancelReporter);
	}

	OperationResult IMatrixSolver<T>.PrepareAndSolve(SparseMatrix<T> A, T[] x, T[] b, IPreconditioner<T> precond, PartialSolve partSolve)
	{
		return PrepareAndSolve(A, x, b, precond, partSolve);
	}

	public OperationResult Solve(SparseMatrix<T> A, T[] x, T[] b)
	{
		return Solve(A, x, b, null);
	}

	public OperationResult Solve(SparseMatrix<T> A, T[] x, T[] b, ProgressReporter cancelReporter)
	{
		OperationResult operationResult = ValidatePrepared(SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult = MatrixSolver<T>.ValidateMatrix(A, SolverName);
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
		operationResult = Update(A);
		if (!operationResult)
		{
			return operationResult;
		}
		return Solve(x, b, cancelReporter);
	}

	public OperationResult Solve(T[] x, T[] b)
	{
		return Solve(x, b, null);
	}

	public OperationResult Solve(T[] x, T[] b, ProgressReporter cancelReporter)
	{
		OperationResult operationResult = ValidatePrepared(SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult = MatrixSolver<T>.ValidateArray(x, "solution", Matrix.RowCount, SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult = MatrixSolver<T>.ValidateArray(b, "right-hand-side", Matrix.RowCount, SolverName);
		if (!operationResult)
		{
			return operationResult;
		}
		PreSolve(x, b);
		operationResult = SolveInternal(cancelReporter);
		PostSolve(x);
		return operationResult;
	}

	private OperationResult SolveInternal(ProgressReporter cancelReporter)
	{
		SparseMatrix<T> matrix = Matrix;
		int rowCount = matrix.RowCount;
		T[] x = Solution;
		T[] r = _r;
		T[] p = _p;
		T[] t = _t;
		bool preconditionerTerm = Preconditioner is NoPreconditioner<T>;
		T[] z = (preconditionerTerm ? r : _z);
		SparseMatrix<T>.ResidualsLinearSystem(matrix, x, RightHandSide, r, SolverParallelOptions);
		Convergence.Reset();
		if (ComputeConvergence(r))
		{
			return GetConvergenceResult();
		}
		if (!preconditionerTerm)
		{
			Preconditioner.Apply(z, r);
		}
		Array.Copy(z, 0, p, 0, rowCount);
		T dotProduct = SparseMatrix<T>.DotProduct(r.AsSpan(0, rowCount), z.AsSpan(0, rowCount));
		T alpha = T.Zero;
		T beta = T.Zero;
		Action<int, int> startToEndSubLoop = (int start, int stop) =>
		{
			T alpha2 = alpha;
			for (int i = start; i <= stop; i++)
			{
				T[] values = x;
				int ordinal = i;
				values[ordinal] += alpha2 * p[i];
				values = r;
				ordinal = i;
				values[ordinal] -= alpha2 * t[i];
			}
		};
		Action<int, int> startToEndSubLoop2 = (int start, int stop) =>
		{
			T beta2 = beta;
			for (int i = start; i <= stop; i++)
			{
				p[i] = z[i] + beta2 * p[i];
			}
		};
		Convergence.Iterations = 1;
		while (Convergence.Iterations <= Convergence.MaxIterations)
		{
			SparseMatrix<T>.ProductMatrixVector(matrix, p, t, SolverParallelOptions);
			alpha = dotProduct / SparseMatrix<T>.DotProduct(p.AsSpan(0, rowCount), t.AsSpan(0, rowCount));
			Parallel.SmartFor(rowCount, startToEndSubLoop, SolverParallelOptions, 10000);
			if (ComputeConvergence(r))
			{
				break;
			}
			if (cancelReporter != null && cancelReporter.CancelRequested)
			{
				throw new OperationCanceledException();
			}
			if (!preconditionerTerm)
			{
				Preconditioner.Apply(z, r);
			}
			T dotProduct2 = SparseMatrix<T>.DotProduct(r.AsSpan(0, rowCount), z.AsSpan(0, rowCount));
			beta = dotProduct2 / dotProduct;
			dotProduct = dotProduct2;
			Parallel.SmartFor(rowCount, startToEndSubLoop2, SolverParallelOptions, 10000);
			Convergence.Iterations++;
		}
		return GetConvergenceResult();
	}

	private OperationResult GetConvergenceResult()
	{
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
