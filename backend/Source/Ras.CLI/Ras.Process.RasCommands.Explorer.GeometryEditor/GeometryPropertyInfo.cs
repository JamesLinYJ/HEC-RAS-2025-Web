using System;
using System.Reflection;

namespace Ras.Process.RasCommands.Explorer.GeometryEditor;

public class GeometryPropertyInfo
{
	public string DisplayName { get; set; }

	public string PropertyName { get; set; }

	public PropertyInfo ReflectionInfo { get; set; }

	public Type ValueType { get; set; }

	public string Description { get; set; }

	public bool IsReadOnly { get; set; }

	public bool IsString => ValueType == typeof(string);
}
