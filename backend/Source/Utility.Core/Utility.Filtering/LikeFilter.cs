using System.Text;
using System.Text.RegularExpressions;

namespace Utility.Filtering;

public sealed class LikeFilter : FieldFilter
{
	private Regex _caseSensitiveRegex;

	private Regex _caseInsensitiveRegex;

	public string Pattern { get; }

	public bool Negated { get; }

	public LikeFilter(string fieldName, string pattern, bool negated)
		: base(fieldName)
	{
		Pattern = pattern;
		Negated = negated;
	}

	internal override bool EvaluateCore(IFilterRow row, FilterOptions options)
	{
		if (!row.TryGetValue(NormalizedFieldName, out var localValue))
		{
			return false;
		}
		if (FilterValues.IsNullish(localValue))
		{
			return false;
		}
		string input = FilterValues.ToComparableString(localValue);
		bool isMatch = GetRegex(options.CaseSensitiveStrings).IsMatch(input);
		if (!Negated)
		{
			return isMatch;
		}
		return !isMatch;
	}

	private Regex GetRegex(bool caseSensitive)
	{
		if (caseSensitive)
		{
			return _caseSensitiveRegex ?? (_caseSensitiveRegex = BuildRegex(RegexOptions.Singleline));
		}
		return _caseInsensitiveRegex ?? (_caseInsensitiveRegex = BuildRegex(RegexOptions.IgnoreCase | RegexOptions.Singleline));
	}

	private Regex BuildRegex(RegexOptions regexOptions)
	{
		StringBuilder stringBuilder = new StringBuilder("^");
		string pattern = Pattern;
		for (int i = 0; i < pattern.Length; i++)
		{
			char c = pattern[i];
			switch (c)
			{
			case '%':
				stringBuilder.Append(".*");
				break;
			case '_':
				stringBuilder.Append('.');
				break;
			default:
				stringBuilder.Append(Regex.Escape(c.ToString()));
				break;
			}
		}
		stringBuilder.Append('$');
		return new Regex(stringBuilder.ToString(), regexOptions);
	}
}
