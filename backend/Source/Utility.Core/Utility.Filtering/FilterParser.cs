using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Utility.Filtering;

internal class FilterParser
{
	private readonly List<FilterToken> _tokens;

	private int _index;

	private string _error;

	private FilterToken Current => _tokens[_index];

	private FilterParser(List<FilterToken> tokens)
	{
		_tokens = tokens;
	}

	public static bool TryParse(string stringValue, out FilterExpression expression, out string error)
	{
		expression = null;
		if (string.IsNullOrWhiteSpace(stringValue))
		{
			error = "Filter expression is empty.";
			return false;
		}
		List<FilterToken> tokens = new List<FilterToken>();
		if (!FilterLexer.TryTokenize(stringValue, tokens, out error))
		{
			return false;
		}
		FilterParser filterParser = new FilterParser(tokens);
		FilterExpression filterExpression = filterParser.ParseOr();
		if (filterExpression == null)
		{
			error = filterParser._error;
			return false;
		}
		if (filterParser.Current.Kind != FilterTokenKind.End)
		{
			error = $"Unexpected '{filterParser.Current.Text}' at position {filterParser.Current.Position}.";
			return false;
		}
		expression = filterExpression;
		error = null;
		return true;
	}

	private void Advance()
	{
		_index++;
	}

	private bool CurrentIsKeyword(string keyword)
	{
		if (Current.Kind == FilterTokenKind.Identifier)
		{
			return MemoryExtensions.Equals(Current.Text, keyword, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	internal static bool IsKeywordText(ReadOnlySpan<char> charSpan)
	{
		if (!MemoryExtensions.Equals(charSpan, "AND", StringComparison.OrdinalIgnoreCase) && !MemoryExtensions.Equals(charSpan, "OR", StringComparison.OrdinalIgnoreCase) && !MemoryExtensions.Equals(charSpan, "NOT", StringComparison.OrdinalIgnoreCase) && !MemoryExtensions.Equals(charSpan, "IN", StringComparison.OrdinalIgnoreCase) && !MemoryExtensions.Equals(charSpan, "LIKE", StringComparison.OrdinalIgnoreCase) && !MemoryExtensions.Equals(charSpan, "IS", StringComparison.OrdinalIgnoreCase))
		{
			return MemoryExtensions.Equals(charSpan, "NULL", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private FilterExpression ParseOr()
	{
		FilterExpression filterExpression = ParseAnd();
		if (filterExpression == null)
		{
			return null;
		}
		while (CurrentIsKeyword("OR"))
		{
			Advance();
			FilterExpression filterExpression2 = ParseAnd();
			if (filterExpression2 == null)
			{
				return null;
			}
			filterExpression = new OrFilter(filterExpression, filterExpression2);
		}
		return filterExpression;
	}

	private FilterExpression ParseAnd()
	{
		FilterExpression filterExpression = ParseUnary();
		if (filterExpression == null)
		{
			return null;
		}
		while (CurrentIsKeyword("AND"))
		{
			Advance();
			FilterExpression filterExpression2 = ParseUnary();
			if (filterExpression2 == null)
			{
				return null;
			}
			filterExpression = new AndFilter(filterExpression, filterExpression2);
		}
		return filterExpression;
	}

	private FilterExpression ParseUnary()
	{
		if (CurrentIsKeyword("NOT"))
		{
			Advance();
			FilterExpression filterExpression = ParseUnary();
			if (filterExpression == null)
			{
				return null;
			}
			return new NotFilter(filterExpression);
		}
		return ParsePrimary();
	}

	private FilterExpression ParsePrimary()
	{
		if (Current.Kind == FilterTokenKind.LeftParen)
		{
			Advance();
			FilterExpression filterExpression = ParseOr();
			if (filterExpression == null)
			{
				return null;
			}
			if (Current.Kind != FilterTokenKind.RightParen)
			{
				_error = $"Expected ')' at position {Current.Position}.";
				return null;
			}
			Advance();
			return filterExpression;
		}
		return ParseComparison();
	}

	private FilterExpression ParseComparison()
	{
		FilterToken current = Current;
		if (current.Kind != FilterTokenKind.Identifier)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Expected a field name at position ");
			current = Current;
			defaultInterpolatedStringHandler.AppendFormatted(current.Position);
			defaultInterpolatedStringHandler.AppendLiteral(".");
			_error = defaultInterpolatedStringHandler.ToStringAndClear();
			return null;
		}
		current = Current;
		string textValue = current.Text.ToString();
		if (IsKeywordText(textValue))
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler2 = new DefaultInterpolatedStringHandler(53, 2);
			defaultInterpolatedStringHandler2.AppendLiteral("Expected a field name at position ");
			current = Current;
			defaultInterpolatedStringHandler2.AppendFormatted(current.Position);
			defaultInterpolatedStringHandler2.AppendLiteral(", found keyword '");
			defaultInterpolatedStringHandler2.AppendFormatted(textValue);
			defaultInterpolatedStringHandler2.AppendLiteral("'.");
			_error = defaultInterpolatedStringHandler2.ToStringAndClear();
			return null;
		}
		Advance();
		current = Current;
		if (TryMapComparisonOperator(current.Kind, out var comparisonOperator))
		{
			Advance();
			if (!TryParseLiteral(out var literal))
			{
				return null;
			}
			return new ComparisonFilter(textValue, comparisonOperator, literal);
		}
		if (CurrentIsKeyword("IS"))
		{
			Advance();
			bool negated = false;
			if (CurrentIsKeyword("NOT"))
			{
				negated = true;
				Advance();
			}
			if (!CurrentIsKeyword("NULL"))
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler3 = new DefaultInterpolatedStringHandler(36, 1);
				defaultInterpolatedStringHandler3.AppendLiteral("Expected NULL after IS at position ");
				current = Current;
				defaultInterpolatedStringHandler3.AppendFormatted(current.Position);
				defaultInterpolatedStringHandler3.AppendLiteral(".");
				_error = defaultInterpolatedStringHandler3.ToStringAndClear();
				return null;
			}
			Advance();
			return new NullCheckFilter(textValue, negated);
		}
		bool condition = false;
		if (CurrentIsKeyword("NOT"))
		{
			condition = true;
			Advance();
		}
		if (CurrentIsKeyword("IN"))
		{
			Advance();
			return ParseInList(textValue, condition);
		}
		if (CurrentIsKeyword("LIKE"))
		{
			Advance();
			FilterToken current2 = Current;
			if (!TryParseLiteral(out var literal2))
			{
				return null;
			}
			if (literal2.IsNumber)
			{
				_error = $"LIKE pattern at position {current2.Position} must be a string.";
				return null;
			}
			return new LikeFilter(textValue, literal2.Text, condition);
		}
		string error;
		if (!condition)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler4 = new DefaultInterpolatedStringHandler(84, 2);
			defaultInterpolatedStringHandler4.AppendLiteral("Expected an operator (=, !=, <, <=, >, >=, IN, LIKE, IS NULL) after '");
			defaultInterpolatedStringHandler4.AppendFormatted(textValue);
			defaultInterpolatedStringHandler4.AppendLiteral("' at position ");
			current = Current;
			defaultInterpolatedStringHandler4.AppendFormatted(current.Position);
			defaultInterpolatedStringHandler4.AppendLiteral(".");
			error = defaultInterpolatedStringHandler4.ToStringAndClear();
		}
		else
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler5 = new DefaultInterpolatedStringHandler(43, 1);
			defaultInterpolatedStringHandler5.AppendLiteral("Expected IN or LIKE after NOT at position ");
			current = Current;
			defaultInterpolatedStringHandler5.AppendFormatted(current.Position);
			defaultInterpolatedStringHandler5.AppendLiteral(".");
			error = defaultInterpolatedStringHandler5.ToStringAndClear();
		}
		_error = error;
		return null;
	}

	private FilterExpression ParseInList(string field, bool negated)
	{
		if (Current.Kind != FilterTokenKind.LeftParen)
		{
			_error = $"Expected '(' after IN at position {Current.Position}.";
			return null;
		}
		Advance();
		List<FilterLiteral> filterLiterals = new List<FilterLiteral>();
		while (true)
		{
			if (!TryParseLiteral(out var literal))
			{
				return null;
			}
			filterLiterals.Add(literal);
			if (Current.Kind != FilterTokenKind.Comma)
			{
				break;
			}
			Advance();
		}
		if (Current.Kind == FilterTokenKind.RightParen)
		{
			Advance();
			return new InFilter(field, filterLiterals, negated);
		}
		_error = $"Expected ',' or ')' in IN list at position {Current.Position}.";
		return null;
	}

	private bool TryParseLiteral(out FilterLiteral literal)
	{
		literal = null;
		FilterToken current = Current;
		switch (current.Kind)
		{
		case FilterTokenKind.Number:
			literal = FilterLiteral.FromNumber(current.Number, current.Text.ToString());
			Advance();
			return true;
		case FilterTokenKind.String:
			literal = FilterLiteral.FromText(current.Text.ToString(), quoted: true);
			Advance();
			return true;
		case FilterTokenKind.Identifier:
			if (MemoryExtensions.Equals(current.Text, "NULL", StringComparison.OrdinalIgnoreCase))
			{
				_error = $"Use IS NULL / IS NOT NULL to test for null (position {current.Position}).";
				return false;
			}
			if (IsKeywordText(current.Text))
			{
				_error = $"Expected a value at position {current.Position}, found keyword '{current.Text}'.";
				return false;
			}
			literal = FilterLiteral.FromText(current.Text.ToString(), quoted: false);
			Advance();
			return true;
		default:
			_error = $"Expected a value at position {current.Position}.";
			return false;
		}
	}

	private static bool TryMapComparisonOperator(FilterTokenKind kind, out FilterComparisonOperator comparisonOperator)
	{
		switch (kind)
		{
		case FilterTokenKind.Equal:
			comparisonOperator = FilterComparisonOperator.Equal;
			return true;
		case FilterTokenKind.NotEqual:
			comparisonOperator = FilterComparisonOperator.NotEqual;
			return true;
		case FilterTokenKind.Less:
			comparisonOperator = FilterComparisonOperator.Less;
			return true;
		case FilterTokenKind.LessOrEqual:
			comparisonOperator = FilterComparisonOperator.LessOrEqual;
			return true;
		case FilterTokenKind.Greater:
			comparisonOperator = FilterComparisonOperator.Greater;
			return true;
		case FilterTokenKind.GreaterOrEqual:
			comparisonOperator = FilterComparisonOperator.GreaterOrEqual;
			return true;
		default:
			comparisonOperator = FilterComparisonOperator.Equal;
			return false;
		}
	}
}
