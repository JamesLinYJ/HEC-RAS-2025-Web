using System;
using System.Collections.Generic;

namespace Utility;

public static class StringHelpers
{
	public static void EnsureUniqueNames<T>(IList<T> items, Func<T, string> getName, Action<T, string> rename, string itemType, char separator = '_')
	{
		EnsureUniqueNames(items, getName, (int i) => itemType + separator + i.ToString(), rename, StringComparer.OrdinalIgnoreCase);
	}

	public static void EnsureUniqueNames<T>(IList<T> items, Func<T, string> getName, Func<int, string> nextNameCandidate, Action<T, string> rename, StringComparer comparer)
	{
		HashSet<string> comparerSet = new HashSet<string>(comparer);
		int count = items.Count;
		foreach (T localValue in items)
		{
			string getName2 = getName(localValue);
			if (string.IsNullOrEmpty(getName2) || comparerSet.Contains(getName2))
			{
				getName2 = NextUnique(comparerSet, nextNameCandidate, count);
				rename(localValue, getName2);
			}
			comparerSet.Add(getName2);
		}
	}

	private static string NextUnique(HashSet<string> hs, Func<int, string> nextNameCandidate, int maxCount)
	{
		int ordinal = 0;
		string nextNameCandidate2;
		while (true)
		{
			if (ordinal > maxCount)
			{
				throw new Exception("Unique name loop exceeded maximum count - likely incorrect implementation of " + nextNameCandidate);
			}
			nextNameCandidate2 = nextNameCandidate(ordinal);
			if (!hs.Contains(nextNameCandidate2))
			{
				break;
			}
			ordinal++;
		}
		return nextNameCandidate2;
	}

	public static string FormatBytes(long sizeInBytes)
	{
		string result = "";
		long ordinal = 1073741824L;
		ordinal *= 1024;
		if (sizeInBytes < 1024)
		{
			result = sizeInBytes + " B";
		}
		else if (sizeInBytes < 1048576)
		{
			result = sizeInBytes / 1024 + " KB";
		}
		else if (sizeInBytes < 1073741824)
		{
			result = sizeInBytes / 1048576 + " MB";
		}
		else if (sizeInBytes < ordinal)
		{
			result = Math.Round((double)sizeInBytes / 1073741824.0, 2) + " GB";
		}
		return result;
	}

	public static string NextUniqueName<T>(IReadOnlyList<T> items, Func<T, string> getName, string targetName, char separator = ' ', int maxCount = 100000)
	{
		HashSet<string> stringSet = new HashSet<string>(items.Count / 2, StringComparer.OrdinalIgnoreCase);
		int ordinal = 0;
		for (int i = 0; i < maxCount; i++)
		{
			string targetName2 = ((i == 0) ? targetName : (targetName + separator + i.ToString()));
			if (stringSet.Contains(targetName2))
			{
				continue;
			}
			bool condition = false;
			for (int j = ordinal; j < items.Count; j++)
			{
				T localValue = items[j];
				string getName2 = getName(localValue);
				stringSet.Add(getName2);
				ordinal = j;
				if (string.Equals(getName2, targetName2, StringComparison.OrdinalIgnoreCase))
				{
					condition = true;
					break;
				}
			}
			if (!condition)
			{
				return targetName2;
			}
		}
		throw new Exception("Could not find a unique name...");
	}

	public static string TruncateUnique(string name, int maxLen, HashSet<string> usedNames)
	{
		string substring = ((name.Length <= maxLen) ? name : name.Substring(0, maxLen));
		if (!usedNames.Contains(substring))
		{
			return substring;
		}
		int unity = 1;
		string substringTerm;
		while (true)
		{
			string textValue = unity.ToString();
			int maxLenAboveLength = maxLen - textValue.Length;
			if (maxLenAboveLength < 1)
			{
				throw new Exception("Could not find a unique truncated name for: " + name);
			}
			substringTerm = ((substring.Length <= maxLenAboveLength) ? substring : substring.Substring(0, maxLenAboveLength)) + textValue;
			if (!usedNames.Contains(substringTerm))
			{
				break;
			}
			unity++;
		}
		return substringTerm;
	}

	public static bool StringEquals(string s1, string s2)
	{
		if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2))
		{
			return true;
		}
		if (string.IsNullOrEmpty(s1))
		{
			return false;
		}
		if (string.IsNullOrEmpty(s2))
		{
			return false;
		}
		return s1.Equals(s2);
	}
}
