namespace Utility.Filtering;

public sealed class NullCheckFilter : FieldFilter
{
	public bool Negated { get; }

	public NullCheckFilter(string fieldName, bool negated)
		: base(fieldName)
	{
		Negated = negated;
	}

	internal override bool EvaluateCore(IFilterRow row, FilterOptions options)
	{
		bool notTryGetValueOrIsNullish = !row.TryGetValue(NormalizedFieldName, out var localValue) || FilterValues.IsNullish(localValue);
		if (!Negated)
		{
			return notTryGetValueOrIsNullish;
		}
		return !notTryGetValueOrIsNullish;
	}
}
