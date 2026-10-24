using System.Collections.Generic;

namespace Utility.Filtering;

public sealed class OrFilter : FilterExpression
{
	public FilterExpression Left { get; }

	public FilterExpression Right { get; }

	public OrFilter(FilterExpression left, FilterExpression right)
	{
		Left = left;
		Right = right;
	}

	internal override bool EvaluateCore(IFilterRow row, FilterOptions options)
	{
		if (!Left.EvaluateCore(row, options))
		{
			return Right.EvaluateCore(row, options);
		}
		return true;
	}

	internal override void CollectFields(List<FilterField> fields)
	{
		Left.CollectFields(fields);
		Right.CollectFields(fields);
	}
}
