using System;

namespace Utility.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnumDisplayName : Attribute
{
	public string DisplayName { get; }

	public EnumDisplayName(string displayName)
	{
		DisplayName = displayName;
	}
}
