using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Utility.Attributes;

namespace Visual.Observables;

public class NotifyPropertyChangedForTableColumnAttribute : Attribute
{
	private static readonly Dictionary<Type, Dictionary<string, string>> _mapsByType = new Dictionary<Type, Dictionary<string, string>>();

	public string ColumnName { get; set; }

	public NotifyPropertyChangedForTableColumnAttribute(string columnName)
	{
		ColumnName = columnName;
	}

	public static IEnumerable<PropertyInfo> GetAllPropertiesWithAttribute(Type classType)
	{
		return classType.GetMembersWithAttribute(typeof(NotifyPropertyChangedForTableColumnAttribute), true).Cast<PropertyInfo>();
	}

	public static Dictionary<string, string> GetMapForType(Type classType)
	{
		if (!_mapsByType.TryGetValue(classType, out var mapsByType))
		{
			mapsByType = BuildMap(classType);
			_mapsByType[classType] = mapsByType;
		}
		return mapsByType;
	}

	public static Dictionary<string, string> BuildMap(Type classType)
	{
		Dictionary<string, string> stringByString = new Dictionary<string, string>();
		foreach (PropertyInfo allPropertiesWithAttribute in GetAllPropertiesWithAttribute(classType))
		{
			stringByString[allPropertiesWithAttribute.Name] = AttributeHelpers.GetAttribute<NotifyPropertyChangedForTableColumnAttribute>(allPropertiesWithAttribute).ColumnName;
		}
		return stringByString;
	}
}
