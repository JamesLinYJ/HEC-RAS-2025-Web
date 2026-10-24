using System;
using System.Collections.Generic;
using Utility.Memory;

namespace Geospatial.IO.Network.ArcGisRest;

internal static class ArcGisRestAttributeTableHelper
{
	internal static Type[] BuildColumns(Table table, List<ArcGisField> fields)
	{
		if (fields == null)
		{
			return Array.Empty<Type>();
		}
		Type[] typeValues = new Type[fields.Count];
		for (int i = 0; i < fields.Count; i++)
		{
			typeValues[i] = GetClrType(fields[i].Type);
			table.AddColumn(fields[i].Name, typeValues[i]);
		}
		return typeValues;
	}

	internal static object[] BuildRow(ArcGisFeature feature, List<ArcGisField> fields, Type[] columnTypes)
	{
		if (fields == null)
		{
			return Array.Empty<object>();
		}
		object[] objectValues = new object[fields.Count];
		for (int i = 0; i < fields.Count; i++)
		{
			if (feature.Attributes != null && feature.Attributes.TryGetValue(fields[i].Name, out var localValue))
			{
				objectValues[i] = JsonValueExtractor.TryParseToTypedValue_OrDefault(localValue, columnTypes[i]);
			}
			else
			{
				objectValues[i] = JsonValueExtractor.DefaultFor(columnTypes[i]);
			}
		}
		return objectValues;
	}

	internal static object[] CopyRow(object[] row)
	{
		object[] objectValues = new object[row.Length];
		Array.Copy(row, objectValues, row.Length);
		return objectValues;
	}

	private static Type GetClrType(string esriFieldType)
	{
		switch (esriFieldType)
		{
		case "esriFieldTypeSmallInteger":
			return typeof(short);
		case "esriFieldTypeInteger":
		case "esriFieldTypeOID":
			return typeof(int);
		case "esriFieldTypeSingle":
			return typeof(float);
		case "esriFieldTypeDouble":
			return typeof(double);
		case "esriFieldTypeDate":
			return typeof(long);
		default:
			return typeof(string);
		}
	}
}
