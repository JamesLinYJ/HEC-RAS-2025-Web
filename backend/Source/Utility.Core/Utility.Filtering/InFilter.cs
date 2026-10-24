using System.Collections.Generic;

namespace Utility.Filtering;

public sealed class InFilter : FieldFilter
{
	public IReadOnlyList<FilterLiteral> Values { get; }

	public bool Negated { get; }

	public InFilter(string fieldName, List<FilterLiteral> values, bool negated)
		: base(fieldName)
	{
		Values = values;
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
		bool condition = false;
		foreach (FilterLiteral filterLiteral in Values)
		{
			if (FilterValues.Matches(localValue, FilterComparisonOperator.Equal, filterLiteral, options))
			{
				condition = true;
				break;
			}
		}
		if (!Negated)
		{
			return condition;
		}
		return !condition;
	}
}
