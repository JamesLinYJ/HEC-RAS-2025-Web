using System;
using System.Numerics;

namespace Utility.Matrices;

public sealed class MIC<T> : IC<T> where T : unmanaged, IFloatingPoint<T>, IRootFunctions<T>
{
	public T ModificationParameter { get; }

	public T MinDiagonalRatio { get; }

	public override string PrecondName => "MIC";

	public MIC()
		: this(T.CreateSaturating(0.95), T.CreateSaturating(0.25))
	{
	}

	public MIC(T modificationParameter)
		: this(modificationParameter, T.CreateSaturating(0.25))
	{
	}

	public MIC(T modificationParameter, T minDiagonalRatio)
	{
		if (!T.IsFinite(modificationParameter) || modificationParameter < T.Zero || modificationParameter > T.One)
		{
			throw new ArgumentOutOfRangeException("modificationParameter");
		}
		if (!T.IsFinite(minDiagonalRatio) || minDiagonalRatio <= T.Zero || minDiagonalRatio > T.One)
		{
			throw new ArgumentOutOfRangeException("minDiagonalRatio");
		}
		ModificationParameter = modificationParameter;
		MinDiagonalRatio = minDiagonalRatio;
	}

	protected override bool Factorize(SparseMatrix<T> A)
	{
		int rowCount = M.RowCount;
		int nonZeroCount = M.NonZeroCount;
		T[] values = M.Values;
		T[] values2 = A.Values;
		int[] diagonalPointers = M.DiagonalPointers;
		int[] rowPointers = M.RowPointers;
		int[] columnIndices = M.ColumnIndices;
		int[] transposePointers = M.TransposePointers;
		Span<T> inverseDiagonal = InverseDiagonal;
		Array.Copy(values2, values, nonZeroCount);
		for (int i = 0; i < rowCount; i++)
		{
			int diagonalPointer = diagonalPointers[i];
			int rowPointer = rowPointers[i + 1];
			T values22 = values2[diagonalPointer];
			if (!T.IsFinite(values22) || values22 <= T.Zero)
			{
				return false;
			}
			T values22Term = values[diagonalPointer];
			T minDiagonalRatioTimesValues22 = MinDiagonalRatio * values22;
			if (!T.IsFinite(values22Term) || values22Term < minDiagonalRatioTimesValues22)
			{
				values22Term = values22;
			}
			T localValue = T.Sqrt(values22Term);
			if (!T.IsFinite(localValue) || localValue <= T.Zero)
			{
				return false;
			}
			values[diagonalPointer] = localValue;
			T inverseDiagonal2 = T.One / localValue;
			inverseDiagonal[i] = inverseDiagonal2;
			for (int j = diagonalPointer + 1; j < rowPointer; j++)
			{
				T inverseDiagonal2Term = values[j] * inverseDiagonal2;
				if (!T.IsFinite(inverseDiagonal2Term))
				{
					return false;
				}
				values[j] = inverseDiagonal2Term;
				values[transposePointers[j]] = inverseDiagonal2Term;
			}
			for (int k = diagonalPointer + 1; k < rowPointer; k++)
			{
				int columnIndex = columnIndices[k];
				T localValue2 = values[k];
				T zero = T.Zero;
				int l = k;
				int m = diagonalPointers[columnIndex];
				int rowPointer2 = rowPointers[columnIndex + 1];
				T[] valuesArray;
				int intervalIndex;
				for (; l < rowPointer; l++)
				{
					int columnIndex2;
					for (columnIndex2 = columnIndices[l]; m < rowPointer2 && columnIndices[m] < columnIndex2; m++)
					{
					}
					T localValue3 = localValue2 * values[l];
					if (m < rowPointer2 && columnIndices[m] == columnIndex2)
					{
						valuesArray = values;
						intervalIndex = m;
						valuesArray[intervalIndex] -= localValue3;
						m++;
					}
					else
					{
						zero += localValue3;
						valuesArray = values;
						intervalIndex = diagonalPointers[columnIndex2];
						valuesArray[intervalIndex] -= ModificationParameter * localValue3;
					}
				}
				valuesArray = values;
				intervalIndex = diagonalPointers[columnIndex];
				valuesArray[intervalIndex] -= ModificationParameter * zero;
			}
		}
		return true;
	}
}
