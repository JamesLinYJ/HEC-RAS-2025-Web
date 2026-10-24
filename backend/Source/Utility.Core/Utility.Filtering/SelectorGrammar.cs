using System;
using System.Collections.Generic;
using System.Text;

namespace Utility.Filtering;

public static class SelectorGrammar
{
	private const int FieldColumn = 30;

	public static string Describe(ISelectorNode root, bool showCounts = true)
	{
		StringBuilder stringBuilder = new StringBuilder();
		WriteNode(stringBuilder, root, 0, showCounts);
		return stringBuilder.ToString().TrimEnd();
	}

	private static void WriteNode(StringBuilder sb, ISelectorNode node, int depth, bool showCounts)
	{
		sb.Append(Pad(depth)).AppendLine(node.Name);
		if (node.Fields != null)
		{
			WriteNodeFields(sb, node.Fields, depth + 1);
		}
		foreach (string memberName in node.MemberNames)
		{
			string normalizedName = FilterNames.Normalize(memberName);
			ISelectorCollection collection;
			if (node.TryGetChildNode(normalizedName, out var node2))
			{
				sb.AppendLine();
				WriteNode(sb, node2, depth + 1, showCounts);
			}
			else if (node.TryGetCollection(normalizedName, out collection))
			{
				sb.AppendLine();
				WriteCollection(sb, collection, depth + 1, showCounts);
			}
		}
	}

	private static void WriteNodeFields(StringBuilder sb, ISelectorFields fields, int depth)
	{
		string pad = Pad(depth);
		foreach (string fieldName in fields.FieldNames)
		{
			if (fields.TryGetField(FilterNames.Normalize(fieldName), out var type, out var settable))
			{
				string settableTerm = (settable ? "" : "   (read-only)");
				StringBuilder stringBuilder = sb.Append(pad);
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(0, 3, stringBuilder);
				handler.AppendFormatted(fieldName.PadRight(30));
				handler.AppendFormatted(TypeName(type));
				handler.AppendFormatted(settableTerm);
				stringBuilder.AppendLine(ref handler);
			}
		}
	}

	private static void WriteCollection(StringBuilder sb, ISelectorCollection collection, int depth, bool showCounts)
	{
		string collectionTerm = "";
		if (showCounts)
		{
			string collectionTerm2 = ((collection.Count == 1) ? "" : "s");
			collectionTerm = $"   ({collection.Count} element{collectionTerm2})";
		}
		StringBuilder stringBuilder = sb.Append(Pad(depth));
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder);
		handler.AppendFormatted(collection.Name);
		handler.AppendLiteral("[]");
		handler.AppendFormatted(collectionTerm);
		stringBuilder2.AppendLine(ref handler);
		string pad = Pad(depth + 1);
		List<string> stringList = new List<string>();
		foreach (string projectionField in collection.ProjectionFields)
		{
			if (collection.TryGetField(FilterNames.Normalize(projectionField), out var type, out var settable))
			{
				if (!settable)
				{
					stringList.Add(projectionField);
					continue;
				}
				stringBuilder = sb.Append(pad);
				StringBuilder stringBuilder3 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(0, 2, stringBuilder);
				handler.AppendFormatted(projectionField.PadRight(30));
				handler.AppendFormatted(TypeName(type));
				stringBuilder3.AppendLine(ref handler);
			}
		}
		List<string> aliasesOf = AliasesOf(collection);
		if (aliasesOf.Count > 0)
		{
			sb.Append(pad).AppendLine("aliases: " + string.Join(", ", aliasesOf));
		}
		if (stringList.Count > 0)
		{
			sb.Append(pad).AppendLine("filter-only: " + string.Join(", ", stringList));
		}
	}

	private static List<string> AliasesOf(ISelectorCollection collection)
	{
		HashSet<string> ordinal = new HashSet<string>(StringComparer.Ordinal);
		foreach (string projectionField in collection.ProjectionFields)
		{
			ordinal.Add(FilterNames.Normalize(projectionField));
		}
		List<string> stringList = new List<string>();
		foreach (string knownFieldName in collection.KnownFieldNames)
		{
			if (!ordinal.Contains(FilterNames.Normalize(knownFieldName)))
			{
				stringList.Add(knownFieldName);
			}
		}
		return stringList;
	}

	private static string TypeName(Type type)
	{
		if (type == typeof(float))
		{
			return "float";
		}
		if (type == typeof(double))
		{
			return "double";
		}
		if (type == typeof(int))
		{
			return "int";
		}
		if (type == typeof(long))
		{
			return "long";
		}
		if (type == typeof(bool))
		{
			return "bool";
		}
		if (type == typeof(string))
		{
			return "string";
		}
		if (type == typeof(DateTime))
		{
			return "datetime (e.g. '2000-01-01 00:00')";
		}
		if (type == typeof(TimeSpan))
		{
			return "duration (e.g. 30s, 15min, 1.5h, or '01:30:00')";
		}
		if (type.IsEnum)
		{
			return type.Name + " (" + string.Join(", ", Enum.GetNames(type)) + ")";
		}
		return type.Name;
	}

	private static string Pad(int depth)
	{
		return new string(' ', depth * 2);
	}
}
