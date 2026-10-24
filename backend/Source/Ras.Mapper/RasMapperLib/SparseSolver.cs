using System;
using System.Linq;

namespace RasMapperLib;

public class SparseSolver
{
	public enum enumConvergenceResult
	{
		Converged,
		NotConverged,
		Crashed,
		NoUnknowns
	}

	private int _n_1;

	private int[] _ija;

	private double[] _sa;

	private double[] _p;

	private double[] _pp;

	private double[] _r;

	private double[] _rr;

	private double[] _z;

	private double[] _zz;

	private int _iterations;

	private double _residual;

	private enumConvergenceResult _convergence;

	public int Iterations => _iterations;

	public double Residual => _residual;

	public string ConvergenceResultMessage
	{
		get
		{
			if (_convergence == enumConvergenceResult.Converged)
			{
				return "Solution converged on iteration: " + Iterations;
			}
			if (_convergence == enumConvergenceResult.NotConverged)
			{
				return "Solution solved but not to the specified tolerance with " + Iterations + " iterations";
			}
			if (_convergence == enumConvergenceResult.Crashed)
			{
				return "Solution crashed on iteration: " + Iterations;
			}
			return "";
		}
	}

	public SparseSolver()
	{
		_p = null;
		_pp = null;
		_r = null;
		_rr = null;
		_z = null;
		_zz = null;
	}

	public void CreateSparseMatrix(int sizeA, double[,] A)
	{
		checked
		{
			_n_1 = sizeA - 1;
			int ordinal = 0;
			int ordinal2 = 0;
			int n_ = _n_1;
			for (int i = 0; i <= n_; i++)
			{
				int n_2 = _n_1;
				for (int j = 0; j <= n_2; j++)
				{
					if (A[i, j] != 0.0)
					{
						if (i == j)
						{
							ordinal++;
						}
						else
						{
							ordinal2++;
						}
					}
				}
			}
			int n_1Term = ordinal2 + _n_1 + 1;
			_ija = new int[n_1Term + 1];
			_sa = new double[n_1Term + 1];
			int n_3 = _n_1;
			for (int k = 0; k <= n_3; k++)
			{
				_sa[k] = A[k, k];
			}
			_ija[0] = _n_1 + 2;
			int n_1Offset = _n_1 + 1;
			int n_4 = _n_1;
			for (int l = 0; l <= n_4; l++)
			{
				int n_5 = _n_1;
				for (int m = 0; m <= n_5; m++)
				{
					if (A[l, m] != 0.0 && l != m)
					{
						n_1Offset++;
						_sa[n_1Offset] = A[l, m];
						_ija[n_1Offset] = m;
					}
				}
				_ija[l + 1] = n_1Offset + 1;
			}
		}
	}

	public void SetSparseMatrix(int sizeA, int[] ija, double[] sa)
	{
		_n_1 = checked(sizeA - 1);
		_ija = ija;
		_sa = sa;
	}

	private double ComputeNorm(double[] x)
	{
		double computed = 0.0;
		int n_ = _n_1;
		for (int i = 0; i <= n_; i = checked(i + 1))
		{
			computed += Math.Pow(x[i], 2.0);
		}
		return Math.Sqrt(computed);
	}

	private void ASolve(double[] b, double[] x)
	{
		int n_ = _n_1;
		for (int i = 0; i <= n_; i = checked(i + 1))
		{
			x[i] = b[i] / _sa[i];
		}
	}

	private void ATimesX(double[] x, double[] b)
	{
		int n_ = _n_1;
		checked
		{
			for (int i = 0; i <= n_; i++)
			{
				b[i] = _sa[i] * x[i];
				int ija = _ija[i];
				int count = _ija[i + 1] - 1;
				for (int j = ija; j <= count; j++)
				{
					b[i] += _sa[j] * x[_ija[j]];
				}
			}
		}
	}

	private void ATimesTX(double[] x, double[] b)
	{
		int n_ = _n_1;
		checked
		{
			for (int i = 0; i <= n_; i++)
			{
				b[i] = _sa[i] * x[i];
			}
			int n_2 = _n_1;
			for (int j = 0; j <= n_2; j++)
			{
				int ija = _ija[j];
				int count = _ija[j + 1] - 1;
				for (int k = ija; k <= count; k++)
				{
					int ija2 = _ija[k];
					b[ija2] += _sa[k] * x[j];
				}
			}
		}
	}

	public enumConvergenceResult SolveMatrix(double[] b, double tol, double[] x, int maxIterations = 0)
	{
		checked
		{
			if (maxIterations == 0)
			{
				maxIterations = _n_1 + 1;
			}
			if (_p == null || _p.Count() < x.Count())
			{
				_p = new double[_n_1 + 1];
				_pp = new double[_n_1 + 1];
				_r = new double[_n_1 + 1];
				_rr = new double[_n_1 + 1];
				_z = new double[_n_1 + 1];
				_zz = new double[_n_1 + 1];
			}
			ATimesX(x, _r);
			int n_ = _n_1;
			for (int i = 0; i <= n_; i++)
			{
				_r[i] = b[i] - _r[i];
				_rr[i] = _r[i];
			}
			double norm = ComputeNorm(b);
			if (norm == 0.0)
			{
				_iterations = -1;
				_residual = 0.0;
				_convergence = enumConvergenceResult.Converged;
				return _convergence;
			}
			ASolve(_r, _z);
			_residual = double.NaN;
			int maxIterations2 = maxIterations;
			double computed = default;
			for (int j = 1; j <= maxIterations2; j++)
			{
				ASolve(_rr, _zz);
				double computed2 = 0.0;
				int n_2 = _n_1;
				for (int k = 0; k <= n_2; k++)
				{
					computed2 += _z[k] * _rr[k];
				}
				if (j == 1)
				{
					int n_3 = _n_1;
					for (int l = 0; l <= n_3; l++)
					{
						_p[l] = _z[l];
						_pp[l] = _zz[l];
					}
				}
				else
				{
					double computed3 = computed2 / computed;
					int n_4 = _n_1;
					for (int m = 0; m <= n_4; m++)
					{
						_p[m] = computed3 * _p[m] + _z[m];
						_pp[m] = computed3 * _pp[m] + _zz[m];
					}
				}
				computed = computed2;
				ATimesX(_p, _z);
				double computed4 = 0.0;
				int n_5 = _n_1;
				for (int n = 0; n <= n_5; n++)
				{
					computed4 += _z[n] * _pp[n];
				}
				double computed5 = computed2 / computed4;
				ATimesTX(_pp, _zz);
				int n_6 = _n_1;
				for (int intervalIndex = 0; intervalIndex <= n_6; intervalIndex++)
				{
					x[intervalIndex] += computed5 * _p[intervalIndex];
					_r[intervalIndex] -= computed5 * _z[intervalIndex];
					_rr[intervalIndex] -= computed5 * _zz[intervalIndex];
				}
				ASolve(_r, _z);
				_iterations = j;
				_residual = ComputeNorm(_r) / norm;
				if (_residual < tol)
				{
					_convergence = enumConvergenceResult.Converged;
					return _convergence;
				}
				if (double.IsNaN(_residual))
				{
					_convergence = enumConvergenceResult.Crashed;
					return _convergence;
				}
			}
			_convergence = enumConvergenceResult.NotConverged;
			return _convergence;
		}
	}
}
