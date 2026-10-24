using System;

namespace Utility.ORM;

[AttributeUsage(AttributeTargets.Property)]
public class ComplexMetadataAttribute : Attribute
{
	public bool Optional { get; set; }

	public string ColumnNamePrefix { get; set; }

	public string UsableColumnNamePrefix
	{
		get
		{
			if (string.IsNullOrEmpty(ColumnNamePrefix))
			{
				return string.Empty;
			}
			return ColumnNamePrefix + " ";
		}
	}

	public ComplexMetadataAttribute()
	{
	}

	public ComplexMetadataAttribute(string namePrefix)
	{
		ColumnNamePrefix = namePrefix;
	}
}
