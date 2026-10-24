using System.Collections.Generic;

namespace Utility.Filtering;

public sealed class PathSelector
{
	public IReadOnlyList<PathSegment> Segments { get; }

	public bool HasAssignment { get; }

	public FilterLiteral AssignmentValue { get; }

	private PathSelector(List<PathSegment> segments, bool hasAssignment, FilterLiteral assignmentValue)
	{
		Segments = segments;
		HasAssignment = hasAssignment;
		AssignmentValue = assignmentValue;
	}

	public static bool TryParse(string stringValue, out PathSelector selector, out string error)
	{
		return TryParse(stringValue, FilterOptions.Default, out selector, out error);
	}

	public static bool TryParse(string stringValue, FilterOptions options, out PathSelector selector, out string error)
	{
		selector = null;
		error = null;
		if (string.IsNullOrWhiteSpace(stringValue))
		{
			error = "Selector is empty.";
			return false;
		}
		if (!TrySplitAssignment(stringValue, out var pathPart, out var valuePart, out error))
		{
			return false;
		}
		FilterLiteral literal = null;
		bool hasValuePart = valuePart != null;
		if (hasValuePart && !FilterLiteral.TryParse(valuePart.Trim(), out literal, out error))
		{
			return false;
		}
		if (!TrySplitSegments(pathPart, out var segments, out error))
		{
			return false;
		}
		if (segments.Count == 0)
		{
			error = "Selector has no path.";
			return false;
		}
		List<PathSegment> pathSegments = new List<PathSegment>();
		foreach (string segment2 in segments)
		{
			if (!TryParseSegment(segment2, options, out var segment, out error))
			{
				return false;
			}
			pathSegments.Add(segment);
		}
		selector = new PathSelector(pathSegments, hasValuePart, literal);
		return true;
	}

	private static bool TrySplitAssignment(string pathPart2, out string pathPart, out string valuePart, out string error)
	{
		pathPart = pathPart2;
		valuePart = null;
		error = null;
		int ordinal = 0;
		char c = '\0';
		for (int i = 0; i < pathPart2.Length; i++)
		{
			char c2 = pathPart2[i];
			if (c != '\0')
			{
				if (c2 == c)
				{
					if (i + 1 < pathPart2.Length && pathPart2[i + 1] == c)
					{
						i++;
					}
					else
					{
						c = '\0';
					}
				}
				continue;
			}
			switch (c2)
			{
			case '"':
			case '\'':
				c = c2;
				break;
			case '(':
			case '[':
				ordinal++;
				break;
			case ')':
			case ']':
				ordinal--;
				break;
			case '=':
				if (ordinal == 0)
				{
					pathPart = pathPart2.Substring(0, i);
					valuePart = pathPart2.Substring(i + 1);
					return true;
				}
				break;
			}
		}
		if (ordinal != 0)
		{
			error = "Unbalanced brackets/parentheses in selector.";
			return false;
		}
		if (c != '\0')
		{
			error = "Unterminated string in selector.";
			return false;
		}
		return true;
	}

	private static bool TrySplitSegments(string path, out List<string> segments, out string error)
	{
		segments = new List<string>();
		error = null;
		int ordinal = 0;
		char c = '\0';
		int ordinal2 = 0;
		for (int i = 0; i < path.Length; i++)
		{
			char c2 = path[i];
			if (c != '\0')
			{
				if (c2 == c)
				{
					if (i + 1 < path.Length && path[i + 1] == c)
					{
						i++;
					}
					else
					{
						c = '\0';
					}
				}
				continue;
			}
			switch (c2)
			{
			case '"':
			case '\'':
				c = c2;
				break;
			case '(':
			case '[':
				ordinal++;
				break;
			case ')':
			case ']':
				ordinal--;
				break;
			case '.':
				if (ordinal == 0)
				{
					segments.Add(path.Substring(ordinal2, i - ordinal2));
					ordinal2 = i + 1;
				}
				break;
			}
		}
		if (ordinal != 0)
		{
			error = "Unbalanced brackets/parentheses in selector.";
			return false;
		}
		if (c != '\0')
		{
			error = "Unterminated string in selector.";
			return false;
		}
		segments.Add(path.Substring(ordinal2));
		return true;
	}

