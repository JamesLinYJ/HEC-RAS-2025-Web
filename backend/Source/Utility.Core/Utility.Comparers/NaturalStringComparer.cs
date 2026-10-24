using System;
using System.Collections.Generic;

namespace Utility.Comparers;

public sealed class NaturalStringComparer : IComparer<string>
{
	public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

	private NaturalStringComparer()
	{
	}

	public int Compare(string a, string b)
	{
		if (a == b)
		{
			return 0;
		}
		if (a == null)
		{
			return 1;
		}
		if (b == null)
		{
			return -1;
		}
		return Compare((ReadOnlySpan<char>)a, (ReadOnlySpan<char>)b);
	}

	public static int Compare(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
	{
		int i = 0;
		int j = 0;
		while (i < a.Length && j < b.Length)
		{
			char c = a[i];
			char c2 = b[j];
			if (char.IsAsciiDigit(c) && char.IsAsciiDigit(c2))
			{
				int ordinal = i;
				int ordinal2 = j;
				for (; i < a.Length && char.IsAsciiDigit(a[i]); i++)
				{
				}
				for (; j < b.Length && char.IsAsciiDigit(b[j]); j++)
				{
				}
				int ordinal3 = ordinal;
				ReadOnlySpan<char> a2 = a.Slice(ordinal3, i - ordinal3);
				ordinal3 = ordinal2;
				int compareDigitRuns = CompareDigitRuns(a2, b.Slice(ordinal3, j - ordinal3));
				if (compareDigitRuns != 0)
				{
					return compareDigitRuns;
				}
				continue;
			}
			if (c != c2)
			{
				int toUpperInvariantC2Term = char.ToUpperInvariant(c) - char.ToUpperInvariant(c2);
				if (toUpperInvariantC2Term != 0)
				{
					return toUpperInvariantC2Term;
				}
			}
			i++;
			j++;
		}
		return a.Length - i - (b.Length - j);
	}

	private static int CompareDigitRuns(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
	{
		a = TrimLeadingZeros(a);
		b = TrimLeadingZeros(b);
		if (a.Length != b.Length)
		{
			return a.Length - b.Length;
		}
		for (int i = 0; i < a.Length; i++)
		{
			int ordinal = a[i] - b[i];
			if (ordinal != 0)
			{
				return ordinal;
			}
		}
		return 0;
	}

	private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> run)
	{
		int i;
		for (i = 0; i < run.Length - 1 && run[i] == '0'; i++)
		{
		}
		int ordinal = i;
		return run.Slice(ordinal, run.Length - ordinal);
	}
}
