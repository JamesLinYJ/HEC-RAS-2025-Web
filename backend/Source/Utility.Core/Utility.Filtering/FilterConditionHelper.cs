using System;

namespace Utility.Filtering;

public static class FilterConditionHelper
{
	public static Type Type => typeof(FilterCondition);

	public static bool TakesValue(FilterCondition condition)
	{
		if (condition != FilterCondition.IsBlank)
		{
			return condition != FilterCondition.IsNotBlank;
		}
		return false;
	}

	public static string BuildClauseText(string fieldReference, FilterCondition condition, string stringValue, bool numericField)
	{
		return condition switch
		{
			FilterCondition.Contains => FilterText.Contains(fieldReference, stringValue, negated: false), 
			FilterCondition.DoesNotContain => FilterText.Contains(fieldReference, stringValue, negated: true), 
			FilterCondition.IsBlank => FilterText.IsNull(fieldReference, negated: false), 
			FilterCondition.IsNotBlank => FilterText.IsNull(fieldReference, negated: true), 
			FilterCondition.NotEquals => FilterText.Comparison(fieldReference, FilterComparisonOperator.NotEqual, stringValue, numericField), 
			FilterCondition.GreaterThan => FilterText.Comparison(fieldReference, FilterComparisonOperator.Greater, stringValue, numericField), 
			FilterCondition.AtLeast => FilterText.Comparison(fieldReference, FilterComparisonOperator.GreaterOrEqual, stringValue, numericField), 
			FilterCondition.LessThan => FilterText.Comparison(fieldReference, FilterComparisonOperator.Less, stringValue, numericField), 
			FilterCondition.AtMost => FilterText.Comparison(fieldReference, FilterComparisonOperator.LessOrEqual, stringValue, numericField), 
			_ => FilterText.Comparison(fieldReference, FilterComparisonOperator.Equal, stringValue, numericField), 
		};
	}

	public static bool TryReadClause(FieldFilter clause, out FilterCondition condition, out string stringValue)
	{
		condition = FilterCondition.Equals;
		stringValue = string.Empty;
		if (clause is NullCheckFilter nullCheckFilter)
		{
			condition = (nullCheckFilter.Negated ? FilterCondition.IsNotBlank : FilterCondition.IsBlank);
			return true;
		}
		if (clause is LikeFilter likeFilter)
		{
			if (!likeFilter.Pattern.StartsWith('%') || !likeFilter.Pattern.EndsWith('%') || likeFilter.Pattern.Length < 2)
			{
				return false;
			}
			condition = (likeFilter.Negated ? FilterCondition.DoesNotContain : FilterCondition.Contains);
			string pattern = likeFilter.Pattern;
			stringValue = pattern.Substring(1, pattern.Length - 1 - 1);
			return true;
		}
		if (!(clause is ComparisonFilter comparisonFilter))
		{
			return false;
		}
		condition = comparisonFilter.Operator switch
		{
			FilterComparisonOperator.NotEqual => FilterCondition.NotEquals, 
			FilterComparisonOperator.Greater => FilterCondition.GreaterThan, 
			FilterComparisonOperator.GreaterOrEqual => FilterCondition.AtLeast, 
			FilterComparisonOperator.Less => FilterCondition.LessThan, 
			FilterComparisonOperator.LessOrEqual => FilterCondition.AtMost, 
			_ => FilterCondition.Equals, 
		};
		stringValue = comparisonFilter.Value.Text;
		return true;
	}
}
