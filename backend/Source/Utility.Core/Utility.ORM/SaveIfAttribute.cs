using System;

namespace Utility.ORM;

[AttributeUsage(AttributeTargets.Property)]
public class SaveIfAttribute : Attribute
{
	public string PropertyName { get; set; }

	public bool CleanOldStorage { get; set; } = true;

	public SaveIfAttribute(string propertyName)
	{
		PropertyName = propertyName;
	}
}
