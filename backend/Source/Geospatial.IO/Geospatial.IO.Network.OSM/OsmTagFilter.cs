using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Geospatial.IO.Network.OSM;

public readonly struct OsmTagFilter
{
	public string Key { get; }

	public OsmTagOperator Operator { get; }

	public string Value { get; }

	public bool RequiresValue
	{
		get
		{
			if (Operator != OsmTagOperator.Exists)
			{
				return Operator != OsmTagOperator.NotExists;
			}
			return false;
		}
	}

	public OsmTagFilter(string key, OsmTagOperator op, string stringValue = null)
	{
		Key = key;
		Operator = op;
		Value = stringValue;
	}

	public bool TryAppendQl(StringBuilder sb, out string error)
	{
		error = null;
		if (string.IsNullOrWhiteSpace(Key))
		{
			error = "A tag filter is missing its key.";
			return false;
		}
		if (RequiresValue && string.IsNullOrWhiteSpace(Value))
		{
			error = $"Tag filter \"{Key}\" needs a value for the \"{Operator}\" comparison.";
			return false;
		}
		string escapeLiteral = OverpassQl.EscapeLiteral(Key.Trim());
		switch (Operator)
		{
		case OsmTagOperator.Exists:
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder7 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
			handler.AppendLiteral("[\"");
			handler.AppendFormatted(escapeLiteral);
			handler.AppendLiteral("\"]");
			stringBuilder7.Append(ref handler);
			return true;
		}
		case OsmTagOperator.NotExists:
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder6 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder);
			handler.AppendLiteral("[!\"");
			handler.AppendFormatted(escapeLiteral);
			handler.AppendLiteral("\"]");
			stringBuilder6.Append(ref handler);
			return true;
		}
		case OsmTagOperator.Equals:
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder5 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, stringBuilder);
			handler.AppendLiteral("[\"");
			handler.AppendFormatted(escapeLiteral);
			handler.AppendLiteral("\"=\"");
			handler.AppendFormatted(OverpassQl.EscapeLiteral(Value.Trim()));
			handler.AppendLiteral("\"]");
			stringBuilder5.Append(ref handler);
			return true;
		}
		case OsmTagOperator.NotEquals:
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 2, stringBuilder);
			handler.AppendLiteral("[\"");
			handler.AppendFormatted(escapeLiteral);
			handler.AppendLiteral("\"!=\"");
			handler.AppendFormatted(OverpassQl.EscapeLiteral(Value.Trim()));
			handler.AppendLiteral("\"]");
			stringBuilder4.Append(ref handler);
			return true;
		}
		case OsmTagOperator.Matches:
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, stringBuilder);
			handler.AppendLiteral("[\"");
			handler.AppendFormatted(escapeLiteral);
			handler.AppendLiteral("\"~\"");
			handler.AppendFormatted(OverpassQl.EscapeLiteral(Value.Trim()));
			handler.AppendLiteral("\"]");
			stringBuilder3.Append(ref handler);
			return true;
		}
		case OsmTagOperator.NotMatches:
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 2, stringBuilder);
			handler.AppendLiteral("[\"");
			handler.AppendFormatted(escapeLiteral);
			handler.AppendLiteral("\"!~\"");
			handler.AppendFormatted(OverpassQl.EscapeLiteral(Value.Trim()));
			handler.AppendLiteral("\"]");
			stringBuilder2.Append(ref handler);
			return true;
		}
		case OsmTagOperator.OneOf:
			return TryAppendOneOf(sb, escapeLiteral, out error);
		default:
			error = $"Unsupported tag operator \"{Operator}\".";
			return false;
		}
	}

	private bool TryAppendOneOf(StringBuilder sb, string escapedKey, out string error)
	{
		error = null;
		List<string> stringList = new List<string>();
		string[] parts = Value.Split(';', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			string trim = parts[i].Trim();
			if (trim.Length > 0)
			{
				stringList.Add(Regex.Escape(trim));
			}
		}
		if (stringList.Count == 0)
		{
			error = "Tag filter \"" + Key + "\" lists no values.";
			return false;
		}
		string textValue = "^(" + string.Join("|", stringList) + ")$";
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, sb);
		handler.AppendLiteral("[\"");
		handler.AppendFormatted(escapedKey);
		handler.AppendLiteral("\"~\"");
		handler.AppendFormatted(OverpassQl.EscapeLiteral(textValue));
		handler.AppendLiteral("\"]");
		sb.Append(ref handler);
		return true;
	}
}
