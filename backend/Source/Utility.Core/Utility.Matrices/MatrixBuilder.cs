using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Utility.Matrices;

public static class MatrixBuilder<T> where T : unmanaged, INumber<T>
{
	public static SparseMatrix<T> FromRows(List<Dictionary<int, T>> rows, T zeroTol)
	{
		int count = rows.Count;
		int[] intValues = new int[count + 1];
		List<int> scaledCount = new List<int>(count * 5);
		List<T> scaledCountList = new List<T>(count * 5);
		int ordinal = 0;
		for (int i = 0; i < count; i++)
		{
			intValues[i] = ordinal;
			if (rows[i].Count <= 0)
			{
				continue;
			}
			foreach (KeyValuePair<int, T> orderBy in rows[i].OrderBy((KeyValuePair<int, T> k) => k.Key))
			{
				if (T.Abs(orderBy.Value) > zeroTol)
				{
					scaledCount.Add(orderBy.Key);
					scaledCountList.Add(orderBy.Value);
					ordinal++;
				}
			}
		}
		intValues[count] = ordinal;
		return new SparseMatrix<T>(count, intValues, scaledCount.ToArray(), scaledCountList.ToArray());
	}

	public static SparseMatrix<T> FromRows(List<List<int>> rows)
	{
		int count = rows.Count;
		int[] intValues = new int[count + 1];
		List<int> scaledCount = new List<int>(count * 5);
		int ordinal = 0;
		for (int i = 0; i < count; i++)
		{
			intValues[i] = ordinal;
			if (rows[i].Count > 0)
			{
				rows[i].Sort();
				int[] row = rows[i].ToArray();
				foreach (int row2 in row)
				{
					scaledCount.Add(row2);
				}
			}
		}
		intValues[count] = ordinal;
		return new SparseMatrix<T>(count, intValues, scaledCount.ToArray());
	}

	public static void Add(Dictionary<int, T> row, int col, T row2)
	{
		if (col >= 0 && !(T.Abs(row2) == T.Zero))
		{
			if (row.TryGetValue(col, out var localValue))
			{
				row[col] = localValue + row2;
			}
			else
			{
				row[col] = row2;
			}
		}
	}

	public static void Add(List<int> row, int col)
	{
		if (col >= 0 && !row.Contains(col))
		{
			row.Add(col);
		}
	}
}
