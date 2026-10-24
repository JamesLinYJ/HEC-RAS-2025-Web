using System;
using System.Collections.Generic;
using System.Text;
using Utility;
using Utility.Memory;

namespace Geospatial.IO.Network.OSM;

public static class OsmAttributeTableBuilder
{
	public const string OsmIdColumn = "osm_id";

	public const string OsmTypeColumn = "osm_type";

	public const int DefaultMaxTagColumns = 40;

	public const int MaxColumnNameLength = 10;

	public const int MaxValueLength = 254;

	public const string FallbackColumnName = "tag";

	public static Table Build(IReadOnlyList<OsmElement> elements, int maxTagColumns = 40)
	{
		Table table = new Table();
		table.AddColumn("osm_id", typeof(long));
		table.AddColumn("osm_type", typeof(string));
		List<string> rankTagKeys = RankTagKeys(elements, maxTagColumns);
		foreach (string columnName in BuildColumnNames(rankTagKeys))
		{
			table.AddColumn(columnName, typeof(string));
		}
		if (elements == null)
		{
			return table;
		}
		foreach (OsmElement element in elements)
		{
			object[] objectValues = new object[2 + rankTagKeys.Count];
			objectValues[0] = element?.Id ?? 0;
			objectValues[1] = element?.Type ?? string.Empty;
			for (int i = 0; i < rankTagKeys.Count; i++)
			{
				objectValues[2 + i] = GetTagValue(element, rankTagKeys[i]);
			}
			table.AddNewRow(objectValues);
		}
		return table;
	}

	public static List<string> RankTagKeys(IReadOnlyList<OsmElement> elements, int maxKeys)
	{
		List<string> stringList = new List<string>();
		if (elements == null || maxKeys <= 0)
		{
			return stringList;
		}
		Dictionary<string, int> counts = new Dictionary<string, int>();
		foreach (OsmElement element in elements)
		{
			if (element == null || element.Tags == null)
			{
				continue;
			}
			foreach (KeyValuePair<string, string> tag in element.Tags)
			{
				counts.TryGetValue(tag.Key, out var localValue);
				counts[tag.Key] = localValue + 1;
			}
		}
		foreach (KeyValuePair<string, int> count in counts)
		{
			stringList.Add(count.Key);
		}
		stringList.Sort((string a, string b) =>
		{
			int countsTerm = counts[b].CompareTo(counts[a]);
			return (countsTerm != 0) ? countsTerm : string.CompareOrdinal(a, b);
		});
		if (stringList.Count > maxKeys)
		{
			stringList.RemoveRange(maxKeys, stringList.Count - maxKeys);
		}
		return stringList;
	}

	public static List<string> BuildColumnNames(IReadOnlyList<string> tagKeys)
	{
		List<string> columnNames = new List<string>();
		if (tagKeys == null)
		{
			return columnNames;
		}
		HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "osm_id", "osm_type" };
		foreach (string tagKey in tagKeys)
		{
			columnNames.Add(MakeUnique(SanitizeColumnName(tagKey), used));
		}
		return columnNames;
	}

	public static string SanitizeColumnName(string key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return "tag";
		}
		StringBuilder stringBuilder = new StringBuilder(10);
		foreach (char c in key)
		{
			if (stringBuilder.Length == 10)
			{
				break;
			}
			bool condition = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
			stringBuilder.Append(condition ? char.ToLowerInvariant(c) : '_');
		}
		string stringBuilder2 = stringBuilder.ToString();
		if (stringBuilder2.Length != 0)
		{
			return stringBuilder2;
		}
		return "tag";
	}

	private static string GetTagValue(OsmElement element, string key)
	{
		if (element == null || !element.TryGetTag(key, out var localValue) || localValue == null)
		{
			return string.Empty;
		}
		if (localValue.Length > 254)
		{
			return localValue.Substring(0, 254);
		}
		return localValue;
	}

	private static string MakeUnique(string baseName, HashSet<string> used)
	{
		string truncateUnique = StringHelpers.TruncateUnique(baseName, 10, used);
		used.Add(truncateUnique);
		return truncateUnique;
	}
}
