using System;

namespace Ras.Process.RasCommands.Explorer.Actions.Parameters;

public class BoolParameter : IActionParameter
{
	public string Name { get; }

	public string DisplayName { get; }

	public string Description { get; }

	public Type ValueType => typeof(bool);

	public object DefaultValue { get; }

	public bool IsRequired => false;

	public BoolParameter(string name, string displayName, string description = null, bool defaultValue = false)
	{
		Name = name;
		DisplayName = displayName;
		Description = description;
		DefaultValue = defaultValue;
	}

	public string Validate(object localValue)
	{
		if (!(localValue is bool))
		{
			return "Value must be a boolean";
		}
		return null;
	}
}
