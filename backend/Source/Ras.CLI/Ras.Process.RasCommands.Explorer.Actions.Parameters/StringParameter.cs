using System;

namespace Ras.Process.RasCommands.Explorer.Actions.Parameters;

public class StringParameter : IActionParameter
{
	public string Name { get; }

	public string DisplayName { get; }

	public string Description { get; }

	public Type ValueType => typeof(string);

	public object DefaultValue { get; }

	public bool IsRequired { get; }

	public StringParameter(string name, string displayName, string description = null, bool isRequired = true, string defaultValue = null)
	{
		Name = name;
		DisplayName = displayName;
		Description = description;
		IsRequired = isRequired;
		DefaultValue = defaultValue;
	}

	public string Validate(object localValue)
	{
		if (!(localValue is string textValue))
		{
			return "Value must be a string";
		}
		if (IsRequired && string.IsNullOrWhiteSpace(textValue))
		{
			return DisplayName + " is required";
		}
		return null;
	}
}
