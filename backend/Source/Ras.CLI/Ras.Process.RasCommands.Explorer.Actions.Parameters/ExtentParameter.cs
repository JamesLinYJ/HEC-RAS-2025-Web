using System;
using Geospatial.Vectors;

namespace Ras.Process.RasCommands.Explorer.Actions.Parameters;

public class ExtentParameter : IActionParameter
{
	public string Name { get; }

	public string DisplayName { get; }

	public string Description { get; }

	public Type ValueType => typeof(Extent);

	public object DefaultValue { get; }

	public bool IsRequired { get; }

	public ExtentParameter(string name, string displayName, string description = null, bool isRequired = true, Extent? defaultValue = null)
	{
		Name = name;
		DisplayName = displayName;
		Description = description;
		IsRequired = isRequired;
		DefaultValue = defaultValue;
	}

	public string Validate(object localValue)
	{
		if (!(localValue is Extent extent))
		{
			return "Value must be an Extent";
		}
		if (extent.IsNull())
		{
			if (!IsRequired)
			{
				return null;
			}
			return DisplayName + " is required";
		}
		if (extent.Width <= 0.0 || extent.Height <= 0.0)
		{
			return "Extent must have positive width and height";
		}
		return null;
	}
}
