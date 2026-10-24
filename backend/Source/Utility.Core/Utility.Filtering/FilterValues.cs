using System;
using System.Globalization;
using Utility.Units;

namespace Utility.Filtering;

internal static class FilterValues
{
	public static bool IsNullish(object localValue)
	{
		if (localValue == null)
		{
			return true;
		}
		if (localValue is float f)
		{
			return float.IsNaN(f);
		}
		if (localValue is double d)
		{
			return double.IsNaN(d);
		}
		if (localValue is string textValue)
		{
			return string.IsNullOrWhiteSpace(textValue);
		}
		return false;
	}

	public static bool Matches(object localValue, FilterComparisonOperator comparisonOperator, FilterLiteral literal, FilterOptions options)
	{
		if (IsNullish(localValue))
		{
			return false;
		}
		Type type = localValue.GetType();
		if (type.IsEnum)
		{
			return MatchesEnum(localValue, type, comparisonOperator, literal);
		}
		if (localValue is bool condition)
		{
			return MatchesBool(condition, comparisonOperator, literal);
		}
		if (IsNumericType(type))
		{
			return MatchesNumber(localValue, comparisonOperator, literal);
		}
		if (localValue is DateTime dateTime)
		{
			return MatchesDateTime(dateTime, comparisonOperator, literal);
		}
		if (localValue is TimeSpan timeSpan)
		{
			return MatchesTimeSpan(timeSpan, comparisonOperator, literal);
		}
		return MatchesString(ToComparableString(localValue), comparisonOperator, literal, options);
	}

	public static bool TryCoerce(FilterLiteral literal, Type target, out object toObjectTargetTerm, out string error)
	{
		toObjectTargetTerm = null;
		error = null;
		if (target.IsEnum)
		{
			if (literal.IsNumber)
			{
				toObjectTargetTerm = Enum.ToObject(target, (long)literal.NumberValue);
				return true;
			}
			if (Enum.TryParse(target, literal.Text, ignoreCase: true, out object result))
			{
				toObjectTargetTerm = result;
				return true;
			}
			error = $"'{literal.Text}' is not a valid {target.Name} value.";
			return false;
		}
		if (target == typeof(bool))
		{
			if (literal.IsNumber)
			{
				toObjectTargetTerm = literal.NumberValue != 0.0;
				return true;
			}
			if (bool.TryParse(literal.Text, out var result2))
			{
				toObjectTargetTerm = result2;
				return true;
			}
			error = "'" + literal.Text + "' is not a valid boolean.";
			return false;
		}
		if (target == typeof(string))
		{
			toObjectTargetTerm = literal.Text;
			return true;
		}
		if (target == typeof(DateTime))
		{
			if (DateTime.TryParse(literal.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result3))
			{
				toObjectTargetTerm = result3;
				return true;
			}
			error = "'" + literal.Text + "' is not a date/time; expected e.g. '2000-01-01 00:00'.";
			return false;
		}
		if (target == typeof(TimeSpan))
		{
			if (!TryParseDuration(literal, out var toObjectTargetTerm2, out error))
			{
				return false;
			}
			toObjectTargetTerm = toObjectTargetTerm2;
			return true;
		}
		if (IsNumericType(target))
		{
			if (!literal.TryGetNumber(out var number))
			{
				error = "'" + literal.Text + "' is not a number.";
				return false;
			}
			try
			{
				toObjectTargetTerm = Convert.ChangeType(number, target, CultureInfo.InvariantCulture);
				return true;
			}
			catch
			{
				error = $"'{literal.Text}' is out of range for {target.Name}.";
				return false;
			}
		}
		error = "Cannot assign to a field of type " + target.Name + ".";
		return false;
	}

	public static string ToComparableString(object localValue)
	{
		if (localValue is string result)
		{
			return result;
		}
		return Convert.ToString(localValue, CultureInfo.InvariantCulture) ?? "";
	}

