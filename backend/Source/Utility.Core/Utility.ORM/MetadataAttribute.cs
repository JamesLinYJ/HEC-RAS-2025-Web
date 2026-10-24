using System;

namespace Utility.ORM;

[AttributeUsage(AttributeTargets.Property)]
public class MetadataAttribute : Attribute
{
	public string PathRelativeTarget { get; set; }

	public string KeyName { get; set; }

	public string[] LegacyKeyNames { get; set; }

	public bool Optional { get; set; }

	public bool SetValueToDefaultIfKeyMissing { get; set; } = true;

	public object DefaultValue { get; set; }

	public MetadataAttribute()
	{
	}

	public MetadataAttribute(string attributeName)
	{
		KeyName = attributeName;
	}
}
