using System;
using System.Collections.Generic;

namespace Utility.Search;

public static class SearchEngineHelpers
{
	public static StringComparer DefaultStringComparer = StringComparer.OrdinalIgnoreCase;

	private static readonly HashSet<string> IgnoreWordList = new HashSet<string>(new string[9] { "the", "a", "it", "at", "when", "then", "in", "where", "why" }, StringComparer.OrdinalIgnoreCase);

	public static bool IsWordBreak(char c)
	{
		if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsSeparator(c))
		{
			return char.IsControl(c);
		}
		return true;
	}

	public static float GetScore(ReadOnlySpan<char> searchPattern, ReadOnlySpan<char> fullString)
	{
		if (ShouldIgnore(searchPattern))
		{
			return 0f;
		}
		float searchPatternTerm = 0f;
		int unity = -1;
		bool condition = false;
		int ordinal = 0;
		for (int i = 0; i < fullString.Length; i++)
		{
			char c = fullString[i];
			if (IsWordBreak(c))
			{
				if (condition)
				{
					searchPatternTerm = ((unity != ordinal - 1) ? (searchPatternTerm + (float)searchPattern.Length / (float)ordinal) : (searchPatternTerm + 1f));
				}
				unity = -1;
				condition = false;
				ordinal = 0;
				continue;
			}
			ordinal++;
			if (!condition && (ordinal <= 1 || unity != -1))
			{
				char c2 = searchPattern[unity + 1];
				unity = ((char.ToUpperInvariant(c) != char.ToUpperInvariant(c2)) ? (-1) : (unity + 1));
				if (unity == searchPattern.Length - 1)
				{
					condition = true;
				}
			}
		}
		if (unity != -1 && condition)
		{
			searchPatternTerm = ((unity != ordinal - 1) ? (searchPatternTerm + (float)searchPattern.Length / (float)ordinal) : (searchPatternTerm + 1f));
		}
		return searchPatternTerm;
	}

	public static bool ShouldIgnore(string word)
	{
		return IgnoreWordList.Contains(word);
	}

	public static bool ShouldIgnore(ReadOnlySpan<char> word)
	{
		return ShouldIgnore(word.ToString());
	}

	public static HashSet<string> Tokenize(ReadOnlySpan<char> input)
	{
		HashSet<string> defaultStringComparer = new HashSet<string>(DefaultStringComparer);
		int unity = -1;
		for (int i = 0; i < input.Length; i++)
		{
			if (IsWordBreak(input[i]))
			{
				if (unity != -1)
				{
					int length = i - unity;
					string slice = input.Slice(unity, length).ToString();
					unity = -1;
					if (!ShouldIgnore(slice))
					{
						defaultStringComparer.Add(slice);
					}
				}
			}
			else if (unity == -1)
			{
				unity = i;
			}
		}
		if (unity != -1)
		{
			string slice2 = input.Slice(unity).ToString();
			if (!ShouldIgnore(slice2))
			{
				defaultStringComparer.Add(slice2);
			}
		}
		return defaultStringComparer;
	}

	private static void FindPrefix(string pattern, int[] prefArray)
	{
		int length = pattern.Length;
		int intervalIndex = 0;
		prefArray[0] = 0;
		for (int i = 1; i < length; i++)
		{
			if (pattern[i] == pattern[intervalIndex])
			{
				intervalIndex = (prefArray[i] = intervalIndex + 1);
			}
			else if (intervalIndex != 0)
			{
				intervalIndex = prefArray[intervalIndex - 1];
				i--;
			}
			else
			{
				prefArray[i] = 0;
			}
		}
	}

	private static void kmpPattSearch(string mainString, string pattern, int[] locArray, int loc)
	{
		int ordinal = 0;
		int intervalIndex = 0;
		int length = mainString.Length;
		int length2 = pattern.Length;
		int[] intValues = new int[length2];
		FindPrefix(pattern, intValues);
		loc = 0;
		while (ordinal < length)
		{
			if (mainString[ordinal] == pattern[intervalIndex])
			{
				ordinal++;
				intervalIndex++;
			}
			if (intervalIndex == length2)
			{
				locArray[loc] = ordinal - intervalIndex;
				loc++;
				intervalIndex = intValues[intervalIndex - 1];
			}
			else if (ordinal < length && pattern[intervalIndex] != mainString[ordinal])
			{
				if (intervalIndex != 0)
				{
					intervalIndex = intValues[intervalIndex - 1];
				}
				else
				{
					ordinal++;
				}
			}
		}
	}
}
