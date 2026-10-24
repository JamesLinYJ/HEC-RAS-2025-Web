using System;
using System.Runtime.CompilerServices;

namespace Utility.Filtering;

public static class FuzzyStringMatcher
{
	public static float CaseMismatchPenalty { get; set; } = 0.1f;

	public static float PunctuationPenalty { get; set; } = 0.15f;

	public static float Match(string target, string candidate)
	{
		if (string.IsNullOrEmpty(target) && string.IsNullOrEmpty(candidate))
		{
			return 1f;
		}
		if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(candidate))
		{
			return 0f;
		}
		return Match((ReadOnlySpan<char>)target, (ReadOnlySpan<char>)candidate);
	}

	public static float Match(ReadOnlySpan<char> target, ReadOnlySpan<char> candidate)
	{
		if (target.Length == 0 && candidate.Length == 0)
		{
			return 1f;
		}
		if (target.Length == 0 || candidate.Length == 0)
		{
			return 0f;
		}
		if (target.SequenceEqual(candidate))
		{
			return 1f;
		}
		float weightedEditDistance = CalculateWeightedEditDistance(target, candidate);
		int targetCandidateTerm = Math.Max(target.Length, candidate.Length);
		return Math.Clamp(1f - weightedEditDistance / (float)targetCandidateTerm, 0f, 1f);
	}

	private static float CalculateWeightedEditDistance(ReadOnlySpan<char> source, ReadOnlySpan<char> target)
	{
		int length = source.Length;
		int length2 = target.Length;
		Span<float> floatSpan = stackalloc float[length2 + 1];
		Span<float> floatSpan2 = stackalloc float[length2 + 1];
		for (int i = 0; i <= length2; i++)
		{
			floatSpan[i] = CalculateInsertionCost(target, 0, i);
		}
		for (int j = 1; j <= length; j++)
		{
			floatSpan2[0] = CalculateDeletionCost(source, 0, j);
			for (int k = 1; k <= length2; k++)
			{
				char c = source[j - 1];
				char c2 = target[k - 1];
				if (c == c2)
				{
					floatSpan2[k] = floatSpan[k - 1];
					continue;
				}
				float getSubstitutionCostC2Term = floatSpan[k - 1] + GetSubstitutionCost(c, c2);
				float getCharacterCostTerm = floatSpan[k] + GetCharacterCost(c);
				float getCharacterCostC2Term = floatSpan2[k - 1] + GetCharacterCost(c2);
				floatSpan2[k] = Math.Min(Math.Min(getSubstitutionCostC2Term, getCharacterCostTerm), getCharacterCostC2Term);
			}
			Span<float> floatSpan3 = floatSpan;
			floatSpan = floatSpan2;
			floatSpan2 = floatSpan3;
		}
		return floatSpan[length2];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float CalculateInsertionCost(ReadOnlySpan<char> localValue, int start, int count)
	{
		float characterCostSum = 0f;
		for (int i = start; i < start + count && i < localValue.Length; i++)
		{
			characterCostSum += GetCharacterCost(localValue[i]);
		}
		return characterCostSum;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float CalculateDeletionCost(ReadOnlySpan<char> localValue, int start, int count)
	{
		float characterCostSum = 0f;
		for (int i = start; i < start + count && i < localValue.Length; i++)
		{
			characterCostSum += GetCharacterCost(localValue[i]);
		}
		return characterCostSum;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float GetSubstitutionCost(char c1, char c2)
	{
		if (char.ToUpperInvariant(c1) == char.ToUpperInvariant(c2))
		{
			return CaseMismatchPenalty;
		}
		bool isPunctuationOrIsSeparator = char.IsPunctuation(c1) || char.IsSeparator(c1);
		bool isPunctuationOrIsSeparator2 = char.IsPunctuation(c2) || char.IsSeparator(c2);
		if (isPunctuationOrIsSeparator & isPunctuationOrIsSeparator2)
		{
			return PunctuationPenalty;
		}
		if (isPunctuationOrIsSeparator | isPunctuationOrIsSeparator2)
		{
			return 0.5f + PunctuationPenalty;
		}
		return 1f;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float GetCharacterCost(char c)
	{
		if (char.IsWhiteSpace(c))
		{
			return 0.2f;
		}
		if (char.IsPunctuation(c) || char.IsSeparator(c))
		{
			return PunctuationPenalty;
		}
		return 1f;
	}

	public static float SimpleMatch(ReadOnlySpan<char> target, ReadOnlySpan<char> candidate)
	{
		if (target.Length == 0 && candidate.Length == 0)
		{
			return 1f;
		}
		if (target.Length == 0 || candidate.Length == 0)
		{
			return 0f;
		}
		int ordinal = 0;
		int i = 0;
		int j = 0;
		int ordinal2 = 0;
		while (i < target.Length && j < candidate.Length)
		{
			char c = target[i];
			char c2 = candidate[j];
			if (char.IsPunctuation(c) || char.IsSeparator(c) || char.IsWhiteSpace(c))
			{
				i++;
				continue;
			}
			if (char.IsPunctuation(c2) || char.IsSeparator(c2) || char.IsWhiteSpace(c2))
			{
				j++;
				continue;
			}
			if (char.ToUpperInvariant(c) == char.ToUpperInvariant(c2))
			{
				ordinal++;
			}
			ordinal2++;
			i++;
			j++;
		}
		for (; i < target.Length; i++)
		{
			if (!char.IsPunctuation(target[i]) && !char.IsSeparator(target[i]) && !char.IsWhiteSpace(target[i]))
			{
				ordinal2++;
			}
		}
		for (; j < candidate.Length; j++)
		{
			if (!char.IsPunctuation(candidate[j]) && !char.IsSeparator(candidate[j]) && !char.IsWhiteSpace(candidate[j]))
			{
				ordinal2++;
			}
		}
		if (ordinal2 == 0)
		{
			if (target.Length != candidate.Length)
			{
				return 0f;
			}
			return 1f;
		}
		return (float)ordinal / (float)ordinal2;
	}
}
