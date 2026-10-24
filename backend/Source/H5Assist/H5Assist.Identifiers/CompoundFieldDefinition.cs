using System;
using System.Diagnostics;

namespace H5Assist.Identifiers;

[DebuggerDisplay("{Name}: {Type.Name} ({Size} bytes)")]
public class CompoundFieldDefinition
{
	public readonly Type Type;

	public readonly string Name;

	public readonly int Size;

	public readonly int Offset;

	public CompoundFieldDefinition(Type t, string name, int size, int offset)
	{
		Type = t;
		Name = name;
		Size = size;
		Offset = offset;
	}
}
