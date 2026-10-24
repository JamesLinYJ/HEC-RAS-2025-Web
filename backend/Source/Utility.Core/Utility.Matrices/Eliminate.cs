using System.Collections.Generic;
using System.Numerics;

namespace Utility.Matrices;

public class Eliminate<T> where T : unmanaged, INumber<T>
{
	public static void Compute(SparseMatrix<T> A, T[] rhs, out int[] indexMap, out T[] rhsBound, out SparseMatrix<T> AFree, out T[] rhsFree)
	{
		bool[] boolValues = new bool[rhs.Length];
		for (int i = 0; i < rhs.Length; i++)
		{
			boolValues[i] = false;
		}
		int count = 0;
		int rowPointerStepSum = 0;
		for (int i = 0; i < A.RowCount; i++)
		{
			if (A.RowPointers[i] + 1 == A.RowPointers[i + 1])
			{
				int rowPointer = A.RowPointers[i];
				int columnIndex = A.ColumnIndices[rowPointer];
				if (!(A.Values[rowPointer] == T.Zero))
				{
					count++;
					boolValues[columnIndex] = true;
				}
			}
			else
			{
				rowPointerStepSum += A.RowPointers[i + 1] - A.RowPointers[i];
			}
		}
		rhsBound = new T[count];
		int indexMap2 = 0;
		int ordinal = 0;
		indexMap = new int[A.RowCount];
		for (int i = 0; i < A.RowCount; i++)
		{
			if (boolValues[i])
			{
				int rowPointer2 = A.RowPointers[i];
				T rowPointer2Term = A.Values[rowPointer2];
				indexMap[i] = ~ordinal;
				rhsBound[ordinal] = rhs[i] / rowPointer2Term;
				ordinal++;
			}
			else
			{
				indexMap[i] = indexMap2;
				indexMap2++;
			}
		}
		List<T> items = new List<T>();
		List<int> intList = new List<int>();
		AFree = new SparseMatrix<T>(A.RowCount - count, rowPointerStepSum);
		rhsFree = new T[A.RowCount - count];
		for (int i = 0; i < A.RowCount; i++)
		{
			if (boolValues[i])
			{
				continue;
			}
			items.Clear();
			intList.Clear();
			rhsFree[indexMap[i]] = rhs[i];
			for (int rowPointer3 = A.RowPointers[i]; rowPointer3 < A.RowPointers[i + 1]; rowPointer3++)
			{
				int columnIndex2 = A.ColumnIndices[rowPointer3];
				T rowPointer3Term = A.Values[rowPointer3];
				if (boolValues[columnIndex2])
				{
					T[] rhsFreeArray = rhsFree;
					int indexMap3 = indexMap[i];
					rhsFreeArray[indexMap3] -= rowPointer3Term * rhsBound[~indexMap[columnIndex2]];
				}
				else
				{
					items.Add(rowPointer3Term);
					intList.Add(indexMap[columnIndex2]);
				}
			}
			AFree.BuildRow(indexMap[i], intList.ToArray(), items.ToArray());
		}
	}
}
