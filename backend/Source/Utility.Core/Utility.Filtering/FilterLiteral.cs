using System;
using System.Collections.Generic;
using System.Globalization;

namespace Utility.Filtering;

public sealed class FilterLiteral
{
	public string Text { get; }

	public bool IsQuoted { get; }

	public bool IsNumber { get; }

	public double NumberValue { get; }

	private FilterLiteral(string textValue, bool isQuoted, bool isNumber, double numberValue)
	{
		Text = textValue;
		IsQuoted = isQuoted;
		IsNumber = isNumber;
		NumberValue = numberValue;
	}

	public static FilterLiteral FromNumber(double doubleValue, string rawText)
	{
		return new FilterLiteral(rawText, isQuoted: false, isNumber: true, doubleValue);
	}

	public static FilterLiteral FromText(string stringValue, bool quoted)
	{
		return new FilterLiteral(stringValue, quoted, isNumber: false, 0.0);
	}

	public static bool TryParse(string stringValue, out FilterLiteral literal, out string error)
	{
		literal = null;
		List<FilterToken> filterTokens = new List<FilterToken>();
		if (!FilterLexer.TryTokenize(stringValue ?? "", filterTokens, out error))
		{
			return false;
		}
		if (filterTokens.Count != 2 || filterTokens[1].Kind != FilterTokenKind.End)
		{
			error = "Expected a single value.";
			return false;
		}
		FilterToken filterToken = filterTokens[0];
		switch (filterToken.Kind)
		{
		case FilterTokenKind.Number:
			literal = FromNumber(filterToken.Number, filterToken.Text.ToString());
			return true;
		case FilterTokenKind.String:
			literal = FromText(filterToken.Text.ToString(), quoted: true);
			return true;
		case FilterTokenKind.Identifier:
			if (MemoryExtensions.Equals(filterToken.Text, "null", StringComparison.OrdinalIgnoreCase))
			{
				error = "Cannot assign NULL.";
				return false;
			}
			literal = FromText(filterToken.Text.ToString(), quoted: false);
			return true;
		default:
			error = "Expected a value.";
			return false;
		}
	}

	public bool TryGetNumber(out double number)
	{
		if (IsNumber)
		{
			number = NumberValue;
			return true;
		}
		return double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
	}

	public override string ToString()
	{
		if (!IsQuoted)
		{
			return Text;
		}
		return FilterText.Quote(Text);
	}
}
