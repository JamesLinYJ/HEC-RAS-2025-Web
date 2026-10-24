using System;
using System.Collections.Generic;
using System.Text;

namespace Utility.Filtering;

public static class FilterNames
{
	public static string Normalize(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder(name.Length);
		foreach (char c in name)
		{
			if (c != '_' && c != ' ')
			{
				stringBuilder.Append(char.ToLowerInvariant(c));
			}
		}
		return stringBuilder.ToString();
	}

	public static string Closest(string input, IEnumerable<string> candidates)
	{
		string candidate2 = null;
		int maxValue = int.MaxValue;
		foreach (string candidate in candidates)
		{
			int editDistance = EditDistance(input, Normalize(candidate));
			if (editDistance < maxValue)
			{
				maxValue = editDistance;
				candidate2 = candidate;
			}
		}
		int scaledLength = Math.Max(2, input.Length / 3);
		if (candidate2 == null || maxValue > scaledLength)
		{
			return null;
		}
		return candidate2;
	}

	private static int EditDistance(string a, string b)
	{
		int[] intValues = new int[b.Length + 1];
		int[] intValuesArray = new int[b.Length + 1];
		for (int i = 0; i <= b.Length; i++)
		{
			intValues[i] = i;
		}
		for (int j = 1; j <= a.Length; j++)
		{
			intValuesArray[0] = j;
			for (int k = 1; k <= b.Length; k++)
			{
				int ordinal = ((a[j - 1] != b[k - 1]) ? 1 : 0);
				intValuesArray[k] = Math.Min(Math.Min(intValuesArray[k - 1] + 1, intValues[k] + 1), intValues[k - 1] + ordinal);
			}
			int[] intValues2 = intValuesArray;
			intValuesArray = intValues;
			intValues = intValues2;
		}
		return intValues[b.Length];
	}
}
