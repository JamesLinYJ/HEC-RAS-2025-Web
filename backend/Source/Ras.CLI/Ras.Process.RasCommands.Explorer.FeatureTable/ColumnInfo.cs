using System;
using System.Reflection;

namespace Ras.Process.RasCommands.Explorer.FeatureTable;

public class ColumnInfo
{
	public string Name { get; set; }

	public PropertyInfo[] PropertyChain { get; set; }

	public FieldInfo FieldInfo { get; set; }

	public Type ValueType { get; set; }

	public bool IsReadOnly { get; set; }

	public bool IsField => FieldInfo != null;
}
