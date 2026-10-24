using System;
using System.Collections.Generic;
using Utility.DataStructures;

namespace Geospatial.Rendering;

public static class ConditionOperatorMap
{
	private static readonly Dictionary<string, ConditionOperator> _toEnum = new Dictionary<string, ConditionOperator>(StringComparer.OrdinalIgnoreCase)
	{
		["=="] = ConditionOperator.Equal,
		["!="] = ConditionOperator.NotEqual,
		[">"] = ConditionOperator.GreaterThan,
		[">="] = ConditionOperator.GreaterThanOrEqual,
		["<"] = ConditionOperator.LessThan,
		["<="] = ConditionOperator.LessThanOrEqual,
		["equals"] = ConditionOperator.StringEquals,
		["not equals"] = ConditionOperator.StringNotEquals,
		["contains"] = ConditionOperator.StringContains,
		["starts with"] = ConditionOperator.StringStartsWith,
		["ends with"] = ConditionOperator.StringEndsWith
	};

	public static Map<string, ConditionOperator> Map = new Map<string, ConditionOperator>(_toEnum);

	public static readonly string[] NumericOperators = new string[6] { "==", "!=", ">", ">=", "<", "<=" };

	public static readonly string[] StringOperators = new string[5] { "equals", "not equals", "contains", "starts with", "ends with" };

	public static string[] GetOperatorsForType(Type columnType)
	{
		if (columnType == typeof(string))
		{
			return StringOperators;
		}
		return NumericOperators;
	}
}
