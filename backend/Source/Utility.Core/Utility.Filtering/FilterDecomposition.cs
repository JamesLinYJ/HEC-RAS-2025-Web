using System.Collections.Generic;

namespace Utility.Filtering;

public static class FilterDecomposition
{
	public static bool TryDecompose(FilterExpression expression, out Dictionary<string, FieldFilter> clausesByField)
	{
		clausesByField = null;
		if (expression == null)
		{
			return false;
		}
		Dictionary<string, FieldFilter> clausesByField2 = new Dictionary<string, FieldFilter>();
		if (!TryCollect(expression, clausesByField2))
		{
			return false;
		}
		clausesByField = clausesByField2;
		return true;
	}

	private static bool TryCollect(FilterExpression node, Dictionary<string, FieldFilter> collected)
	{
		if (node is AndFilter andFilter)
		{
			if (TryCollect(andFilter.Left, collected))
			{
				return TryCollect(andFilter.Right, collected);
			}
			return false;
		}
		if (!(node is FieldFilter fieldFilter))
		{
			return false;
		}
		return collected.TryAdd(fieldFilter.NormalizedFieldName, fieldFilter);
	}
}
