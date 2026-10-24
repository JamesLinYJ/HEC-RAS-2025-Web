using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Utility.Logging;

namespace Utility.Matrices;

public class PGMRES<T> : MatrixSolver<T>, IMatrixSolver<T> where T : unmanaged, IFloatingPointIeee754<T>, IRootFunctions<T>
{
	public int InnerIterMax = 10;

	private int _IterMax;

	public T ToleranceAbs = T.CreateSaturating(1E-06);

	public T ToleranceRel = T.CreateSaturating(0.001);

	private int _m;

	private int _n;

	private T[] c;

	private T[] g;

	private T[,] h;

	private T[] l;

	private T[] r;

	private T[] s;

	private T[] t;

	private T[][] v;

	private T[] y;

	public string SolverName => "PGMRES";

	public bool IsDirectSolver => false;

	public override OperationResult Prepare(SparseMatrix<T> A, IPreconditioner<T> precond, PartialSolve partSolve = null)
	{
		OperationResult operationResult = base.Prepare(A, precond, partSolve);
		if (!operationResult)
		{
			return operationResult;
		}
		_n = Matrix.RowCount;
		EnsureCapacity(Matrix.MaxRows, Matrix.MaxRows, Matrix.NonZeroCount + 1, Matrix.MaxNonZero + 1);
		_IterMax = Convergence.MaxIterations;
		return OperationResult.Success();
	}

