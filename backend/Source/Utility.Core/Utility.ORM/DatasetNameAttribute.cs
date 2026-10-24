using System;

namespace Utility.ORM;

[AttributeUsage(AttributeTargets.Property)]
public class DatasetNameAttribute : Attribute
{
	public string DatasetName { get; set; }

	public bool Optional { get; set; }

	public bool ForceWriteIfDefault { get; set; }

	public DatasetNameAttribute(string dsetName)
	{
		DatasetName = dsetName;
		Optional = false;
	}
}
