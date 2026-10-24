using System;
using System.Numerics;
using Utility.Extensions;

namespace Geospatial.Rendering;

public static class ConditionPredicates
{
	public static Func<object, bool> Build(ConditionOperator op, string threshold, Type columnType)
	{
		if (IsStringOperator(op))
		{
			return BuildString(op, threshold);
		}
		return BuildNumeric(op, threshold, columnType);
	}

	private static Func<object, bool> BuildNumeric<T>(ConditionOperator op, T t) where T : INumber<T>
	{
		return op switch
		{
			ConditionOperator.Equal => (Func<object, bool>)((object v) => (T)v == t), 
			ConditionOperator.NotEqual => (object v) => (T)v != t, 
			ConditionOperator.GreaterThan => (object v) => (T)v > t, 
			ConditionOperator.GreaterThanOrEqual => (object v) => (T)v >= t, 
			ConditionOperator.LessThan => (object v) => (T)v < t, 
			ConditionOperator.LessThanOrEqual => (object v) => (T)v <= t, 
			_ => throw new ArgumentException($"Operator {op} is not a numeric operator."), 
		};
	}

	private static Func<object, bool> BuildNumeric(ConditionOperator op, string threshold, Type columnType)
	{
		if (columnType == typeof(int))
		{
			int t = int.Parse(threshold);
			return BuildNumeric(op, t);
		}
		if (columnType == typeof(long))
		{
			long t2 = long.Parse(threshold);
			return BuildNumeric(op, t2);
		}
		if (columnType == typeof(float))
		{
			float t3 = float.Parse(threshold);
			return BuildNumeric(op, t3);
		}
		if (columnType == typeof(double))
		{
			double t4 = double.Parse(threshold);
			return BuildNumeric(op, t4);
		}
		throw new ArgumentException($"Column type {columnType} must be an integer, long, float, or double.");
	}

	private static Func<object, bool> BuildString(ConditionOperator op, string threshold)
	{
		return op switch
		{
			ConditionOperator.StringEquals => (Func<object, bool>)((object v) => ((string)v).RoughlyEquals(threshold)), 
			ConditionOperator.StringNotEquals => (object v) => !((string)v).RoughlyEquals(threshold), 
			ConditionOperator.StringContains => (object v) => ((string)v).Contains(threshold, StringComparison.OrdinalIgnoreCase), 
			ConditionOperator.StringStartsWith => (object v) => ((string)v).RoughlyStartsWith(threshold), 
			ConditionOperator.StringEndsWith => (object v) => ((string)v).RoughlyEndsWith(threshold), 
			_ => throw new ArgumentException($"Operator {op} is not a string operator."), 
		};
	}

	public static bool IsStringOperator(ConditionOperator op)
	{
		if ((uint)(op - 6) <= 4u)
		{
			return true;
		}
		return false;
	}
}
