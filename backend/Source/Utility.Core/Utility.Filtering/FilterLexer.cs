using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Utility.Units;

namespace Utility.Filtering;

internal static class FilterLexer
{
	public static bool TryTokenize(string stringValue, List<FilterToken> tokens, out string error)
	{
		error = null;
		int i = 0;
		while (i < stringValue.Length)
		{
			char c = stringValue[i];
			if (char.IsWhiteSpace(c))
			{
				i++;
				continue;
			}
			if (char.IsLetter(c) || c == '_')
			{
				int ordinal = i;
				for (; i < stringValue.Length && IsIdentifierChar(stringValue[i]); i++)
				{
				}
				tokens.Add(FilterToken.Slice(FilterTokenKind.Identifier, stringValue, ordinal, i - ordinal));
				continue;
			}
			if (IsNumberStart(stringValue, i))
			{
				if (TryLexNumber(stringValue, ref i, tokens, out error))
				{
					continue;
				}
				return false;
			}
			switch (c)
			{
			case '"':
			case '\'':
				if (!TryLexString(stringValue, ref i, tokens, out error))
				{
					return false;
				}
				break;
			case '(':
				tokens.Add(FilterToken.Slice(FilterTokenKind.LeftParen, stringValue, i, 1));
				i++;
				break;
			case ')':
				tokens.Add(FilterToken.Slice(FilterTokenKind.RightParen, stringValue, i, 1));
				i++;
				break;
			case ',':
				tokens.Add(FilterToken.Slice(FilterTokenKind.Comma, stringValue, i, 1));
				i++;
				break;
			case '=':
			{
				bool condition = i + 1 < stringValue.Length && stringValue[i + 1] == '=';
				tokens.Add(FilterToken.Slice(FilterTokenKind.Equal, stringValue, i, (!condition) ? 1 : 2));
				i += ((!condition) ? 1 : 2);
				break;
			}
			case '!':
				if (i + 1 < stringValue.Length && stringValue[i + 1] == '=')
				{
					tokens.Add(FilterToken.Slice(FilterTokenKind.NotEqual, stringValue, i, 2));
					i += 2;
					break;
				}
				error = $"Unexpected character '!' at position {i} (did you mean '!='?).";
				return false;
			case '<':
				if (i + 1 < stringValue.Length && stringValue[i + 1] == '=')
				{
					tokens.Add(FilterToken.Slice(FilterTokenKind.LessOrEqual, stringValue, i, 2));
					i += 2;
				}
				else if (i + 1 < stringValue.Length && stringValue[i + 1] == '>')
				{
					tokens.Add(FilterToken.Slice(FilterTokenKind.NotEqual, stringValue, i, 2));
					i += 2;
				}
				else
				{
					tokens.Add(FilterToken.Slice(FilterTokenKind.Less, stringValue, i, 1));
					i++;
				}
				break;
			case '>':
				if (i + 1 < stringValue.Length && stringValue[i + 1] == '=')
				{
					tokens.Add(FilterToken.Slice(FilterTokenKind.GreaterOrEqual, stringValue, i, 2));
					i += 2;
				}
				else
				{
					tokens.Add(FilterToken.Slice(FilterTokenKind.Greater, stringValue, i, 1));
					i++;
				}
				break;
			default:
				error = $"Unexpected character '{c}' at position {i}.";
				return false;
			}
		}
		tokens.Add(FilterToken.Slice(FilterTokenKind.End, stringValue, stringValue.Length, 0));
		return true;
	}

	private static bool IsIdentifierChar(char c)
	{
		if (!char.IsLetterOrDigit(c))
		{
			return c == '_';
		}
		return true;
	}

	private static bool IsNumberStart(string textValue, int pos)
	{
		char c = textValue[pos];
		if (char.IsDigit(c))
		{
			return true;
		}
		char c2 = ((pos + 1 < textValue.Length) ? textValue[pos + 1] : '\0');
		switch (c)
		{
		case '.':
			return char.IsDigit(c2);
		case '-':
			if (!char.IsDigit(c2))
			{
				return c2 == '.';
			}
			return true;
		default:
			return false;
		}
	}

	private static bool TryLexNumber(string textValue, ref int pos, List<FilterToken> tokens, out string error)
	{
		error = null;
		int pos2 = pos;
		if (textValue[pos] == '-')
		{
			pos++;
		}
		while (pos < textValue.Length && (char.IsDigit(textValue[pos]) || textValue[pos] == '.'))
		{
			pos++;
		}
		if (pos < textValue.Length && (textValue[pos] == 'e' || textValue[pos] == 'E'))
		{
			int pos3 = pos + 1;
			if (pos3 < textValue.Length && (textValue[pos3] == '+' || textValue[pos3] == '-'))
			{
				pos3++;
			}
			if (pos3 < textValue.Length && char.IsDigit(textValue[pos3]))
			{
				pos = pos3;
				while (pos < textValue.Length && char.IsDigit(textValue[pos]))
				{
					pos++;
				}
			}
		}
		ReadOnlySpan<char> s = textValue.AsSpan(pos2, pos - pos2);
		if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			error = $"Invalid number '{s.ToString()}' at position {pos2}.";
			return false;
		}
		int i;
		for (i = pos; i < textValue.Length && IsIdentifierChar(textValue[i]); i++)
		{
		}
		if (i > pos && Time.TryParse(textValue.Substring(pos, i - pos)) != null)
		{
			tokens.Add(FilterToken.Slice(FilterTokenKind.Identifier, textValue, pos2, i - pos2));
			pos = i;
			return true;
		}
		tokens.Add(FilterToken.Slice(FilterTokenKind.Number, textValue, pos2, pos - pos2, -1, result));
		return true;
	}

	private static bool TryLexString(string textValue, ref int pos, List<FilterToken> tokens, out string error)
	{
		error = null;
		char c = textValue[pos];
		int pos2 = pos;
		int posOffset = pos + 1;
		pos++;
		while (pos < textValue.Length)
		{
			if (textValue[pos] != c)
			{
				pos++;
				continue;
			}
			if (pos + 1 < textValue.Length && textValue[pos + 1] == c)
			{
				return LexEscapedString(textValue, ref pos, pos2, posOffset, c, tokens, out error);
			}
			tokens.Add(FilterToken.Slice(FilterTokenKind.String, textValue, posOffset, pos - posOffset, pos2));
			pos++;
			return true;
		}
		error = $"Unterminated string starting at position {pos2}.";
		return false;
	}

	private static bool LexEscapedString(string textValue, ref int pos, int start, int contentStart, char quote, List<FilterToken> tokens, out string error)
	{
		error = null;
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(textValue, contentStart, pos - contentStart);
		while (pos < textValue.Length)
		{
			char c = textValue[pos];
			if (c == quote)
			{
				if (pos + 1 >= textValue.Length || textValue[pos + 1] != quote)
				{
					pos++;
					tokens.Add(FilterToken.Materialized(FilterTokenKind.String, stringBuilder.ToString(), start));
					return true;
				}
				stringBuilder.Append(quote);
				pos += 2;
			}
			else
			{
				stringBuilder.Append(c);
				pos++;
			}
		}
		error = $"Unterminated string starting at position {start}.";
		return false;
	}
}