	public static bool TryToDouble(object localValue, out double number)
	{
		number = 0.0;
		if (IsNullish(localValue))
		{
			return false;
		}
		try
		{
			number = Convert.ToDouble(localValue, CultureInfo.InvariantCulture);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryParseDuration(FilterLiteral literal, out TimeSpan duration, out string error)
	{
		duration = default;
		error = null;
		if (literal.TryGetNumber(out var _))
		{
			error = "'" + literal.Text + "' needs a time unit; expected e.g. 30s, 15min, 1.5h, or '01:30:00'.";
			return false;
		}
		try
		{
			if (Time.TryParseTimestep(literal.Text, out duration))
			{
				return true;
			}
		}
		catch (OverflowException)
		{
			error = "'" + literal.Text + "' is out of range for a duration.";
			return false;
		}
		if (TimeSpan.TryParse(literal.Text, CultureInfo.InvariantCulture, out duration))
		{
			return true;
		}
		error = "'" + literal.Text + "' is not a duration; expected e.g. 30s, 15min, 1.5h, or '01:30:00'.";
		return false;
	}

	private static bool MatchesEnum(object localValue, Type type, FilterComparisonOperator comparisonOperator, FilterLiteral literal)
	{
		if (literal.IsNumber)
		{
			return Apply(comparisonOperator, Convert.ToDouble(localValue).CompareTo(literal.NumberValue));
		}
		if (!Enum.TryParse(type, literal.Text, ignoreCase: true, out object result))
		{
			return false;
		}
		return Apply(comparisonOperator, Convert.ToInt64(localValue).CompareTo(Convert.ToInt64(result)));
	}

	private static bool MatchesBool(bool condition, FilterComparisonOperator comparisonOperator, FilterLiteral literal)
	{
		bool result;
		if (literal.IsNumber)
		{
			result = literal.NumberValue != 0.0;
		}
		else if (!bool.TryParse(literal.Text, out result))
		{
			return false;
		}
		return Apply(comparisonOperator, condition.CompareTo(result));
	}

	private static bool MatchesNumber(object localValue, FilterComparisonOperator comparisonOperator, FilterLiteral literal)
	{
		if (!literal.TryGetNumber(out var number))
		{
			return false;
		}
		if (localValue is float)
		{
			number = (float)number;
		}
		return Apply(comparisonOperator, Convert.ToDouble(localValue, CultureInfo.InvariantCulture).CompareTo(number));
	}

	private static bool MatchesDateTime(DateTime dateTime, FilterComparisonOperator comparisonOperator, FilterLiteral literal)
	{
		if (!DateTime.TryParse(literal.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
		{
			return false;
		}
		return Apply(comparisonOperator, dateTime.CompareTo(result));
	}

	private static bool MatchesTimeSpan(TimeSpan timeSpan, FilterComparisonOperator comparisonOperator, FilterLiteral literal)
	{
		if (!TryParseDuration(literal, out var duration, out var _))
		{
			return false;
		}
		return Apply(comparisonOperator, timeSpan.CompareTo(duration));
	}

	private static bool MatchesString(string textValue, FilterComparisonOperator comparisonOperator, FilterLiteral literal, FilterOptions options)
	{
		return Apply(comparisonOperator, string.Compare(textValue, literal.Text, options.StringComparison));
	}

	public static bool IsNumericType(Type type)
	{
		if (!(type == typeof(double)) && !(type == typeof(float)) && !(type == typeof(int)) && !(type == typeof(long)) && !(type == typeof(short)) && !(type == typeof(byte)) && !(type == typeof(uint)) && !(type == typeof(ulong)) && !(type == typeof(ushort)) && !(type == typeof(sbyte)))
		{
			return type == typeof(decimal);
		}
		return true;
	}

	private static bool Apply(FilterComparisonOperator comparisonOperator, int comparison)
	{
		return comparisonOperator switch
		{
			FilterComparisonOperator.Equal => comparison == 0, 
			FilterComparisonOperator.NotEqual => comparison != 0, 
			FilterComparisonOperator.Less => comparison < 0, 
			FilterComparisonOperator.LessOrEqual => comparison <= 0, 
			FilterComparisonOperator.Greater => comparison > 0, 
			FilterComparisonOperator.GreaterOrEqual => comparison >= 0, 
			_ => false, 
		};
	}
}
