using System.Collections.Generic;

namespace Utility.Filtering;

public sealed class NotFilter : FilterExpression
{
	public FilterExpression Inner { get; }

	public NotFilter(FilterExpression inner)
	{
		Inner = inner;
	}

	internal override bool EvaluateCore(IFilterRow row, FilterOptions options)
	{
		return !Inner.EvaluateCore(row, options);
	}

	internal override void CollectFields(List<FilterField> fields)
	{
		Inner.CollectFields(fields);
	}
}
