using System;

namespace Ras.Process.RasCommands.Explorer.Actions.Parameters;

public class NumberParameter : IActionParameter
{
	public string Name { get; }

	public string DisplayName { get; }

	public string Description { get; }

	public Type ValueType { get; }

	public object DefaultValue { get; }

	public bool IsRequired { get; }

	public double? MinValue { get; }

	public double? MaxValue { get; }

	public NumberParameter(string name, string displayName, string description = null, bool isRequired = true, double? defaultValue = null, double? minValue = null, double? maxValue = null, bool useInteger = false)
	{
		Name = name;
		DisplayName = displayName;
		Description = description;
		IsRequired = isRequired;
		DefaultValue = ((useInteger && defaultValue.HasValue) ? ((object)(int)defaultValue.Value) : ((object)defaultValue));
		MinValue = minValue;
		MaxValue = maxValue;
		ValueType = (useInteger ? typeof(int) : typeof(double));
	}

	public static NumberParameter Integer(string name, string displayName, string description = null, bool isRequired = true, int? defaultValue = null, int? minValue = null, int? maxValue = null)
	{
		return new NumberParameter(name, displayName, description, isRequired, defaultValue, minValue, maxValue, useInteger: true);
	}

	public static NumberParameter Double(string name, string displayName, string description = null, bool isRequired = true, double? defaultValue = null, double? minValue = null, double? maxValue = null)
	{
		return new NumberParameter(name, displayName, description, isRequired, defaultValue, minValue, maxValue);
	}

	public string Validate(object localValue)
	{
		double computed;
		if (localValue is int ordinal)
		{
			computed = ordinal;
		}
		else
		{
			if (!(localValue is double computed2))
			{
				return "Value must be a number";
			}
			computed = computed2;
		}
		if (MinValue.HasValue && computed < MinValue.Value)
		{
			return $"{DisplayName} must be >= {MinValue.Value}";
		}
		if (MaxValue.HasValue && computed > MaxValue.Value)
		{
			return $"{DisplayName} must be <= {MaxValue.Value}";
		}
		return null;
	}
}
