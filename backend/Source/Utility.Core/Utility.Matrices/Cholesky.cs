using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Utility.Matrices;

public static class Cholesky<T> where T : unmanaged, IFloatingPoint<T>, IRootFunctions<T>
{
	private const int StackPivotLimit = 256;

	public static void Solve(T[,] A, T[] x, T[] b, T diagTolerance = default(T))
	{
		if (A.Length != 0)
		{
			Solve(MemoryMarshal.CreateSpan(ref A[0, 0], A.Length), A.GetLength(0), x, b, diagTolerance);
		}
	}

	public static void Factorize(T[,] A, T diagTolerance = default(T))
	{
		if (A.Length != 0)
		{
			Factorize(MemoryMarshal.CreateSpan(ref A[0, 0], A.Length), A.GetLength(0), diagTolerance);
		}
	}

	public static void SolveLower(T[,] L, T[] b, T[] y)
	{
		if (L.Length != 0)
		{
			SolveLower(MemoryMarshal.CreateReadOnlySpan(in L[0, 0], L.Length), L.GetLength(0), b, y);
		}
	}

	public static void SolveUpperTransposed(T[,] L, T[] y)
	{
		if (L.Length != 0)
		{
			SolveUpperTransposed(MemoryMarshal.CreateReadOnlySpan(in L[0, 0], L.Length), L.GetLength(0), y);
		}
	}

	public static void Solve(Span<T> M, int n, Span<T> x, Span<T> b, T diagTolerance = default(T))
	{
		if (n <= 256)
		{
			Span<T> buffer = stackalloc T[n];
			FactorizeCore(M, n, buffer, diagTolerance);
			SolveLower(M, n, b, x, buffer);
			SolveUpperTransposed(M, n, x, buffer);
		}
		else
		{
			Factorize(M, n, diagTolerance);
			SolveLower(M, n, b, x);
			SolveUpperTransposed(M, n, x);
		}
	}

	public static void Factorize(Span<T> M, int n, T diagTolerance = default(T))
	{
		FactorizeCore(M, n, Span<T>.Empty, diagTolerance);
	}

	public static void Factorize(Span<T> M, int n, Span<T> inverseDiagonal, T diagTolerance = default(T))
	{
		FactorizeCore(M, n, inverseDiagonal.Slice(0, n), diagTolerance);
	}

	private static void FactorizeCore(Span<T> M, int n, Span<T> inverseDiagonal, T diagTolerance)
	{
		bool inverseDiagonalTerm = inverseDiagonal.Length >= n;
		for (int i = 0; i < n; i++)
		{
			int ordinal = i * n;
			Span<T> slice = M.Slice(ordinal, i);
			T localValue = M[ordinal + i];
			for (int j = 0; j < slice.Length; j++)
			{
				localValue -= slice[j] * slice[j];
			}
			if (localValue < diagTolerance)
			{
				localValue = diagTolerance;
			}
			localValue = T.Sqrt(localValue);
			M[ordinal + i] = localValue;
			T inverseDiagonal2 = T.One / localValue;
			if (inverseDiagonalTerm)
			{
				inverseDiagonal[i] = inverseDiagonal2;
			}
			for (int k = i + 1; k < n; k++)
			{
				int ordinal2 = k * n;
				Span<T> sliceSpan = M.Slice(ordinal2, i);
				T localValue2 = M[ordinal2 + i];
				for (int l = 0; l < sliceSpan.Length; l++)
				{
					localValue2 -= sliceSpan[l] * slice[l];
				}
				M[ordinal2 + i] = localValue2 * inverseDiagonal2;
			}
		}
	}

	public static void SolveLower(ReadOnlySpan<T> L, int n, ReadOnlySpan<T> b, Span<T> y)
	{
		for (int i = 0; i < n; i++)
		{
			int ordinal = i * n;
			ReadOnlySpan<T> slice = L.Slice(ordinal, i);
			Span<T> sliceSpan = y.Slice(0, i);
			T localValue = b[i];
			for (int j = 0; j < slice.Length; j++)
			{
				localValue -= slice[j] * sliceSpan[j];
			}
			y[i] = localValue / L[ordinal + i];
		}
	}

	public static void SolveLower(ReadOnlySpan<T> L, int n, ReadOnlySpan<T> b, Span<T> y, ReadOnlySpan<T> inverseDiagonal)
	{
		for (int i = 0; i < n; i++)
		{
			ReadOnlySpan<T> slice = L.Slice(i * n, i);
			Span<T> sliceSpan = y.Slice(0, i);
			T localValue = b[i];
			for (int j = 0; j < slice.Length; j++)
			{
				localValue -= slice[j] * sliceSpan[j];
			}
			y[i] = localValue * inverseDiagonal[i];
		}
	}

	public static void SolveUpperTransposed(ReadOnlySpan<T> L, int n, Span<T> y)
	{
		for (int ordinal = n - 1; ordinal >= 0; ordinal--)
		{
			int ordinal2 = ordinal * n;
			T localValue = y[ordinal] / L[ordinal2 + ordinal];
			y[ordinal] = localValue;
			ReadOnlySpan<T> slice = L.Slice(ordinal2, ordinal);
			Span<T> sliceSpan = y.Slice(0, ordinal);
			for (int i = 0; i < sliceSpan.Length; i++)
			{
				sliceSpan[i] -= slice[i] * localValue;
			}
		}
	}

	public static void SolveUpperTransposed(ReadOnlySpan<T> L, int n, Span<T> y, ReadOnlySpan<T> inverseDiagonal)
	{
		for (int ordinal = n - 1; ordinal >= 0; ordinal--)
		{
			T inverseDiagonalTerm = y[ordinal] * inverseDiagonal[ordinal];
			y[ordinal] = inverseDiagonalTerm;
			ReadOnlySpan<T> slice = L.Slice(ordinal * n, ordinal);
			Span<T> sliceSpan = y.Slice(0, ordinal);
			for (int i = 0; i < sliceSpan.Length; i++)
			{
				sliceSpan[i] -= slice[i] * inverseDiagonalTerm;
			}
		}
	}
}
