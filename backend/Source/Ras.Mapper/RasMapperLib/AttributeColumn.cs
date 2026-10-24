using System;

namespace RasMapperLib;

public class AttributeColumn
{
	public readonly string Name;

	public readonly Type Type;

	public readonly bool IsReadonly;

	public AttributeColumn(string name, Type type, bool isReadOnly)
	{
		Name = name;
		Type = type;
		IsReadonly = isReadOnly;
	}
}
