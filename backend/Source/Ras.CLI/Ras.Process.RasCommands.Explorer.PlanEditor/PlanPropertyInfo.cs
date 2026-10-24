using System;
using System.Reflection;

namespace Ras.Process.RasCommands.Explorer.PlanEditor;

public class PlanPropertyInfo
{
	public string DisplayName { get; set; }

	public string PropertyName { get; set; }

	public PropertyInfo ReflectionInfo { get; set; }

	public Type ValueType { get; set; }

	public string Description { get; set; }

	public double? MinValue { get; set; }

	public double? MaxValue { get; set; }

	public bool IsReadOnly { get; set; }

	public bool IsBool => ValueType == typeof(bool);

	public bool IsEnum => ValueType.IsEnum;

	public bool IsNumeric
	{
		get
		{
			if (!(ValueType == typeof(int)) && !(ValueType == typeof(double)))
			{
				return ValueType == typeof(float);
			}
			return true;
		}
	}

	public bool IsTimeSpan => ValueType == typeof(TimeSpan);

	public bool IsDateTime => ValueType == typeof(DateTime);
}
