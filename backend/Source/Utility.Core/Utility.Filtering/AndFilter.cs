using System.Collections.Generic;

namespace Utility.Filtering;

public sealed class AndFilter : FilterExpression
{
	public FilterExpression Left { get; }

	public FilterExpression Right { get; }

	public AndFilter(FilterExpression left, FilterExpression right)
	{
		Left = left;
		Right = right;
	}

	internal override bool EvaluateCore(IFilterRow row, FilterOptions options)
	{
		if (Left.EvaluateCore(row, options))
		{
			return Right.EvaluateCore(row, options);
		}
		return false;
	}

	internal override void CollectFields(List<FilterField> fields)
	{
		Left.CollectFields(fields);
		Right.CollectFields(fields);
	}
}
