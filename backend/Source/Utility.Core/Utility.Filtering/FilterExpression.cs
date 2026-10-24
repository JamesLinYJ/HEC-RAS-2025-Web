using System.Collections.Generic;

namespace Utility.Filtering;

public abstract class FilterExpression
{
	public FilterOptions Options { get; set; }

	public static bool TryParse(string stringValue, out FilterExpression expression, out string error)
	{
		return FilterParser.TryParse(stringValue, out expression, out error);
	}

	public static bool TryParse(string stringValue, FilterOptions options, out FilterExpression expression, out string error)
	{
		if (!FilterParser.TryParse(stringValue, out expression, out error))
		{
			return false;
		}
		expression.Options = options;
		return true;
	}

	public bool Evaluate(IFilterRow row, FilterOptions options = null)
	{
		return EvaluateCore(row, options ?? Options ?? FilterOptions.Default);
	}

	public IReadOnlyList<FilterField> GetFields()
	{
		List<FilterField> filterFields = new List<FilterField>();
		CollectFields(filterFields);
		return filterFields;
	}

	internal abstract bool EvaluateCore(IFilterRow row, FilterOptions options);

	internal abstract void CollectFields(List<FilterField> fields);
}
