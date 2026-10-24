using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Ras.Process.Helpers;

internal static class OutputFormats
{
	public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private static readonly JsonSerializerOptions Flat = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static string Properties(object model)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (KeyValuePair<string, string> flatten in Flatten(model))
		{
			stringBuilder.Append(flatten.Key).Append('=').AppendLine(flatten.Value);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	public static bool WriteGet(object model, string path)
	{
		if (TryGet(model, path, out var output, out var suggestion))
		{
			Console.WriteLine(output);
			return true;
		}
		string suggestionTerm = "No field matches '" + path + "'.";
		if (suggestion != null)
		{
			suggestionTerm = suggestionTerm + " Did you mean '" + suggestion + "'?";
		}
		Console.Error.WriteLine(suggestionTerm);
		return false;
	}

	public static bool TryGet(object model, string path, out string output, out string suggestion)
	{
		output = null;
		suggestion = null;
		List<KeyValuePair<string, string>> flatten = Flatten(model);
		StringBuilder stringBuilder = new StringBuilder();
		bool condition = false;
		foreach (KeyValuePair<string, string> flatten2 in flatten)
		{
			if (!(flatten2.Key != path))
			{
				stringBuilder.AppendLine(flatten2.Value);
				condition = true;
			}
		}
		if (condition)
		{
			output = stringBuilder.ToString().TrimEnd();
			return true;
		}
		string pathTerm = path + ".";
		string pathTerm2 = path + "[";
		bool condition2 = false;
		foreach (KeyValuePair<string, string> flatten3 in flatten)
		{
			if (flatten3.Key.StartsWith(pathTerm, StringComparison.Ordinal) || flatten3.Key.StartsWith(pathTerm2, StringComparison.Ordinal))
			{
				stringBuilder.Append(flatten3.Key).Append('=').AppendLine(flatten3.Value);
				condition2 = true;
			}
		}
		if (condition2)
		{
			output = stringBuilder.ToString().TrimEnd();
			return true;
		}
		suggestion = ClosestPath(path, flatten);
		return false;
	}

	private static string ClosestPath(string path, List<KeyValuePair<string, string>> pairs)
	{
		HashSet<string> ordinal = new HashSet<string>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, string> pair in pairs)
		{
			string key = pair.Key;
			ordinal.Add(key);
			for (int i = 1; i < key.Length; i++)
			{
				if (key[i] == '.' || key[i] == '[')
				{
					ordinal.Add(key.Substring(0, i));
				}
			}
		}
		string ordinal2 = null;
		int maxValue = int.MaxValue;
		foreach (string ordinal3 in ordinal)
		{
			int editDistance = EditDistance(path, ordinal3);
			if (editDistance < maxValue)
			{
				maxValue = editDistance;
				ordinal2 = ordinal3;
			}
		}
		int scaledLength = Math.Max(2, path.Length / 3);
		if (ordinal2 == null || maxValue > scaledLength)
		{
			return null;
		}
		return ordinal2;
	}

	internal static int EditDistance(string a, string b)
	{
		a = a.ToLowerInvariant();
		b = b.ToLowerInvariant();
		int[] intValues = new int[b.Length + 1];
		int[] intValuesArray = new int[b.Length + 1];
		for (int i = 0; i <= b.Length; i++)
		{
			intValues[i] = i;
		}
		for (int j = 1; j <= a.Length; j++)
		{
			intValuesArray[0] = j;
			for (int k = 1; k <= b.Length; k++)
			{
				int ordinal = ((a[j - 1] != b[k - 1]) ? 1 : 0);
				intValuesArray[k] = Math.Min(Math.Min(intValuesArray[k - 1] + 1, intValues[k] + 1), intValues[k - 1] + ordinal);
			}
			int[] intValues2 = intValuesArray;
			intValuesArray = intValues;
			intValues = intValues2;
		}
		return intValues[b.Length];
	}

	private static List<KeyValuePair<string, string>> Flatten(object model)
	{
		using JsonDocument jsonDocument = JsonSerializer.SerializeToDocument(model, Flat);
		List<KeyValuePair<string, string>> stringForStringList = new List<KeyValuePair<string, string>>();
		Walk(stringForStringList, "", jsonDocument.RootElement);
		return stringForStringList;
	}

	private static void Walk(List<KeyValuePair<string, string>> pairs, string prefix, JsonElement element)
	{
		switch (element.ValueKind)
		{
		case JsonValueKind.Object:
		{
			foreach (JsonProperty enumerateObject in element.EnumerateObject())
			{
				Walk(pairs, (prefix.Length == 0) ? enumerateObject.Name : (prefix + "." + enumerateObject.Name), enumerateObject.Value);
			}
			break;
		}
		case JsonValueKind.Array:
		{
			int ordinal = 0;
			{
				foreach (JsonElement enumerateArray in element.EnumerateArray())
				{
					Walk(pairs, $"{prefix}[{ordinal++}]", enumerateArray);
				}
				break;
			}
		}
		case JsonValueKind.Null:
			pairs.Add(new KeyValuePair<string, string>(prefix, ""));
			break;
		default:
			pairs.Add(new KeyValuePair<string, string>(prefix, element.ToString()));
			break;
		}
	}
}
