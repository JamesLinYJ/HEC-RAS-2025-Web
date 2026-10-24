using System;
using System.Collections.Generic;
using System.Linq;

namespace Testing.Core;

public static class PropertyQueryExtensions
{
	public static IEnumerable<T> WhereSatisfiesPropertyComparison<T>(this IEnumerable<T> enumerable, PropertyComparison comparison) where T : class
	{
		return enumerable.Where((T element) => element.SatisfiesPropertyComparison(comparison));
	}

	public static bool SatisfiesPropertyComparison<T>(this T localValue, PropertyComparison comparison) where T : class
	{
		if (!(localValue.GetType().GetProperty(comparison.PropertyName).GetValue(localValue) is IComparable comparable))
		{
			return false;
		}
		if (comparison.CompareType == CompareType.Contains)
		{
			if (comparable.GetType() != typeof(string))
			{
				throw new InvalidOperationException("Cannot do contains property comparison on non-string type");
			}
			if (comparison.Value.GetType() != typeof(string))
			{
				return false;
			}
			string comparable2 = comparable as string;
			string comparisonTerm = comparison.Value as string;
			return comparable2.Contains(comparisonTerm);
		}
		return comparison.CompareType switch
		{
			CompareType.GreaterThan => comparable.CompareTo(comparison.Value) > 0, 
			CompareType.LessThan => comparable.CompareTo(comparison.Value) < 0, 
			CompareType.EqualTo => comparable.CompareTo(comparison.Value) == 0, 
			_ => false, 
		};
	}
}
