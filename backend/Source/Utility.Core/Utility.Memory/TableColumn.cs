using System;
using System.Diagnostics;
using System.IO;
using Utility.Extensions;
using Utility.Systems;

namespace Utility.Memory;

[DebuggerDisplay("{DebuggerDisplay()}")]
public class TableColumn : IStreamableObject
{
	public string Name { get; private set; }

	public Type Type { get; private set; }

	public bool IsReadOnly { get; }

	internal TableColumn()
	{
	}

	public TableColumn(string columnName, Type columnType, bool isReadOnly)
	{
		Name = columnName;
		Type = columnType;
		IsReadOnly = isReadOnly;
	}

	public TableColumn Clone()
	{
		return new TableColumn(Name, Type, IsReadOnly);
	}

	private string DebuggerDisplay()
	{
		return Name.ToString() + " : " + Type.Name.ToString();
	}

	internal void DebugSetType(Type type)
	{
		Type = type;
	}

	internal void Rename(string newName)
	{
		Name = newName;
	}

	public void StreamOut(BinaryWriter bw)
	{
		if (Type.IsPrimitive || Type == typeof(string) || MemoryExtensions.Contains(Type.GetInterfaces(), typeof(IStreamableObject), null))
		{
			bw.WriteStringA(Name);
			bw.WriteStringA(Type.AssemblyQualifiedName);
			return;
		}
		throw new NotSupportedException();
	}

	public void StreamIn(BinaryReader br)
	{
		Name = br.ReadStringA();
		string typeName = br.ReadStringA();
		Type = Type.GetType(typeName);
	}
}
