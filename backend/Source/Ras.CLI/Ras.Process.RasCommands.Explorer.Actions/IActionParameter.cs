using System;

namespace Ras.Process.RasCommands.Explorer.Actions;

public interface IActionParameter
{
	string Name { get; }

	string DisplayName { get; }

	string Description { get; }

	Type ValueType { get; }

	object DefaultValue { get; }

	bool IsRequired { get; }

	string Validate(object localValue);
}
