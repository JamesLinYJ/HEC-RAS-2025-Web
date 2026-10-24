using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Geospatial.Vectors;

namespace Geospatial.IO.Network.OSM;

public static class OverpassQlSanitizer
{
	public const string BoundingBoxPlaceholder = "{{bbox}}";

	private const string JsonSetting = "[out:json]";

	private const string JsonFormat = "json";

	private const string GeometryModifier = "geom";

	private const string CenterModifier = "center";

	private static readonly Regex _bboxPlaceholder = new Regex("\\{\\{\\s*bbox\\s*\\}\\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex _outFormatSetting = new Regex("\\[\\s*out\\s*:\\s*([A-Za-z]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex _outStatement = new Regex("\\bout\\b([^;]*);", RegexOptions.Compiled);

	private static readonly Regex _explicitBoundingBox = new Regex("\\(\\s*-?\\d+(\\.\\d+)?\\s*,\\s*-?\\d+(\\.\\d+)?\\s*,\\s*-?\\d+(\\.\\d+)?\\s*,\\s*-?\\d+(\\.\\d+)?\\s*\\)", RegexOptions.Compiled);

	private static readonly Regex _unresolvedPlaceholder = new Regex("\\{\\{[^}]*\\}\\}", RegexOptions.Compiled);

	private static readonly string[] SpatialConstraintTokens = new string[5] { "[bbox:", "area", "around:", "poly:", "(id:" };

	public static bool TryPrepare(string rawQl, Extent bboxWgs84, out string prepared, out string warning, out string error)
	{
		prepared = null;
		warning = null;
		error = null;
		if (string.IsNullOrWhiteSpace(rawQl))
		{
			error = "Enter an Overpass QL query.";
			return false;
		}
		string rawQl2 = rawQl;
		bool isMatch = _bboxPlaceholder.IsMatch(rawQl2);
		if (isMatch)
		{
			if (!bboxWgs84.IsFiniteAndValid())
			{
				error = "The map extent is not a valid area to query.";
				return false;
			}
			rawQl2 = _bboxPlaceholder.Replace(rawQl2, OverpassQl.FormatBoundingBox(bboxWgs84));
		}
		int skipLeadingTrivia = SkipLeadingTrivia(rawQl2);
		int indexOfOffset;
		if (skipLeadingTrivia < rawQl2.Length && rawQl2[skipLeadingTrivia] == '[')
		{
			int indexOf = rawQl2.IndexOf(';', skipLeadingTrivia);
			if (indexOf < 0)
			{
				error = "The settings block at the start of the query is missing its closing \";\".";
				return false;
			}
			string input = rawQl2.Substring(skipLeadingTrivia, indexOf - skipLeadingTrivia + 1);
			indexOfOffset = indexOf + 1;
			Match match = _outFormatSetting.Match(input);
			if (match.Success)
			{
				string groupsMatchTerm = match.Groups[1].Value;
				if (!string.Equals(groupsMatchTerm, "json", StringComparison.OrdinalIgnoreCase))
				{
					error = $"This query asks Overpass for \"{groupsMatchTerm}\" output. Only \"{"[out:json]"}\" can be imported - change the settings block and try again.";
					return false;
				}
			}
			else
			{
				rawQl2 = rawQl2.Insert(skipLeadingTrivia, "[out:json]");
				indexOfOffset += "[out:json]".Length;
			}
		}
		else
		{
			string message = $"{"[out:json]"}[timeout:{180}];\n";
			rawQl2 = message + rawQl2;
			indexOfOffset = message.Length;
		}
		List<string> stringList = new List<string>();
		if (!RequestsGeometry(rawQl2.Substring(indexOfOffset)))
		{
			stringList.Add("The query has no \"out geom\" or \"out center\" statement, so the result will carry tags but no shapes.");
		}
		if (!isMatch && !HasSpatialConstraint(rawQl2))
		{
			error = "The query is not bounded to an area, so it would search the entire planet. Add {{bbox}} to bound it to the current map extent, or give it an explicit bounding box.";
			return false;
		}
		Match match2 = _unresolvedPlaceholder.Match(rawQl2);
		if (match2.Success)
		{
			stringList.Add("\"" + match2.Value + "\" is an overpass-turbo editor placeholder and is sent to Overpass as-is, which will likely reject it.");
		}
		if (stringList.Count > 0)
		{
			warning = string.Join(" ", stringList);
		}
		prepared = rawQl2;
		return true;
	}

	private static int SkipLeadingTrivia(string textValue)
	{
		int intervalIndex = 0;
		while (intervalIndex < textValue.Length)
		{
			if (char.IsWhiteSpace(textValue[intervalIndex]))
			{
				intervalIndex++;
				continue;
			}
			bool condition = intervalIndex + 1 < textValue.Length;
			if (((textValue[intervalIndex] == '/') & condition) && textValue[intervalIndex + 1] == '/')
			{
				int indexOf = textValue.IndexOf('\n', intervalIndex);
				if (indexOf < 0)
				{
					return textValue.Length;
				}
				intervalIndex = indexOf + 1;
				continue;
			}
			if (((textValue[intervalIndex] == '/') & condition) && textValue[intervalIndex + 1] == '*')
			{
				int indexOf2 = textValue.IndexOf("*/", intervalIndex + 2, StringComparison.Ordinal);
				if (indexOf2 < 0)
				{
					return textValue.Length;
				}
				intervalIndex = indexOf2 + 2;
				continue;
			}
			return intervalIndex;
		}
		return textValue.Length;
	}

	private static bool RequestsGeometry(string body)
	{
		foreach (Match matche in _outStatement.Matches(body))
		{
			string groupsMatcheTerm = matche.Groups[1].Value;
			if (groupsMatcheTerm.Contains("geom", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (groupsMatcheTerm.Contains("center", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasSpatialConstraint(string textValue)
	{
		if (_explicitBoundingBox.IsMatch(textValue))
		{
			return true;
		}
		string[] spatialConstraintTokens = SpatialConstraintTokens;
		foreach (string spatialConstraintToken in spatialConstraintTokens)
		{
			if (textValue.Contains(spatialConstraintToken, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
