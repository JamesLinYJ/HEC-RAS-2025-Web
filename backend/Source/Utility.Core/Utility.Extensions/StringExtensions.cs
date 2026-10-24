using System;
using System.Collections.Generic;
using System.Text;

namespace Utility.Extensions;

public static class StringExtensions
{
	public static string InSingleQuotes(this string x)
	{
		return "'" + x + "'";
	}

	public static string InDoubleQuotes(this string x)
	{
		return "\"" + x + "\"";
	}

	public static string InParentheses(this string x)
	{
		return "(" + x + ")";
	}

	public static string InSquareBrackets(this string x)
	{
		return "[" + x + "]";
	}

	public static string InCurlyBraces(this string x)
	{
		return "{" + x + "}";
	}

	public static string InSpaces(this string x)
	{
		return " " + x + " ";
	}

	public static bool RoughlyEquals(this string x, string other)
	{
		return string.Equals(x, other, StringComparison.OrdinalIgnoreCase);
	}

	public static bool RoughlyStartsWith(this string x, string other)
	{
		return x.StartsWith(other, StringComparison.OrdinalIgnoreCase);
	}

	public static bool RoughlyEndsWith(this string x, string other)
	{
		return x.EndsWith(other, StringComparison.OrdinalIgnoreCase);
	}

	public static string EnsureEndsWith(this string source, string suffix)
	{
		if (string.IsNullOrEmpty(suffix))
		{
			return source;
		}
		for (int count = Math.Min(source.Length, suffix.Length); count >= 0; count--)
		{
			bool condition = true;
			for (int i = 0; i < count; i++)
			{
				if (source[source.Length - count + i] != suffix[i])
				{
					condition = false;
					break;
				}
			}
			if (condition)
			{
				return source + suffix.Substring(count);
			}
		}
		return source;
	}

	public static string RemoveFirstInstance(this string input, string instanceToRemove)
	{
		int indexOf = input.IndexOf(instanceToRemove, StringComparison.Ordinal);
		if (indexOf >= 0)
		{
			return input.Remove(indexOf, instanceToRemove.Length);
		}
		return input;
	}

	public static string RemoveLastInstance(this string input, string instanceToRemove)
	{
		if (input.EndsWith(instanceToRemove))
		{
			input = input.Substring(0, input.LastIndexOf(instanceToRemove));
		}
		return input;
	}

	public static string StripWhitespace(this string s)
	{
		int count = 0;
		for (int i = 0; i < s.Length; i++)
		{
			if (!char.IsWhiteSpace(s[i]))
			{
				count++;
			}
		}
		if (count == s.Length)
		{
			return s;
		}
		char[] charValues = new char[count];
		int ordinal = 0;
		for (int j = 0; j < s.Length; j++)
		{
			if (!char.IsWhiteSpace(s[j]))
			{
				charValues[ordinal++] = s[j];
			}
		}
		return new string(charValues);
	}

	public static string Tail(this string x, int charCount)
	{
		if (x.Length <= charCount)
		{
			return x;
		}
		return x.Substring(x.Length - charCount, charCount);
	}

	public static IEnumerable<string> Split(this string stringValue, Func<char, bool> controller)
	{
		int ordinal = 0;
		for (int c = 0; c < stringValue.Length; c++)
		{
			if (controller(stringValue[c]))
			{
				yield return stringValue.Substring(ordinal, c - ordinal);
				ordinal = c + 1;
			}
		}
		yield return stringValue.Substring(ordinal);
	}

	public static string TrimMatchingQuotes(this string input, char quote)
	{
		if (input.Length >= 2 && input[0] == quote && input[input.Length - 1] == quote)
		{
			return input.Substring(1, input.Length - 2);
		}
		return input;
	}

	public static bool StartsWithNumeric(this string s)
	{
		if (s == null)
		{
			throw new ArgumentNullException("s");
		}
		if (s.Length == 0)
		{
			return false;
		}
		if (s[0] >= '0' && s[0] <= '9')
		{
			return true;
		}
		return false;
	}

	public static bool StartsWithLetter(this string s)
	{
		if (s == null)
		{
			throw new ArgumentNullException("s");
		}
		if (s.Length == 0)
		{
			return false;
		}
		if ((s[0] >= 'A' && s[0] <= 'Z') || (s[0] >= 'a' && s[0] <= 'z'))
		{
			return true;
		}
		return false;
	}

	public static bool RoughlyContains(this IList<string> stringList, string element)
	{
		for (int i = 0; i < stringList.Count; i++)
		{
			if (stringList[i].RoughlyEquals(element))
			{
				return true;
			}
		}
		return false;
	}

	public static string ReduceWhitespace(this string stringValue)
	{
		StringBuilder stringBuilder = new StringBuilder();
		bool condition = false;
		for (int i = 0; i < stringValue.Length; i++)
		{
			if (char.IsWhiteSpace(stringValue[i]))
			{
				if (condition)
				{
					continue;
				}
				condition = true;
			}
			else
			{
				condition = false;
			}
			stringBuilder.Append(stringValue[i]);
		}
		return stringBuilder.ToString();
	}

	public static string AssignNameADuplicateNumber(this string name)
	{
		if (name[name.Length - 1] == ')')
		{
			int lengthOffset = name.Length - 2;
			string insertTerm = "";
			while (name[lengthOffset] != '(')
			{
				insertTerm = insertTerm.Insert(0, name[lengthOffset].ToString());
				lengthOffset--;
			}
			if (!int.TryParse(insertTerm, out var result))
			{
				name += " (1)";
			}
			else
			{
				name = name.Remove(name.IndexOf(" (" + result + ")"));
				name = name + " (" + (result + 1) + ")";
			}
		}
		else
		{
			name += " (1)";
		}
		return name;
	}
}
