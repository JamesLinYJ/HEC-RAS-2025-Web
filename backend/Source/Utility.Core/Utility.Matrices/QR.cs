using System;
using System.Numerics;

namespace Utility.Matrices;

public class QR<T> where T : unmanaged, IFloatingPoint<T>, IRootFunctions<T>
{
	public static void Solve(T[,] A, T[] x, T[] b)
	{
		Factorize(A, b);
		BackSubstitute(A, b, x);
	}

	public static void Factorize(T[,] A, T[] b)
	{
		int length = A.GetLength(0);
		int length2 = A.GetLength(1);
		int count = Math.Min(length - 1, length2);
		for (int i = 0; i < count; i++)
		{
			for (int j = i + 1; j < length; j++)
			{
				T localValue = A[i, i];
				T localValue2 = A[j, i];
				if (!(localValue2 == T.Zero))
				{
					T localValue3 = T.Sqrt(localValue * localValue + localValue2 * localValue2);
					T localValue4 = localValue / localValue3;
					T localValue5 = localValue2 / localValue3;
					for (int k = i; k < length2; k++)
					{
						T localValue6 = A[i, k];
						T localValue7 = A[j, k];
						A[i, k] = localValue4 * localValue6 + localValue5 * localValue7;
						A[j, k] = -localValue5 * localValue6 + localValue4 * localValue7;
					}
					T localValue8 = b[i];
					T localValue9 = b[j];
					b[i] = localValue4 * localValue8 + localValue5 * localValue9;
					b[j] = -localValue5 * localValue8 + localValue4 * localValue9;
				}
			}
		}
	}

	public static void BackSubstitute(T[,] R, T[] b, T[] x)
	{
		int count = x.Length;
		for (int countOffset = count - 1; countOffset >= 0; countOffset--)
		{
			T localValue = b[countOffset];
			for (int i = countOffset + 1; i < count; i++)
			{
				localValue -= R[countOffset, i] * x[i];
			}
			x[countOffset] = localValue / R[countOffset, countOffset];
		}
	}

	public static void Solve(Span<T> A, int nRows, int nCols, Span<T> x, Span<T> b, T diagTolerance = default(T))
	{
		Factorize(A, nRows, nCols, b);
		BackSubstitute(A, nCols, b, x, diagTolerance);
	}

	public static void Factorize(Span<T> A, int nRows, int nCols, Span<T> b)
	{
		int count = Math.Min(nRows - 1, nCols);
		for (int i = 0; i < count; i++)
		{
			for (int j = i + 1; j < nRows; j++)
			{
				T nColsTerm = A[i * nCols + i];
				T nColsTerm2 = A[j * nCols + i];
				if (!(nColsTerm2 == T.Zero))
				{
					T localValue = T.Sqrt(nColsTerm * nColsTerm + nColsTerm2 * nColsTerm2);
					T localValue2 = nColsTerm / localValue;
					T localValue3 = nColsTerm2 / localValue;
					for (int k = i; k < nCols; k++)
					{
						T nColsTerm3 = A[i * nCols + k];
						T nColsTerm4 = A[j * nCols + k];
						A[i * nCols + k] = localValue2 * nColsTerm3 + localValue3 * nColsTerm4;
						A[j * nCols + k] = -localValue3 * nColsTerm3 + localValue2 * nColsTerm4;
					}
					T localValue4 = b[i];
					T localValue5 = b[j];
					b[i] = localValue2 * localValue4 + localValue3 * localValue5;
					b[j] = -localValue3 * localValue4 + localValue2 * localValue5;
				}
			}
		}
	}

	public static void BackSubstitute(ReadOnlySpan<T> R, int nCols, ReadOnlySpan<T> b, Span<T> x, T diagTolerance = default(T))
	{
		int length = x.Length;
		for (int lengthOffset = length - 1; lengthOffset >= 0; lengthOffset--)
		{
			T localValue = b[lengthOffset];
			for (int i = lengthOffset + 1; i < length; i++)
			{
				localValue -= R[lengthOffset * nCols + i] * x[i];
			}
			T diagToleranceTerm = R[lengthOffset * nCols + lengthOffset];
			if (diagToleranceTerm < diagTolerance)
			{
				diagToleranceTerm = diagTolerance;
			}
			x[lengthOffset] = localValue / diagToleranceTerm;
		}
	}
}
