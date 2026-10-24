using System;
using System.Numerics;

namespace Utility.Matrices;

public static class DenseMatrix<T> where T : unmanaged, INumber<T>
{
	public static T[,] Multiply(T[,] A, T[,] B)
	{
		int length = A.GetLength(0);
		int length2 = A.GetLength(1);
		int length3 = B.GetLength(0);
		int length4 = B.GetLength(1);
		if (length2 != length3)
		{
			throw new InvalidOperationException("Product is undefined. n columns of first matrix must equal to n rows of second matrix");
		}
		T[,] rows = new T[length, length4];
		for (int i = 0; i < length; i++)
		{
			for (int j = 0; j < length4; j++)
			{
				for (int k = 0; k < length2; k++)
				{
					T[,] rows2 = rows;
					int ordinal = i;
					int ordinal2 = j;
					rows2[ordinal, ordinal2] += A[i, k] * B[k, j];
				}
			}
		}
		return rows;
	}

	public static T[,] TransposeMultiply(T[,] A, T[,] B)
	{
		int length = A.GetLength(0);
		int length2 = A.GetLength(1);
		T[,] rows = new T[length2, length2];
		for (int i = 0; i < length2; i++)
		{
			for (int j = 0; j < length2; j++)
			{
				rows[j, i] = T.Zero;
				for (int k = 0; k < length; k++)
				{
					rows[j, i] += A[k, j] * B[k, i];
				}
			}
		}
		return rows;
	}

	public static T[] TransposeMultiply(T[,] A, T[] b)
	{
		int length = A.GetLength(0);
		int length2 = A.GetLength(1);
		T[] values = new T[length2];
		for (int i = 0; i < length2; i++)
		{
			values[i] = T.Zero;
			for (int j = 0; j < length; j++)
			{
				values[i] += A[j, i] * b[j];
			}
		}
		return values;
	}

	public static double[,] Transpose(double[,] A)
	{
		int length = A.GetLength(0);
		int length2 = A.GetLength(1);
		double[,] doubleValues = new double[length2, length];
		for (int i = 0; i < length; i++)
		{
			for (int j = 0; j < length2; j++)
			{
				doubleValues[j, i] = A[i, j];
			}
		}
		return doubleValues;
	}
}