	public OperationResult Configure(object options)
	{
		if (options == null || !(options is PgmresOptions pgmresOptions))
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverOptionsError(SolverName));
		}
		if (pgmresOptions.MatrixConv != null && pgmresOptions.MatrixConv is MatrixConvergence<T> convergence)
		{
			Convergence = convergence;
		}
		MatrixSolver<double>.ScalingType scalingType = pgmresOptions.ScalingType;
		if (scalingType is ScalingType)
		{
			ScalingType scaling = (ScalingType)scalingType;
			Scaling = scaling;
		}
		InnerIterMax = pgmresOptions.MaxInnerIterations;
		return OperationResult.Success();
	}

	public void EnsureCapacity(int requiredRows, int rowCap, int requiredNnz, int nnzCap)
	{
		if (c == null || c.Length < InnerIterMax + 1)
		{
			c = new T[InnerIterMax + 1];
			g = new T[InnerIterMax + 1];
			h = new T[InnerIterMax + 1, InnerIterMax];
			s = new T[InnerIterMax + 1];
			y = new T[InnerIterMax + 1];
		}
		BufferCapacity.EnsureArrayCapacity(ref l, requiredNnz, nnzCap);
		BufferCapacity.EnsureArrayCapacity(ref r, requiredRows, rowCap);
		BufferCapacity.EnsureArrayCapacity(ref t, requiredRows, rowCap);
		if (v == null || v.Length < InnerIterMax + 1)
		{
			v = new T[InnerIterMax + 1][];
		}
		for (int i = 0; i <= InnerIterMax; i++)
		{
			BufferCapacity.EnsureArrayCapacity(ref v[i], requiredRows, rowCap);
		}
		_m = r.Length;
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
		operationResult.MergeIn(Prepare(A, precond, partSolve));
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
		operationResult.MergeIn(Update(A));
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult.MergeIn(Solve(x, b));
		return operationResult;
	}

	public OperationResult Solve(T[] x, T[] b)
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
		operationResult = SolveInternal();
		PostSolve(x);
		return operationResult;
	}

	private OperationResult SolveInternal()
	{
		T saturating = T.CreateSaturating(0.001);
		int ordinal = 0;
		T zero = T.Zero;
		SparseMatrix<T>.Residuals(Matrix, Solution, RightHandSide, r, SolverParallelOptions);
		Convergence.Reset();
		if (ComputeConvergence(r))
		{
			return OperationResult.Success();
		}
		Convergence.Iterations = 1;
		T rho;
		while (Convergence.Iterations <= Convergence.MaxIterations)
		{
			Array.Copy(r, 0, t, 0, _n);
			Preconditioner.Apply(r, t);
			rho = T.Sqrt(SparseMatrix<T>.DotProduct(r, r));
			Parallel.SmartFor(_n, (int start, int stop) =>
			{
				for (int i = start; i <= stop; i++)
				{
					v[0][i] = r[i] / rho;
				}
			}, SolverParallelOptions, 3000);
			Array.Clear(g, 0, g.Length);
			g[0] = rho;
			int count32;
			for (count32 = 0; count32 < InnerIterMax + 1; count32++)
			{
				for (int intervalIndex = 0; intervalIndex < InnerIterMax; intervalIndex++)
				{
					h[count32, intervalIndex] = T.Zero;
				}
			}
			int count = 0;
			int count2;
			for (count2 = 0; count2 < InnerIterMax; count2++)
			{
				count = count2;
				SparseMatrix<T>.Product(Matrix, v[count2], v[count2 + 1], SolverParallelOptions);
				Array.Copy(v[count2 + 1], 0, t, 0, _n);
				Preconditioner.Apply(v[count2 + 1], t);
				T sqrtDotProduct = T.Sqrt(SparseMatrix<T>.DotProduct(v[count2 + 1], v[count2 + 1]));
				for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
				{
					h[intervalIndex2, count2] = SparseMatrix<T>.DotProduct(v[count2 + 1], v[intervalIndex2]);
					for (count32 = 0; count32 < _n; count32++)
					{
						T[] values = v[count2 + 1];
						int count2Offset = count32;
						values[count2Offset] -= h[intervalIndex2, count2] * v[intervalIndex2][count32];
					}
				}
				h[count2 + 1, count2] = T.Sqrt(SparseMatrix<T>.DotProduct(v[count2 + 1], v[count2 + 1]));
				if (sqrtDotProduct + saturating * h[count2 + 1, count2] == sqrtDotProduct)
				{
					for (int intervalIndex3 = 0; intervalIndex3 < count2 + 1; intervalIndex3++)
					{
						T dotProduct = SparseMatrix<T>.DotProduct(v[count2 + 1], v[intervalIndex3]);
						T[,] rows = h;
						int ordinal2 = intervalIndex3;
						int count3 = count2;
						rows[ordinal2, count3] += dotProduct;
						for (count32 = 0; count32 < _n; count32++)
						{
							T[] valuesArray = v[count2 + 1];
							count3 = count32;
							valuesArray[count3] -= dotProduct * v[intervalIndex3][count32];
						}
					}
					h[count2 + 1, count2] = T.Sqrt(SparseMatrix<T>.DotProduct(v[count2 + 1], v[count2 + 1]));
				}
				if (h[count2 + 1, count2] != T.Zero)
				{
					for (count32 = 0; count32 < _n; count32++)
					{
						T[] values2 = v[count2 + 1];
						int count2Offset2 = count32;
						values2[count2Offset2] /= h[count2 + 1, count2];
					}
				}
				if (0 < count2)
				{
					for (count32 = 0; count32 < count2 + 2; count32++)
					{
						y[count32] = h[count32, count2];
					}
					for (int intervalIndex4 = 0; intervalIndex4 < count2; intervalIndex4++)
					{
						MultGivens(c[intervalIndex4], s[intervalIndex4], intervalIndex4, y);
					}
					for (count32 = 0; count32 < count2 + 2; count32++)
					{
						h[count32, count2] = y[count32];
					}
				}
				T count2Term = T.Sqrt(h[count2, count2] * h[count2, count2] + h[count2 + 1, count2] * h[count2 + 1, count2]);
				c[count2] = h[count2, count2] / count2Term;
				s[count2] = -h[count2 + 1, count2] / count2Term;
				h[count2, count2] = c[count2] * h[count2, count2] - s[count2] * h[count2 + 1, count2];
				h[count2 + 1, count2] = T.Zero;
				MultGivens(c[count2], s[count2], count2, g);
				rho = T.Abs(g[count2 + 1]);
				ordinal++;
				if (rho <= zero && rho <= ToleranceAbs)
				{
					break;
				}
			}
			count2 = count;
			y[count2] = g[count2] / h[count2, count2];
			count32 = count2 - 1;
			while (0 <= count32)
			{
				y[count32] = g[count32];
				T[] values3;
				int intervalIndex5;
				for (int ordinal3 = count32 + 1; ordinal3 < count2 + 1; ordinal3++)
				{
					values3 = y;
					intervalIndex5 = count32;
					values3[intervalIndex5] -= h[count32, ordinal3] * y[ordinal3];
				}
				values3 = y;
				intervalIndex5 = count32;
				values3[intervalIndex5] /= h[count32, count32];
				count32--;
			}
			for (count32 = 0; count32 < _n; count32++)
			{
				for (int intervalIndex6 = 0; intervalIndex6 < count2 + 1; intervalIndex6++)
				{
					T[] solution = Solution;
					int count2Offset3 = count32;
					solution[count2Offset3] += v[intervalIndex6][count32] * y[intervalIndex6];
				}
			}
			SparseMatrix<T>.Residuals(Matrix, Solution, RightHandSide, r, SolverParallelOptions);
			if (ComputeConvergence(r))
			{
				break;
			}
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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void MultGivens(T c, T s, int k, T[] g)
	{
		T localValue = c * g[k] - s * g[k + 1];
		T localValue2 = s * g[k] + c * g[k + 1];
		g[k] = localValue;
		g[k + 1] = localValue2;
	}

	public OperationResult Destroy(SparseMatrix<T> A)
	{
		return OperationResult.Success();
	}
}
