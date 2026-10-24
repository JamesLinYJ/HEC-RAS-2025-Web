using System;
using System.Reflection;

namespace Ras.Process.RasCommands.Explorer.FeatureTable;

public static class FeatureTableDataAccessor
{
	public static object GetValue(object feature, ColumnInfo column)
	{
		if (feature == null || column == null)
		{
			return null;
		}
		if (column.IsField)
		{
			return column.FieldInfo.GetValue(feature);
		}
		if (column.PropertyChain == null || column.PropertyChain.Length == 0)
		{
			return null;
		}
		object feature2 = feature;
		PropertyInfo[] propertyChain = column.PropertyChain;
		foreach (PropertyInfo propertyInfo in propertyChain)
		{
			if (feature2 == null)
			{
				return null;
			}
			feature2 = propertyInfo.GetValue(feature2);
		}
		return feature2;
	}

	public static bool SetValue(object feature, ColumnInfo column, object assignedValue)
	{
		if (feature == null || column?.PropertyChain == null || column.PropertyChain.Length == 0)
		{
			return false;
		}
		if (column.IsReadOnly)
		{
			return false;
		}
		try
		{
			object feature2 = feature;
			for (int i = 0; i < column.PropertyChain.Length - 1; i++)
			{
				object getValue = column.PropertyChain[i].GetValue(feature2);
				if (getValue == null)
				{
					return false;
				}
				feature2 = getValue;
			}
			PropertyInfo propertyInfo = column.PropertyChain[column.PropertyChain.Length - 1];
			object convertValue = ConvertValue(assignedValue, propertyInfo.PropertyType);
			propertyInfo.SetValue(feature2, convertValue);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool TryParseValue(string input, Type targetType, out object result)
	{
		result = null;
		if (input == null)
		{
			return false;
		}
		try
		{
			Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
			if (string.IsNullOrWhiteSpace(input) && Nullable.GetUnderlyingType(targetType) != null)
			{
				result = null;
				return true;
			}
			if (type == typeof(string))
			{
				result = input;
				return true;
			}
			if (type == typeof(int))
			{
				if (int.TryParse(input, out var result2))
				{
					result = result2;
					return true;
				}
				return false;
			}
			if (type == typeof(double))
			{
				if (double.TryParse(input, out var result3))
				{
					result = result3;
					return true;
				}
				if (input.Equals("NaN", StringComparison.OrdinalIgnoreCase))
				{
					result = double.NaN;
					return true;
				}
				if (input.Equals("Inf", StringComparison.OrdinalIgnoreCase))
				{
					result = double.PositiveInfinity;
					return true;
				}
				if (input.Equals("-Inf", StringComparison.OrdinalIgnoreCase))
				{
					result = double.NegativeInfinity;
					return true;
				}
				return false;
			}
			if (type == typeof(float))
			{
				if (float.TryParse(input, out var result4))
				{
					result = result4;
					return true;
				}
				if (input.Equals("NaN", StringComparison.OrdinalIgnoreCase))
				{
					result = float.NaN;
					return true;
				}
				if (input.Equals("Inf", StringComparison.OrdinalIgnoreCase))
				{
					result = float.PositiveInfinity;
					return true;
				}
				if (input.Equals("-Inf", StringComparison.OrdinalIgnoreCase))
				{
					result = float.NegativeInfinity;
					return true;
				}
				return false;
			}
			if (type == typeof(long))
			{
				if (long.TryParse(input, out var result5))
				{
					result = result5;
					return true;
				}
				return false;
			}
			if (type == typeof(bool))
			{
				if (bool.TryParse(input, out var result6))
				{
					result = result6;
					return true;
				}
				if (input.Equals("yes", StringComparison.OrdinalIgnoreCase) || input == "1")
				{
					result = true;
					return true;
				}
				if (input.Equals("no", StringComparison.OrdinalIgnoreCase) || input == "0")
				{
					result = false;
					return true;
				}
				return false;
			}
			if (type == typeof(DateTime))
			{
				if (DateTime.TryParse(input, out var result7))
				{
					result = result7;
					return true;
				}
				return false;
			}
			if (type == typeof(TimeSpan))
			{
				if (TimeSpan.TryParse(input, out var result8))
				{
					result = result8;
					return true;
				}
				return false;
			}
			if (type.IsEnum)
			{
				if (Enum.TryParse(type, input, ignoreCase: true, out object result9))
				{
					result = result9;
					return true;
				}
				return false;
			}
			result = Convert.ChangeType(input, type);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static object ConvertValue(object localValue, Type targetType)
	{
		if (localValue == null)
		{
			return null;
		}
		Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
		if (type.IsAssignableFrom(localValue.GetType()))
		{
			return localValue;
		}
		if (localValue is string input && TryParseValue(input, targetType, out var result))
		{
			return result;
		}
		return Convert.ChangeType(localValue, type);
	}

	public static string FormatValue(object localValue2)
	{
		if (localValue2 == null)
		{
			return "";
		}
		if (!(localValue2 is double d))
		{
			if (!(localValue2 is float f))
			{
				if (!(localValue2 is bool))
				{
					if (!(localValue2 is DateTime dateTime))
					{
						if (!(localValue2 is TimeSpan timeSpan))
						{
							if (localValue2 is Enum localValue)
							{
								return localValue.ToString();
							}
							return localValue2.ToString() ?? "";
						}
						return timeSpan.ToString("d\\.hh\\:mm\\:ss");
					}
					return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
				}
				return ((bool)localValue2) ? "Yes" : "No";
			}
			return FormatFloat(f);
		}
		return FormatDouble(d);
	}

	private static string FormatDouble(double d)
	{
		if (double.IsNaN(d))
		{
			return "NaN";
		}
		if (double.IsPositiveInfinity(d))
		{
			return "Inf";
		}
		if (double.IsNegativeInfinity(d))
		{
			return "-Inf";
		}
		if (Math.Abs(d) >= 1000000000.0 || (Math.Abs(d) < 0.0001 && d != 0.0))
		{
			return d.ToString("E3");
		}
		if (d == Math.Floor(d))
		{
			return d.ToString("F0");
		}
		return d.ToString("F4");
	}

	private static string FormatFloat(float f)
	{
		if (float.IsNaN(f))
		{
			return "NaN";
		}
		if (float.IsPositiveInfinity(f))
		{
			return "Inf";
		}
		if (float.IsNegativeInfinity(f))
		{
			return "-Inf";
		}
		if (Math.Abs(f) >= 1E+09f || (Math.Abs(f) < 0.0001f && f != 0f))
		{
			return f.ToString("E3");
		}
		if (f == MathF.Floor(f))
		{
			return f.ToString("F0");
		}
		return f.ToString("F4");
	}
}
