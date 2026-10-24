using System;
using System.Collections.Generic;

namespace Utility.Filtering;

public readonly struct FilterCompletion
{
	public static readonly FilterCompletion None = new FilterCompletion(FilterCompletionKind.None, "");

	public static readonly IReadOnlyList<string> Operators = new string[10] { "= ", "!= ", "> ", ">= ", "< ", "<= ", "LIKE ", "IN (", "IS NULL", "IS NOT NULL" };

	public static readonly IReadOnlyList<string> Conjunctions = new string[2] { "AND ", "OR " };

	public static readonly IReadOnlyList<string> ListOpeners = new string[1] { "(" };

	public static readonly IReadOnlyList<string> NullChecks = new string[2] { "NULL", "NOT NULL" };

	public FilterCompletionKind Kind { get; }

	public string Prefix { get; }

	public string Partial { get; }

	public string FieldName { get; }

	public FilterCompletion(FilterCompletionKind kind, string prefix, string partial = "", string fieldName = null)
	{
		Kind = kind;
		Prefix = prefix;
		Partial = partial ?? "";
		FieldName = fieldName;
	}

	public static FilterCompletion Analyze(string stringValue)
	{
		if (stringValue == null)
		{
			stringValue = "";
		}
		int splitTrailingToken = SplitTrailingToken(stringValue);
		string fieldIds = stringValue.Substring(0, splitTrailingToken);
		string fieldIds2 = stringValue.Substring(splitTrailingToken);
		if (fieldIds2.StartsWith('\'') || fieldIds2.StartsWith('"'))
		{
			fieldIds2 = fieldIds2.Substring(1);
		}
		List<FilterToken> filterTokens = new List<FilterToken>();
		if (!FilterLexer.TryTokenize(fieldIds, filterTokens, out var _))
		{
			return None;
		}
		int countOffset = filterTokens.Count - 2;
		if (countOffset < 0)
		{
			return new FilterCompletion(FilterCompletionKind.Field, fieldIds, fieldIds2);
		}
		FilterToken filterToken = filterTokens[countOffset];
		if (filterToken.Kind == FilterTokenKind.Comma)
		{
			return new FilterCompletion(FilterCompletionKind.Value, fieldIds, fieldIds2, FieldBefore(filterTokens, countOffset));
		}
		if (filterToken.Kind == FilterTokenKind.LeftParen)
		{
			if (countOffset > 0 && IsKeyword(filterTokens[countOffset - 1].Text, "IN"))
			{
				return new FilterCompletion(FilterCompletionKind.Value, fieldIds, fieldIds2, FieldBefore(filterTokens, countOffset));
			}
			return new FilterCompletion(FilterCompletionKind.Field, fieldIds, fieldIds2);
		}
		if (IsComparisonOperator(filterToken.Kind))
		{
			return new FilterCompletion(FilterCompletionKind.Value, fieldIds, fieldIds2, FieldBefore(filterTokens, countOffset));
		}
		if (filterToken.Kind == FilterTokenKind.String || filterToken.Kind == FilterTokenKind.Number || filterToken.Kind == FilterTokenKind.RightParen)
		{
			return new FilterCompletion(FilterCompletionKind.Conjunction, fieldIds, fieldIds2);
		}
		if (filterToken.Kind != FilterTokenKind.Identifier)
		{
			return None;
		}
		if (IsKeyword(filterToken.Text, "AND") || IsKeyword(filterToken.Text, "OR") || IsKeyword(filterToken.Text, "NOT"))
		{
			return new FilterCompletion(FilterCompletionKind.Field, fieldIds, fieldIds2);
		}
		if (IsKeyword(filterToken.Text, "LIKE"))
		{
			return new FilterCompletion(FilterCompletionKind.Value, fieldIds, fieldIds2, FieldBefore(filterTokens, countOffset));
		}
		if (IsKeyword(filterToken.Text, "IN"))
		{
			return new FilterCompletion(FilterCompletionKind.ListOpen, fieldIds, fieldIds2);
		}
		if (IsKeyword(filterToken.Text, "IS"))
		{
			return new FilterCompletion(FilterCompletionKind.NullCheck, fieldIds, fieldIds2);
		}
		if (IsKeyword(filterToken.Text, "NULL"))
		{
			return new FilterCompletion(FilterCompletionKind.Conjunction, fieldIds, fieldIds2);
		}
		if (countOffset > 0 && IsComparisonOperator(filterTokens[countOffset - 1].Kind))
		{
			return new FilterCompletion(FilterCompletionKind.Conjunction, fieldIds, fieldIds2);
		}
		return new FilterCompletion(FilterCompletionKind.Operator, fieldIds, fieldIds2);
	}

	private static int SplitTrailingToken(string textValue)
	{
		int unterminatedQuote = FindUnterminatedQuote(textValue);
		if (unterminatedQuote >= 0)
		{
			return unterminatedQuote;
		}
		int ordinal = textValue.Length;
		while (ordinal > 0 && IsTokenChar(textValue[ordinal - 1]))
		{
			ordinal--;
		}
		return ordinal;
	}

	private static int FindUnterminatedQuote(string textValue)
	{
		for (int i = 0; i < textValue.Length; i++)
		{
			char c = textValue[i];
			if (c != '\'' && c != '"')
			{
				continue;
			}
			int result = i;
			i++;
			while (i < textValue.Length)
			{
				if (textValue[i] == c)
				{
					if (i + 1 >= textValue.Length || textValue[i + 1] != c)
					{
						break;
					}
					i += 2;
				}
				else
				{
					i++;
				}
			}
			if (i >= textValue.Length)
			{
				return result;
			}
		}
		return -1;
	}

	private static bool IsTokenChar(char c)
	{
		if (!char.IsLetterOrDigit(c) && c != '_')
		{
			return c == '.';
		}
		return true;
	}

	private static string FieldBefore(List<FilterToken> tokens, int operatorIdx)
	{
		for (int operatorIdxOffset = operatorIdx - 1; operatorIdxOffset >= 0; operatorIdxOffset--)
		{
			FilterToken filterToken = tokens[operatorIdxOffset];
			if (filterToken.Kind == FilterTokenKind.Identifier)
			{
				filterToken = tokens[operatorIdxOffset];
				if (!FilterParser.IsKeywordText(filterToken.Text))
				{
					filterToken = tokens[operatorIdxOffset];
					return FilterNames.Normalize(filterToken.Text.ToString());
				}
			}
		}
		return null;
	}

	private static bool IsComparisonOperator(FilterTokenKind kind)
	{
		if (kind != FilterTokenKind.Equal && kind != FilterTokenKind.NotEqual && kind != FilterTokenKind.Less && kind != FilterTokenKind.LessOrEqual && kind != FilterTokenKind.Greater)
		{
			return kind == FilterTokenKind.GreaterOrEqual;
		}
		return true;
	}

	private static bool IsKeyword(ReadOnlySpan<char> localValue, string keyword)
	{
		return MemoryExtensions.Equals(localValue, keyword, StringComparison.OrdinalIgnoreCase);
	}
}