	private static bool TryParseSegment(string raw, FilterOptions options, out PathSegment segment, out string error)
	{
		segment = null;
		error = null;
		string trim = raw.Trim();
		int indexOf = trim.IndexOf('[');
		int indexOf2 = trim.IndexOf('(');
		if (indexOf < 0 && indexOf2 < 0)
		{
			if (trim.Length == 0)
			{
				error = "Empty path segment.";
				return false;
			}
			segment = new PathSegment(trim, null);
			return true;
		}
		if (indexOf2 >= 0 && (indexOf < 0 || indexOf2 < indexOf))
		{
			return TryParseCallSegment(trim, indexOf2, out segment, out error);
		}
		return TryParsePredicateSegment(trim, indexOf, options, out segment, out error);
	}

	private static bool TryParsePredicateSegment(string trimmed, int bracket, FilterOptions options, out PathSegment segment, out string error)
	{
		segment = null;
		if (!trimmed.EndsWith("]"))
		{
			error = "Expected ']' to close the predicate in '" + trimmed + "'.";
			return false;
		}
		string trim = trimmed.Substring(0, bracket).Trim();
		if (trim.Length == 0)
		{
			error = "Missing collection name before '[' in '" + trimmed + "'.";
			return false;
		}
		string trim2 = trimmed.Substring(bracket + 1, trimmed.Length - bracket - 2).Trim();
		if (trim2.Length == 0)
		{
			error = "Empty predicate '[]' on '" + trim + "'.";
			return false;
		}
		if (!TryParsePredicate(trim2, options, out var predicate, out error))
		{
			return false;
		}
		segment = new PathSegment(trim, predicate);
		return true;
	}

	private static bool TryParseCallSegment(string trimmed, int paren, out PathSegment segment, out string error)
	{
		segment = null;
		error = null;
		if (!trimmed.EndsWith(")"))
		{
			error = "Expected ')' to close the call in '" + trimmed + "'.";
			return false;
		}
		string trim = trimmed.Substring(0, paren).Trim();
		if (trim.Length == 0)
		{
			error = "Missing field name before '(' in '" + trimmed + "'.";
			return false;
		}
		string trim2 = trimmed.Substring(paren + 1, trimmed.Length - paren - 2).Trim();
		if (!TrySplitArguments(trim2, out var arguments, out error))
		{
			return false;
		}
		List<FilterLiteral> filterLiterals = new List<FilterLiteral>();
		foreach (string argument in arguments)
		{
			if (!FilterLiteral.TryParse(argument.Trim(), out var literal, out error))
			{
				return false;
			}
			filterLiterals.Add(literal);
		}
		segment = new PathSegment(trim, null, new PathCall(filterLiterals, trim2));
		return true;
	}

	private static bool TrySplitArguments(string interior, out List<string> arguments, out string error)
	{
		arguments = new List<string>();
		error = null;
		if (interior.Length == 0)
		{
			return true;
		}
		int ordinal = 0;
		char c = '\0';
		int ordinal2 = 0;
		for (int i = 0; i < interior.Length; i++)
		{
			char c2 = interior[i];
			if (c != '\0')
			{
				if (c2 == c)
				{
					if (i + 1 < interior.Length && interior[i + 1] == c)
					{
						i++;
					}
					else
					{
						c = '\0';
					}
				}
				continue;
			}
			switch (c2)
			{
			case '"':
			case '\'':
				c = c2;
				break;
			case '(':
			case '[':
				ordinal++;
				break;
			case ')':
			case ']':
				ordinal--;
				break;
			case ',':
				if (ordinal == 0)
				{
					arguments.Add(interior.Substring(ordinal2, i - ordinal2));
					ordinal2 = i + 1;
				}
				break;
			}
		}
		if (ordinal != 0)
		{
			error = "Unbalanced brackets/parentheses in call arguments.";
			return false;
		}
		if (c != '\0')
		{
			error = "Unterminated string in call arguments.";
			return false;
		}
		arguments.Add(interior.Substring(ordinal2));
		return true;
	}

	private static bool TryParsePredicate(string interior, FilterOptions options, out PathPredicate predicate, out string error)
	{
		predicate = null;
		if (int.TryParse(interior, out var result))
		{
			if (result < 0)
			{
				error = "Index '" + interior + "' must be non-negative.";
				return false;
			}
			predicate = PathPredicate.FromIndex(result, interior);
			error = null;
			return true;
		}
		if (!FilterExpression.TryParse(interior, options, out var expression, out error))
		{
			return false;
		}
		predicate = PathPredicate.FromFilter(expression, interior);
		return true;
	}
}
