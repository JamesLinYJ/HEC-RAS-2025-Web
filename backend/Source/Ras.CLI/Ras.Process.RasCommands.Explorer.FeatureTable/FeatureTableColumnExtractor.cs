using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Utility.ORM;

namespace Ras.Process.RasCommands.Explorer.FeatureTable;

public static class FeatureTableColumnExtractor
{
	public static List<ColumnInfo> GetColumns(Type featureRowType)
	{
		List<ColumnInfo> columnInfos = new List<ColumnInfo>();
		ExtractColumnsRecursive(featureRowType, "", new List<PropertyInfo>(), columnInfos);
		if (columnInfos.Count == 0)
		{
			ExtractPlainProperties(featureRowType, columnInfos);
		}
		return columnInfos;
	}

	public static List<ColumnInfo> GetColumns<T>()
	{
		return GetColumns(typeof(T));
	}

	private static void ExtractColumnsRecursive(Type type, string prefix, List<PropertyInfo> propertyChain, List<ColumnInfo> columns)
	{
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (PropertyInfo propertyInfo in properties)
		{
			if (propertyInfo.GetCustomAttribute<DatasetNameAttribute>() != null)
			{
				continue;
			}
			MetadataAttribute customAttribute = propertyInfo.GetCustomAttribute<MetadataAttribute>();
			if (customAttribute != null)
			{
				List<PropertyInfo> propertyChainList = new List<PropertyInfo>(propertyChain) { propertyInfo };
				columns.Add(new ColumnInfo
				{
					Name = prefix + customAttribute.KeyName,
					PropertyChain = propertyChainList.ToArray(),
					ValueType = propertyInfo.PropertyType,
					IsReadOnly = (!propertyInfo.CanWrite || propertyInfo.SetMethod == null || !propertyInfo.SetMethod.IsPublic)
				});
			}
			else
			{
				ComplexMetadataAttribute customAttribute2 = propertyInfo.GetCustomAttribute<ComplexMetadataAttribute>();
				if (customAttribute2 != null)
				{
					List<PropertyInfo> propertyChain2 = new List<PropertyInfo>(propertyChain) { propertyInfo };
					string prefix2 = prefix + customAttribute2.UsableColumnNamePrefix;
					ExtractColumnsRecursive(propertyInfo.PropertyType, prefix2, propertyChain2, columns);
				}
			}
		}
	}

	private static void ExtractPlainProperties(Type type, List<ColumnInfo> columns)
	{
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
		foreach (PropertyInfo propertyInfo in properties)
		{
			if (propertyInfo.CanRead && propertyInfo.GetIndexParameters().Length == 0)
			{
				Type propertyType = propertyInfo.PropertyType;
				if (IsSimpleType(propertyType))
				{
					columns.Add(new ColumnInfo
					{
						Name = propertyInfo.Name,
						PropertyChain = new PropertyInfo[1] { propertyInfo },
						ValueType = propertyType,
						IsReadOnly = (!propertyInfo.CanWrite || propertyInfo.SetMethod == null || !propertyInfo.SetMethod.IsPublic)
					});
				}
			}
		}
		FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
		foreach (FieldInfo fieldInfo in fields)
		{
			if (!fieldInfo.IsLiteral)
			{
				Type fieldType = fieldInfo.FieldType;
				if (IsSimpleType(fieldType))
				{
					columns.Add(new ColumnInfo
					{
						Name = fieldInfo.Name,
						FieldInfo = fieldInfo,
						ValueType = fieldType,
						IsReadOnly = fieldInfo.IsInitOnly
					});
				}
			}
		}
	}

	private static bool IsSimpleType(Type type)
	{
		Type underlyingType = Nullable.GetUnderlyingType(type);
		if (underlyingType != null)
		{
			type = underlyingType;
		}
		if (!type.IsPrimitive && !type.IsEnum && !(type == typeof(string)) && !(type == typeof(decimal)) && !(type == typeof(DateTime)) && !(type == typeof(TimeSpan)))
		{
			return type == typeof(Guid);
		}
		return true;
	}

	public static bool IsFeatureLayer(object dataSource)
	{
		if (dataSource == null)
		{
			return false;
		}
		return IsFeatureLayerType(dataSource.GetType());
	}

	public static bool IsFeatureLayerType(Type type)
	{
		Type type2 = type;
		while (type2 != null)
		{
			if (type2.IsGenericType)
			{
				Type genericTypeDefinition = type2.GetGenericTypeDefinition();
				if (genericTypeDefinition.Name.StartsWith("FeatureLayerBase") || genericTypeDefinition.Name.StartsWith("TableLayerBase") || genericTypeDefinition.Name.StartsWith("GeometryLayerBase"))
				{
					return true;
				}
			}
			type2 = type2.BaseType;
		}
		return false;
	}

	public static Type GetFeatureRowType(Type layerType)
	{
		Type type = layerType;
		while (type != null)
		{
			if (type.IsGenericType)
			{
				Type genericTypeDefinition = type.GetGenericTypeDefinition();
				if (genericTypeDefinition.Name.StartsWith("FeatureLayerBase") || genericTypeDefinition.Name.StartsWith("TableLayerBase") || genericTypeDefinition.Name.StartsWith("GeometryLayerBase"))
				{
					return type.GetGenericArguments()[0];
				}
			}
			type = type.BaseType;
		}
		Type[] interfaces = layerType.GetInterfaces();
		foreach (Type type2 in interfaces)
		{
			if (type2.IsGenericType && type2.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
			{
				return type2.GetGenericArguments()[0];
			}
		}
		return null;
	}

	public static Type GetFeatureRowType(object layer)
	{
		if (layer == null)
		{
			return null;
		}
		return GetFeatureRowType(layer.GetType());
	}

	public static IReadOnlyList<object> GetFeatures(object layer)
	{
		if (layer == null)
		{
			return Array.Empty<object>();
		}
		Type type = layer.GetType();
		PropertyInfo property = type.GetProperty("Count");
		if (property == null)
		{
			return Array.Empty<object>();
		}
		int getValue = (int)property.GetValue(layer);
		if (getValue == 0)
		{
			return Array.Empty<object>();
		}
		PropertyInfo property2 = type.GetProperty("Item");
		if (property2 == null)
		{
			PropertyInfo property3 = type.GetProperty("Features");
			if (property3 != null && property3.GetValue(layer) is IList items)
			{
				object[] objectValues = new object[items.Count];
				for (int i = 0; i < items.Count; i++)
				{
					objectValues[i] = items[i];
				}
				return objectValues;
			}
			return Array.Empty<object>();
		}
		object[] objectValuesArray = new object[getValue];
		for (int j = 0; j < getValue; j++)
		{
			objectValuesArray[j] = property2.GetValue(layer, new object[1] { j });
		}
		return objectValuesArray;
	}

	public static string GetLayerName(object layer)
	{
		if (layer == null)
		{
			return "Unknown";
		}
		Type type = layer.GetType();
		PropertyInfo property = type.GetProperty("Name");
		if (property != null)
		{
			string getValue = property.GetValue(layer) as string;
			if (!string.IsNullOrEmpty(getValue))
			{
				return getValue;
			}
		}
		string substringTerm = type.Name;
		if (substringTerm.EndsWith("Layer"))
		{
			substringTerm = substringTerm.Substring(0, substringTerm.Length - 5);
		}
		return substringTerm;
	}
}
