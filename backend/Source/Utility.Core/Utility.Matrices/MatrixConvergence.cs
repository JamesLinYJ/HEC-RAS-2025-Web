using System;
using System.Linq;
using System.Numerics;

namespace Utility.Matrices;

public class MatrixConvergence<T> where T : unmanaged, INumber<T>, IRootFunctions<T>
{
	public ConvergenceStatus Status = ConvergenceStatus.Iterating;

	public bool CheckConvergence = true;

	public T ToleranceRMSE = T.CreateSaturating(1E-06);

	public T ToleranceMAE = T.CreateSaturating(1E-05);

	public T ToleranceMaxMAE = T.CreateSaturating(0.001);

	public int MinIterations = 3;

	public int MaxIterations = 20;

	public int PrecondIterations = 5;

	public int Iterations;

	public T[] IterErrorRMS;

	public T[] IterErrorMax;

	public int[] IterLocationMax;

	public ulong[] HistoCountIter;

	public bool IsConverged => Status == ConvergenceStatus.Converged;

	public T ErrorRMSE0
	{
		get
		{
			if (IterErrorRMS.Length == 0)
			{
				return T.Zero;
			}
			return IterErrorRMS.Max();
		}
	}

	public T ErrorRMSE
	{
		get
		{
			if (IterErrorRMS.Length == 0)
			{
				return T.Zero;
			}
			return IterErrorRMS[Math.Min(Iterations, IterErrorRMS.Length - 1)];
		}
	}

	public T ErrorMax0
	{
		get
		{
			if (IterErrorMax.Length == 0)
			{
				return T.Zero;
			}
			return IterErrorMax[0];
		}
	}

	public T ErrorMax
	{
		get
		{
			if (IterErrorMax.Length == 0)
			{
				return T.Zero;
			}
			return IterErrorMax[Math.Min(Iterations, IterErrorMax.Length - 1)];
		}
	}

	public int LocationMAE0
	{
		get
		{
			if (IterLocationMax.Length == 0)
			{
				return 0;
			}
			return IterLocationMax[0];
		}
	}

	public int LocationMax
	{
		get
		{
			if (IterLocationMax.Length == 0)
			{
				return 0;
			}
			return IterLocationMax[Math.Min(Iterations, IterLocationMax.Length - 1)];
		}
	}

	public void Prepare()
	{
		Allocate();
		Iterations = 0;
	}

	public void Allocate()
	{
		if (IterErrorRMS == null || IterErrorRMS.Length != MaxIterations + 1)
		{
			IterErrorRMS = new T[MaxIterations + 1];
			IterErrorMax = new T[MaxIterations + 1];
			IterLocationMax = new int[MaxIterations + 1];
			HistoCountIter = new ulong[MaxIterations + 1];
		}
	}

	public void Reset()
	{
		Status = ConvergenceStatus.Iterating;
		Iterations = 0;
		T zero = T.Zero;
		for (int i = 0; i <= MaxIterations; i++)
		{
			IterErrorRMS[i] = zero;
			IterErrorMax[i] = zero;
			IterLocationMax[i] = 0;
		}
	}

	public bool Compute(T[] err)
	{
		return Compute(err, err.Length);
	}

	public bool Compute(T[] err, int n)
	{
		if (!CheckConvergence)
		{
			return false;
		}
		T saturating = T.CreateSaturating(100.0);
		T saturating2 = T.CreateSaturating(1E-12);
		ComputeMAE(err, n);
		if (T.IsNaN(ErrorMax) || T.IsNaN(ErrorRMSE))
		{
			Status = ConvergenceStatus.Crashed;
			return true;
		}
		if (ErrorMax < saturating2)
		{
			Status = ConvergenceStatus.SmallNorm;
			return true;
		}
		Status = ConvergenceStatus.Iterating;
		if (Iterations < MinIterations)
		{
			return false;
		}
		if (ErrorMax > saturating * ErrorMax0)
		{
			Status = ConvergenceStatus.Divergent;
			return true;
		}
		if (ErrorMax <= ToleranceMAE)
		{
			Status = ConvergenceStatus.Converged;
			HistoCountIter[Iterations]++;
			return true;
		}
		if (ErrorMax <= ToleranceMaxMAE)
		{
			ComputeRMSE(err, n);
			if (ErrorRMSE <= ToleranceRMSE)
			{
				Status = ConvergenceStatus.Converged;
				HistoCountIter[Iterations]++;
				return true;
			}
		}
		if (Iterations == MaxIterations)
		{
			Status = ConvergenceStatus.MaxIterations;
			HistoCountIter[Iterations]++;
			return true;
		}
		return false;
	}

	private void ComputeRMSE(T[] err, int n)
	{
		ReadOnlySpan<T> errSpan = err.AsSpan(0, n);
		T x = SparseMatrix<T>.DotProduct(errSpan, errSpan);
		x /= T.CreateSaturating(n);
		x = T.Sqrt(x);
		IterErrorRMS[Iterations] = x;
	}

	private void ComputeMAE(T[] err, int n)
	{
		T iterErrorMax = T.CreateSaturating(-10000000000.0);
		int iterLocationMax = 0;
		for (int i = 0; i < n; i++)
		{
			T absoluteErr = T.Abs(err[i]);
			if (absoluteErr > iterErrorMax)
			{
				iterErrorMax = absoluteErr;
				iterLocationMax = i;
			}
		}
		IterErrorMax[Iterations] = iterErrorMax;
		IterLocationMax[Iterations] = iterLocationMax;
	}
}
