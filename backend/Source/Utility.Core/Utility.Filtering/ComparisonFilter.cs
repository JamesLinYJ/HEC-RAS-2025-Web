namespace Utility.Filtering;

public sealed class ComparisonFilter : FieldFilter
{
	public FilterComparisonOperator Operator { get; }

	public FilterLiteral Value { get; }

	public ComparisonFilter(string fieldName, FilterComparisonOperator comparisonOperator, FilterLiteral filterLiteral)
		: base(fieldName)
	{
		Operator = comparisonOperator;
		Value = filterLiteral;
	}

	internal override bool EvaluateCore(IFilterRow row, FilterOptions options)
	{
		if (!row.TryGetValue(NormalizedFieldName, out var localValue))
		{
			return false;
		}
		return FilterValues.Matches(localValue, Operator, Value, options);
	}
}
