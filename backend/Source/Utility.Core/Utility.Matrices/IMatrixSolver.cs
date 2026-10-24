using System.Numerics;
using Utility.Logging;

namespace Utility.Matrices;

public interface IMatrixSolver<T> where T : unmanaged, IFloatingPointIeee754<T>, IRootFunctions<T>
{
	string SolverName { get; }

	bool IsDirectSolver { get; }

	OperationResult Configure(object options);

	OperationResult PrepareAndSolve(SparseMatrix<T> A, T[] x, T[] b, IPreconditioner<T> precond = null, PartialSolve partSolve = null);

	OperationResult Prepare(SparseMatrix<T> A, IPreconditioner<T> precond = null, PartialSolve partSolve = null);

	OperationResult Solve(SparseMatrix<T> A, T[] x, T[] b);

	OperationResult Update(SparseMatrix<T> A);

	OperationResult Solve(T[] x, T[] b);

	OperationResult Destroy(SparseMatrix<T> A);

	void SetThreadBudget(int numThreads)
	{
	}
}
