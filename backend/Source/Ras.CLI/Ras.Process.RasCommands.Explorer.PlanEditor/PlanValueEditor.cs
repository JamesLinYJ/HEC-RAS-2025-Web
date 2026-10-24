using System;
using System.Globalization;
using System.Linq;
using Utility.Reflection;

namespace Ras.Process.RasCommands.Explorer.PlanEditor;

public static class PlanValueEditor
{
	public static string FormatValue(object localValue, Type type)
	{
		if (localValue == null)
		{
			return "";
		}
		if (type == typeof(bool))
		{
			if (!(bool)localValue)
			{
				return "No";
			}
			return "Yes";
		}
		if (type == typeof(TimeSpan))
		{
			return FormatTimeSpan((TimeSpan)localValue);
		}
		if (type == typeof(DateTime))
		{
			return ((DateTime)localValue).ToString("yyyy-MM-dd HH:mm:ss");
		}
		if (type.IsEnum)
		{
			return FormatEnum(localValue);
		}
		if (type == typeof(double))
		{
			return ((double)localValue).ToString("G6", CultureInfo.InvariantCulture);
		}
		if (type == typeof(float))
		{
			return ((float)localValue).ToString("G6", CultureInfo.InvariantCulture);
		}
		return localValue.ToString();
	}

	private static string FormatTimeSpan(TimeSpan ts)
	{
		if (ts.Days <= 0)
		{
			return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
		}
		return $"{ts.Days}d {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
	}

	private static string FormatEnum(object localValue)
	{
		if (ReflectionHelpers.TryGetEnumDisplayName(localValue, out var displayName))
		{
			return displayName;
		}
		return localValue.ToString();
	}

	public static bool TryParseValue(string input, Type targetType, out object result, out string error)
	{
		error = null;
		result = null;
		if (string.IsNullOrWhiteSpace(input))
		{
			error = "Value cannot be empty";
			return false;
		}
		try
		{
			if (targetType == typeof(bool))
			{
				return TryParseBool(input, out result, out error);
			}
			if (targetType == typeof(int))
			{
				return TryParseInt(input, out result, out error);
			}
			if (targetType == typeof(double))
			{
				return TryParseDouble(input, out result, out error);
			}
			if (targetType == typeof(float))
			{
				return TryParseFloat(input, out result, out error);
			}
			if (targetType == typeof(TimeSpan))
			{
				return TryParseTimeSpan(input, out result, out error);
			}
			if (targetType == typeof(DateTime))
			{
				return TryParseDateTime(input, out result, out error);
			}
			if (targetType.IsEnum)
			{
				return TryParseEnum(input, targetType, out result, out error);
			}
			if (targetType == typeof(string))
			{
				result = input;
				return true;
			}
			error = "Unsupported type: " + targetType.Name;
			return false;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static bool TryParseBool(string input, out object result, out string error)
	{
		error = null;
		switch (input.Trim().ToLowerInvariant())
		{
		case "yes":
		case "true":
		case "1":
		case "y":
			result = true;
			return true;
		case "no":
		case "false":
		case "0":
		case "n":
			result = false;
			return true;
		default:
			result = null;
			error = "Enter Yes or No";
			return false;
		}
	}

	private static bool TryParseInt(string input, out object result, out string error)
	{
		error = null;
		if (int.TryParse(input.Trim(), out var result2))
		{
			result = result2;
			return true;
		}
		result = null;
		error = "Invalid integer";
		return false;
	}

	private static bool TryParseDouble(string input, out object result, out string error)
	{
		error = null;
		if (double.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result2))
		{
			result = result2;
			return true;
		}
		result = null;
		error = "Invalid number";
		return false;
	}

	private static bool TryParseFloat(string input, out object result, out string error)
	{
		error = null;
		if (float.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result2))
		{
			result = result2;
			return true;
		}
		result = null;
		error = "Invalid number";
		return false;
	}

	private static bool TryParseTimeSpan(string input, out object result, out string error)
	{
		error = null;
		input = input.Trim();
		if (TimeSpan.TryParse(input, out var result2))
		{
			result = result2;
			return true;
		}
		if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var result3))
		{
			result = TimeSpan.FromSeconds(result3);
			return true;
		}
		if (input.Contains('d'))
		{
			string[] parts = input.Split('d', StringSplitOptions.TrimEntries);
			if (parts.Length == 2 && int.TryParse(parts[0], out var result4) && TimeSpan.TryParse(parts[1], out var result5))
			{
				result = TimeSpan.FromDays(result4) + result5;
				return true;
			}
		}
		result = null;
		error = "Enter as HH:MM:SS, seconds, or Xd HH:MM:SS";
		return false;
	}

	private static bool TryParseDateTime(string input, out object result, out string error)
	{
		error = null;
		input = input.Trim();
		string[] formats = new string[6] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm", "MM/dd/yyyy" };
		if (DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result2))
		{
			result = result2;
			return true;
		}
		if (DateTime.TryParse(input, out result2))
		{
			result = result2;
			return true;
		}
		result = null;
		error = "Enter as yyyy-MM-dd HH:mm:ss";
		return false;
	}

	private static bool TryParseEnum(string input, Type enumType, out object result, out string error)
	{
		error = null;
		input = input.Trim();
		if (Enum.TryParse(enumType, input, ignoreCase: true, out result))
		{
			return true;
		}
		foreach (object getValue in Enum.GetValues(enumType))
		{
			if (ReflectionHelpers.TryGetEnumDisplayName(getValue, out var displayName) && string.Equals(displayName, input, StringComparison.OrdinalIgnoreCase))
			{
				result = getValue;
				return true;
			}
		}
		result = null;
		string getNamesEnumTypeTerm = string.Join(", ", Enum.GetNames(enumType));
		error = "Valid values: " + getNamesEnumTypeTerm;
		return false;
	}

	public static string Validate(object localValue, PlanPropertyInfo prop)
	{
		if (localValue == null)
		{
			return "Value is required";
		}
		if (prop.MinValue.HasValue || prop.MaxValue.HasValue)
		{
			double computed;
			if (localValue is int ordinal)
			{
				computed = ordinal;
			}
			else if (localValue is double computed2)
			{
				computed = computed2;
			}
			else
			{
				if (!(localValue is float computed3))
				{
					return null;
				}
				computed = computed3;
			}
			if (prop.MinValue.HasValue && computed < prop.MinValue.Value)
			{
				return $"Must be >= {prop.MinValue.Value}";
			}
			if (prop.MaxValue.HasValue && computed > prop.MaxValue.Value)
			{
				return $"Must be <= {prop.MaxValue.Value}";
			}
		}
		if (localValue is TimeSpan { TotalSeconds: <=0.0 })
		{
			return "Must be > 0";
		}
		return null;
	}

	public static object[] GetEnumValues(Type enumType)
	{
		return Enum.GetValues(enumType).Cast<object>().ToArray();
	}

	public static object GetNextEnumValue(object current, Type enumType, int direction)
	{
		object[] enumValues = GetEnumValues(enumType);
		int indexOf = Array.IndexOf(enumValues, current);
		if (indexOf < 0)
		{
			indexOf = 0;
		}
		int indexOfDirectionTerm = (indexOf + direction + enumValues.Length) % enumValues.Length;
		return enumValues[indexOfDirectionTerm];
	}

	public static bool ToggleBool(object current)
	{
		if (!(current is bool condition))
		{
			return false;
		}
		return !condition;
	}
}
